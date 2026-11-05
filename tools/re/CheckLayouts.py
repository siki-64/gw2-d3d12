import glob
import collections
import json
import os
import re
import sys

import ObjectLayoutCatalog

PRIMITIVE_SIZES = {"nint": 8, "nuint": 8, "long": 8, "ulong": 8, "double": 8, "int": 4, "uint": 4, "float": 4,
                   "short": 2, "ushort": 2, "byte": 1, "sbyte": 1, "bool": 1,
                   "Vector2": 8, "Vector3": 12, "Vector4": 16, "Quaternion": 16, "Guid": 16, "EffectContentKey": 16}
FOUR_BYTE_ALIGNED_TYPES = {"Vector2", "Vector3", "Vector4", "Quaternion", "Guid"}
PACKED_RECEIVE_MESSAGE = re.compile(r"^(CmbtCliBuff\w*Message|Pvp\w*Update)$")
ENUM_HEADER = re.compile(r"enum (\w+)(?:\s*:\s*(\w+))?")
STRUCT_HEADER = re.compile(r"(?m)^(?P<indent> *)\[(?:StructLayout|VirtualLayout)\(LayoutKind\.(?P<kind>\w+)(?:, Size = (?P<size>[^,)\]]+))?(?:, SlotCount = \d+)?\)\]\s*internal (?:unsafe )?(?:partial )?struct (?P<name>\w+)")
INLINE_ARRAY = re.compile(r"\[InlineArray\((\w+)\)\]\s*internal (?:unsafe )?struct (\w+)\s*\{\s*(?:internal|private) ([\w*]+) \w+;")
SIZE_CONSTANT = re.compile(r"internal const int Size = ([^;]+);")
ARCHIVE_ROOT = "src/Dat/Gw2.Dat.Content"
ARCHIVE_ENUM = re.compile(r"public enum (?P<name>\w+)(?:\s*:\s*(?P<type>\w+))?\s*\{(?P<body>[^}]*)\}")
ARCHIVE_CONSTANT_CLASS = re.compile(r"public static class (?P<name>\w+)\s*\{(?P<body>.*?)^\}", re.S | re.M)
ARCHIVE_CONSTANT = re.compile(r"public const \w+ (\w+) = ([^;]+);")
ENUM_DECLARATION = re.compile(r"(?m)^[ \t]*internal enum (?P<name>\w+)")
VALUE_TABLE_ORIGIN = re.compile(r"^Origin: (?:server|client)\.$")
SHARED_MEMBERS_FOR_DUPLICATE = 2


def archive_value_sets():
    value_sets = {}
    for path in glob.glob(ARCHIVE_ROOT + "/**/*.cs", recursive=True):
        text = open(path, encoding="utf-8").read()
        for match in ARCHIVE_ENUM.finditer(text):
            PRIMITIVE_SIZES.setdefault(match["name"], PRIMITIVE_SIZES[match["type"] or "int"])
            value_sets[match["name"]] = {name: ObjectLayoutCatalog.evaluate(value) for name, value in re.findall(r"(\w+)\s*=\s*([^,\s]+)", match["body"])}
        for match in ARCHIVE_CONSTANT_CLASS.finditer(text):
            members = {name: ObjectLayoutCatalog.evaluate(value) for name, value in ARCHIVE_CONSTANT.findall(match["body"])}
            if members:
                value_sets[match["name"]] = members
    return value_sets


def enums_without_origin(root):
    problems = []
    for path in sorted(ObjectLayoutCatalog.object_paths(root)):
        for match in ENUM_DECLARATION.finditer(open(path, encoding="utf-8").read()):
            problems.append(f"{path}: enum {match['name']} belongs in {ObjectLayoutCatalog.ENUM_ROOT}/Server or /Client; archive values belong in {ARCHIVE_ROOT}")
    origin_folders = tuple(f"{ObjectLayoutCatalog.ENUM_ROOT}/{origin}/" for origin in ObjectLayoutCatalog.ENUM_ORIGINS)
    for path in sorted(ObjectLayoutCatalog.enum_paths()):
        if not path.replace("\\", "/").startswith(origin_folders):
            problems.append(f"{path}: not under an origin folder ({', '.join(ObjectLayoutCatalog.ENUM_ORIGINS)})")
    current = None
    for line in open(ObjectLayoutCatalog.VALUE_TABLES, encoding="utf-8"):
        header = ObjectLayoutCatalog.VALUE_TABLE_HEADER.match(line)
        if header:
            if current:
                problems.append(f"{ObjectLayoutCatalog.VALUE_TABLES}: {current} has no Origin line")
            current = header[1]
        elif current and VALUE_TABLE_ORIGIN.match(line.strip()):
            current = None
    if current:
        problems.append(f"{ObjectLayoutCatalog.VALUE_TABLES}: {current} has no Origin line")
    return problems


