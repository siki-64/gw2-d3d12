import argparse
import json
import re
import sys
import tempfile
from pathlib import Path

INLINE_TYPES = {
    5: ("byte", 1), 6: ("Gw2PackRaw4", 4), 7: ("double", 8), 10: ("uint", 4), 12: ("float", 4),
    13: ("Vector2", 8), 14: ("Vector3", 12), 15: ("Vector4", 16), 17: ("ulong", 8), 21: ("ushort", 2),
    22: ("Gw2PackRaw16", 16), 23: ("Gw2PackRgb", 3), 24: ("Gw2PackUInt2", 8), 25: ("Gw2PackUInt4", 16),
    26: ("Gw2PackUShort3", 6), 36: ("uint", 4), 37: ("ulong", 8),
}


def pascal(name):
    return re.sub(r"(^|_)([a-z])", lambda m: m.group(2).upper(), name) if name else "Value"


class LayoutWriter:
    def __init__(self, catalog, pointer_size):
        self.types = catalog["types"]
        self.pointer_size = pointer_size
        self.sizes = {}
        self.order = []
        self.names = {}

    def element_name(self, type_id):
        schema = self.types[type_id]
        fields = schema["fields"]
        if len(fields) == 1 and fields[0]["name"] == "":
            return self.field_type(fields[0])[0]
        self.visit(type_id)
        return self.struct_name(type_id)

    def size(self, type_id):
        if type_id not in self.sizes:
            self.sizes[type_id] = sum(self.field_size(field) for field in self.types[type_id]["fields"])
        return self.sizes[type_id]

    def field_size(self, field):
        kind, pointer = field["kind"], self.pointer_size
        if kind in INLINE_TYPES:
            return INLINE_TYPES[kind][1]
        if kind == 1:
            return field["count"] * self.size(field["type"])
        if kind in (2, 3, 28):
            return 4 + pointer
        if kind in (11, 16, 18, 19, 27):
            return pointer
        if kind in (20, 29):
            return self.size(field["type"])
        raise ValueError(f"unsupported kind {kind}")

    def field_type(self, field):
        kind = field["kind"]
        if kind in INLINE_TYPES:
            return INLINE_TYPES[kind][0], None
        if kind == 1:
            return self.element_name(field["type"]), field["count"]
        if kind == 2:
            return f"Gw2PackArray<{self.element_name(field['type'])}>", None
        if kind == 3:
            return f"Gw2PackPointerArray<{self.element_name(field['type'])}>", None
        if kind == 16:
            return f"Gw2PackPointer<{self.element_name(field['type'])}>", None
        if kind in (20, 29):
            return self.element_name(field["type"]), None
        if kind in (11, 27):
            return "Gw2PackFileReferencePointer", None
        if kind == 18:
            return "Gw2PackWideString", None
        if kind == 19:
            return "Gw2PackString", None
        if kind == 28:
            for variant in field.get("variants") or []:
                self.visit(variant)
            return "Gw2PackVariantPointer", None
        raise ValueError(f"unsupported kind {kind}")

    def struct_name(self, type_id):
        if type_id not in self.names:
            name = self.types[type_id]["name"]
            taken = set(self.names.values())
            self.names[type_id] = name if name not in taken else f"{name}_{type_id}"
        return self.names[type_id]

    def visit(self, type_id):
        if type_id in self.order:
            return
        self.order.append(type_id)
        self.struct_name(type_id)
        for field in self.types[type_id]["fields"]:
            self.field_type(field)

    def struct(self, type_id):
        schema = self.types[type_id]
        lines = [f"[StructLayout(LayoutKind.Explicit, Size = 0x{self.size(type_id):X})]",
                 f"internal struct {self.struct_name(type_id)}", "{"]
        offset = 0
        for field in schema["fields"]:
            type_name, count = self.field_type(field)
            name = pascal(field["name"])
            if count is None:
                lines.append(f"    [FieldOffset(0x{offset:02X})] internal {type_name} {name};")
            else:
                stride = self.size(field["type"])
                for index in range(count):
                    lines.append(f"    [FieldOffset(0x{offset + index * stride:02X})] internal {type_name} {name}{index};")
            offset += self.field_size(field)
        lines.append("}")
        return "\n".join(lines)


def source_text(namespace, structs):
    return "\n".join([
        "using System.Numerics;",
        "using System.Runtime.InteropServices;",
        "using Gw2.Exe.Objects.Dat;",
        "",
        f"namespace {namespace};",
        "",
        "\n\n".join(structs),
        "",
    ])


def layout_structs(catalog, root, pointer_size):
    writer = LayoutWriter(catalog, pointer_size)
    writer.visit(root)
    for type_id in list(writer.order):
        writer.visit(type_id)
    return [(writer.struct_name(type_id), writer.struct(type_id)) for type_id in writer.order]


def write_layout(catalog, root, pointer_size, namespace, output):
    structs = layout_structs(catalog, root, pointer_size)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(source_text(namespace, [text for _, text in structs]), newline="\n")
    return len(structs)