def enums_repeated_from_archive(root, archive_sets):
    problems = []
    for layout_type in ObjectLayoutCatalog.load(root) + ObjectLayoutCatalog.load_value_tables():
        if layout_type.kind != "enum":
            continue
        for archive_name, archive_members in archive_sets.items():
            shared = [name for name, value in layout_type.members.items() if archive_members.get(name) == value]
            if len(shared) >= SHARED_MEMBERS_FOR_DUPLICATE:
                problems.append(f"{layout_type.path}: {layout_type.qualified_name} repeats {archive_name} ({', '.join(shared)}); use the archive type")
    return problems


def struct_body(text, start):
    depth = 0
    for index in range(start, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index]
    return ""


def collect_structs(root):
    struct_sizes = {}
    explicit_structs = []
    inline_arrays = []
    file_struct_sizes = {}
    for path in ObjectLayoutCatalog.layout_paths(root):
        text = open(path, encoding="utf-8").read()
        constants = dict(re.findall(r"const int (\w+) = ([^;]+);", text))
        inline_arrays += [(eval(constants.get(count, count)), name, element_type) for count, name, element_type in INLINE_ARRAY.findall(text)]
        for name, underlying_type in ENUM_HEADER.findall(text):
            PRIMITIVE_SIZES[name] = PRIMITIVE_SIZES[underlying_type or "int"]
        local_sizes = file_struct_sizes.setdefault(path, {})
        size_constant = SIZE_CONSTANT.search(text)
        for header in STRUCT_HEADER.finditer(text):
            size_text = header["size"]
            if size_text == "Size" and size_constant:
                size_text = size_constant[1]
            size = eval(size_text) if size_text else None
            struct_sizes.setdefault(header["name"], size)
            local_sizes[header["name"]] = size
            if header["kind"] != "Explicit":
                continue
            field_indent = header["indent"] + "    "
            field_pattern = re.compile(r"(?m)^" + field_indent + r"\[FieldOffset\((0x[0-9A-Fa-f]+|\d+)\)(?:, Base)?\] internal (?:fixed )?([\w*.]+) \w+(?:\[([^\]]+)\])?")
            body = struct_body(text, header.end())
            explicit_structs.append((path, header["name"], size, field_pattern.findall(body), local_sizes))
    for count, name, element_type in inline_arrays:
        element_size = 8 if element_type.endswith("*") else PRIMITIVE_SIZES.get(element_type, struct_sizes.get(element_type))
        struct_sizes[name] = int(count) * element_size if element_size else None
    for path, name, size, fields, local_sizes in explicit_structs:
        if size is None and fields:
            struct_sizes[name] = local_sizes[name] = field_extent(fields, local_sizes, struct_sizes)
    return struct_sizes, explicit_structs


def field_width(field_type, element_count, local_sizes, struct_sizes):
    if field_type.endswith("*"):
        return 8
    field_type = field_type.rpartition(".")[2]
    width = PRIMITIVE_SIZES.get(field_type, local_sizes.get(field_type, struct_sizes.get(field_type)))
    if width is not None and element_count:
        width *= eval(element_count)
    return width


def field_extent(fields, local_sizes, struct_sizes):
    widths = [(int(offset, 0), field_width(field_type, count, local_sizes, struct_sizes)) for offset, field_type, count in fields]
    if any(width is None for _, width in widths):
        return None
    return max(offset + width for offset, width in widths)


SYMBOLS_PATTERN = "gw2-research/research/*/symbols.json"
BUILD_MENTION = re.compile(r"// Build (\d+)\b")


def reference_build():
    symbols_paths = sorted(path.replace("\\", "/") for path in glob.glob(SYMBOLS_PATTERN))
    if not symbols_paths:
        return None, []
    if len(symbols_paths) != 1:
        return None, [f"{SYMBOLS_PATTERN}: expected the reference image's symbols only, found {len(symbols_paths)}"]
    symbols_path = symbols_paths[0]
    image_build = str(json.load(open(symbols_path, encoding="utf-8"))["image"]["build"])
    filed_build = os.path.basename(os.path.dirname(symbols_path))
    if image_build != filed_build:
        return image_build, [f"{symbols_path}: image build {image_build} is filed under {filed_build}"]
    return image_build, []