def referenced_names(text, names):
    return set(re.findall(r"\b\w+\b", text.split("\n", 2)[2])) & names


def shared_structs(layouts):
    counts = {}
    for structs in layouts:
        for name, text in structs.items():
            counts.setdefault(name, {}).setdefault(text, 0)
            counts[name][text] += 1
    shared = {}
    for name, texts in counts.items():
        text, count = max(texts.items(), key=lambda item: (item[1], item[0]))
        if count > 1:
            shared[name] = text
    changed = True
    while changed:
        changed = False
        for name in list(shared):
            for structs in layouts:
                if structs.get(name) != shared[name]:
                    continue
                if any(shared.get(reference) != structs[reference] for reference in referenced_names(shared[name], set(structs))):
                    del shared[name]
                    changed = True
                    break
    return shared


def chunk_name(chunk):
    return chunk.rstrip("\x00 ")


def write_all(catalog, pointer_size, directory):
    types = catalog["types"]
    usages = {}
    for registration in catalog["registrations"]:
        for version, root in registration["versions"].items():
            usages.setdefault(root, []).append((chunk_name(registration["chunk"]), int(version)))
    by_name = {}
    for root in usages:
        by_name.setdefault(types[root]["name"], []).append(root)
    files = []
    for root, usage in sorted(usages.items(), key=lambda item: (re.sub(r"V\d+$", "", types[item[0]]["name"]), min(v for _, v in item[1]))):
        name = types[root]["name"]
        family = re.sub(r"V\d+$", "", name)
        stem = name if len(by_name[name]) == 1 else f"{name}_V{min(v for _, v in usage)}"
        files.append((family, stem, usage, dict(layout_structs(catalog, root, pointer_size))))
    shared = shared_structs([structs for _, _, _, structs in files])
    rows = []
    for family, stem, usage, structs in files:
        local = [text for name, text in structs.items() if shared.get(name) != text]
        output = Path(directory) / family / f"{stem}.cs"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(source_text(f"Gw2.Exe.Objects.Dat.{family}.{stem}", local), newline="\n")
        chunks = sorted({chunk for chunk, _ in usage})
        versions = sorted({version for _, version in usage})
        rows.append((family, f"{family}/{stem}.cs", ", ".join(f"`{c}`" for c in chunks), version_ranges(versions), len(structs), len(local)))
    (Path(directory) / "Shared.cs").write_text(source_text("Gw2.Exe.Objects.Dat", [shared[name] for name in sorted(shared)]), newline="\n")
    index = ["| Family | File | Chunks | Versions | Structs | Local |", "| --- | --- | --- | --- | --- | --- |"]
    index += [f"| {family} | `{file}` | {chunks} | {versions} | {count} | {local} |" for family, file, chunks, versions, count, local in rows]
    (Path(directory) / "INDEX.md").write_text("\n".join(index) + "\n", newline="\n")


def check_all(catalog, pointer_size, directory):
    with tempfile.TemporaryDirectory() as regenerated:
        write_all(catalog, pointer_size, regenerated)
        expected = {path.relative_to(regenerated): path.read_bytes() for path in Path(regenerated).rglob("*") if path.is_file()}
    committed = {path.relative_to(directory): path.read_bytes() for path in Path(directory).rglob("*") if path.is_file()}
    differences = sorted(path.as_posix() for path in expected.keys() | committed.keys() if expected.get(path) != committed.get(path))
    print("\n".join(f"{directory}/{path} differs from the generator output" for path in differences) or "ok")
    return 1 if differences else 0


def version_ranges(versions):
    ranges, start = [], versions[0]
    for previous, current in zip(versions, versions[1:] + [None]):
        if current != previous + 1 if current is not None else True:
            ranges.append(str(start) if start == previous else f"{start}–{previous}")
            start = current
    return ", ".join(ranges)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--all", metavar="DIRECTORY")
    parser.add_argument("--chunk")
    parser.add_argument("--version")
    parser.add_argument("--root")
    parser.add_argument("--pointer-size", type=int, default=8)
    parser.add_argument("--namespace")
    parser.add_argument("--output")
    parser.add_argument("--check", metavar="DIRECTORY")
    arguments = parser.parse_args()

    catalog = json.loads(Path(arguments.catalog).read_text())
    if arguments.check:
        sys.exit(check_all(catalog, arguments.pointer_size, arguments.check))
    if arguments.all:
        write_all(catalog, arguments.pointer_size, arguments.all)
        return
    roots = [r["versions"][arguments.version] for r in catalog["registrations"]
             if chunk_name(r["chunk"]) == chunk_name(arguments.chunk) and arguments.version in r["versions"]]
    if arguments.root:
        roots = [root for root in roots if catalog["types"][root]["name"] == arguments.root]
    root = roots[0]
    write_layout(catalog, root, arguments.pointer_size, arguments.namespace, Path(arguments.output))

if __name__ == "__main__":
    main()