def stamp_problems(root, build):
    problems = []
    if build is None:
        return problems
    stamp = f"// Build {build}."
    paths = ObjectLayoutCatalog.layout_paths(root)
    for path in sorted(paths):
        text = open(path, encoding="utf-8").read()
        if STRUCT_HEADER.search(text) and stamp not in text:
            problems.append(f"{path}: no {stamp} stamp")
        for other in sorted(set(BUILD_MENTION.findall(text)) - {build}):
            problems.append(f"{path}: stamped build {other}, reference build is {build}")
    return problems


IMAGE_ADDRESS = re.compile(r"0x(14[0-9A-Fa-f]{7})")


def image_span(build):
    if build is None:
        return None
    image = json.load(open(f"gw2-research/research/{build}/symbols.json", encoding="utf-8"))["image"]
    return int(image["minAddress"], 16), int(image["maxAddress"], 16)


def cited_addresses(root, span):
    problems = []
    if span is None:
        return problems
    for path in sorted(ObjectLayoutCatalog.object_paths(root)):
        for number, line in enumerate(open(path, encoding="utf-8"), 1):
            if not line.lstrip().startswith("//"):
                continue
            for value in IMAGE_ADDRESS.findall(line):
                if span[0] <= int(value, 16) < span[1]:
                    problems.append(f"{path}:{number}: cites 0x{value}; name it in the database and cite the name")
    return problems


DISTINCTIVE_INTEGER = 0x10000


def is_distinctive(constant_type, value):
    if value is None:
        return False
    if constant_type in ("float", "double"):
        return value not in (0.0, 1.0, -1.0)
    return abs(value) >= DISTINCTIVE_INTEGER


def duplicate_definitions(root):
    layout_types = ObjectLayoutCatalog.load(root)
    problems = []
    paths_by_name = collections.defaultdict(list)
    for layout_type in layout_types:
        paths_by_name[layout_type.qualified_name].append(layout_type.path)
    for name, paths in sorted(paths_by_name.items()):
        if len(paths) > 1:
            problems.append(f"{name}: defined {len(paths)} times ({', '.join(paths)})")
    names_by_value = collections.defaultdict(list)
    for layout_type in layout_types:
        for name, (constant_type, value) in layout_type.constants.items():
            if is_distinctive(constant_type, value):
                names_by_value[(constant_type, value)].append(f"{layout_type.qualified_name}.{name}")
    for (constant_type, value), names in sorted(names_by_value.items(), key=lambda item: str(item[0])):
        if len(names) > 1:
            shown = f"0x{value:X}" if isinstance(value, int) else repr(value)
            problems.append(f"{constant_type} {shown}: one value under {len(names)} names ({', '.join(names)})")
    return problems


def check(root):
    archive_sets = archive_value_sets()
    struct_sizes, explicit_structs = collect_structs(root)
    build, problems = reference_build()
    problems += stamp_problems(root, build) + cited_addresses(root, image_span(build)) + duplicate_definitions(root) + enums_without_origin(root) + enums_repeated_from_archive(root, archive_sets)
    for path, name, size, fields, local_sizes in explicit_structs:
        previous_end, previous_type = 0, None
        for offset, field_type, element_count in sorted((int(o, 0), t, c) for o, t, c in fields):
            is_pointer = field_type.endswith("*")
            if not is_pointer:
                field_type = field_type.rpartition(".")[2]
            width = 8 if is_pointer else PRIMITIVE_SIZES.get(field_type, local_sizes.get(field_type, struct_sizes.get(field_type)))
            if width is None:
                problems.append(f"{path} {name} 0x{offset:X} {field_type}: unknown size")
                continue
            if element_count:
                width *= eval(element_count)
            alignment = 8 if is_pointer else PRIMITIVE_SIZES.get(field_type, 1)
            if field_type in FOUR_BYTE_ALIGNED_TYPES:
                alignment = 4
            if offset % alignment and not PACKED_RECEIVE_MESSAGE.match(name):
                problems.append(f"{path} {name} 0x{offset:X} {field_type}: misaligned")
            if previous_type and offset < previous_end:
                problems.append(f"{path} {name} 0x{offset:X} {field_type}: overlaps {previous_type}")
            if size is not None and offset + width > size:
                problems.append(f"{path} {name} 0x{offset:X} {field_type}: past Size 0x{size:X}")
            previous_end, previous_type = max(previous_end, offset + width), field_type
    return problems


if __name__ == "__main__":
    found = check(sys.argv[1] if len(sys.argv) > 1 else ObjectLayoutCatalog.OBJECT_LAYOUT_ROOT)
    print("\n".join(found) or "ok")
    sys.exit(1 if found else 0)
