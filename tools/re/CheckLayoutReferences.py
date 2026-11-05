import glob
import os
import re
import sys

import ObjectLayoutCatalog

DOCS_ROOTS = ("docs",)
MIRRORS_TABLE = "gw2-research/docs/engine/layout-mirrors.md"
COPY_ROOTS = ["src"]
STRUCT_OFFSET_IN_DOCS = re.compile(r"\b(?P<struct>[A-Z][A-Za-z0-9]+)\s*\+\s*(?P<offset>0x[0-9A-Fa-f]+)\b")
MIRROR_ROW = re.compile(r"^\|\s*`(?P<client>[\w.]+)`\s*\|\s*`(?P<original>[\w.]+)`\s*\|")
CLIENT_ENUM = re.compile(r"enum (?P<name>\w+)(?:\s*:\s*\w+)?\s*\{(?P<body>[^}]*)\}")
CLIENT_ENUM_MEMBER = re.compile(r"(\w+)\s*=\s*([^,\s]+)")
CLIENT_CONSTANT = re.compile(r"\b(?:const|static readonly) (?P<type>\w+) (?P<name>\w+) = (?P<value>[^;]+);")
COLOR32 = re.compile(r"new(?: Color32)?\(\s*(0x[0-9A-Fa-f]+|\d+)\s*,\s*(0x[0-9A-Fa-f]+|\d+)\s*,\s*(0x[0-9A-Fa-f]+|\d+)\s*,\s*(0x[0-9A-Fa-f]+|\d+)\s*\)")


def doc_offset_problems(types_by_name):
    problems = []
    for path in sorted(path for root in DOCS_ROOTS for path in glob.glob(root + "/**/*.md", recursive=True)):
        for line_number, line in enumerate(open(path, encoding="utf-8"), 1):
            for match in STRUCT_OFFSET_IN_DOCS.finditer(line):
                layout_type = types_by_name.get(match["struct"])
                if not layout_type or layout_type.kind != "struct":
                    continue
                offset = int(match["offset"], 0)
                names = layout_type.fields.get(offset)
                written = f"{match['struct']}+{match['offset']}"
                if names:
                    problems.append(f"{path}:{line_number}: {written} repeats an offset; write `{match['struct']}.{names[0]}`")
                else:
                    problems.append(f"{path}:{line_number}: {written} names no field of {match['struct']}; add the field to the layout and write its name")
    return problems


def client_values(path):
    text = open(path, encoding="utf-8").read()
    values = {}
    for enum in CLIENT_ENUM.finditer(text):
        values[enum["name"]] = {name: ObjectLayoutCatalog.evaluate(value) for name, value in CLIENT_ENUM_MEMBER.findall(enum["body"])}
    for constant in CLIENT_CONSTANT.finditer(text):
        color = COLOR32.search(constant["value"])
        if constant["type"] == "Color32" and color:
            red, green, blue, alpha = (int(channel, 0) for channel in color.groups())
            values[constant["name"]] = alpha << 24 | red << 16 | green << 8 | blue
        else:
            values[constant["name"]] = ObjectLayoutCatalog.evaluate(constant["value"])
    return values


def original_value(types_by_name, qualified_name):
    if qualified_name in types_by_name:
        return types_by_name[qualified_name].members
    owner, _, member = qualified_name.rpartition(".")
    layout_type = types_by_name.get(owner)
    if layout_type and member in layout_type.constants:
        return layout_type.constants[member][1]
    if layout_type and member in layout_type.members:
        return layout_type.members[member]
    return None


def mirror_problems(types_by_name):
    layout_project = ObjectLayoutCatalog.LAYOUT_PROJECT_ROOT.replace("/", os.sep)
    client_files = sorted(path for root in COPY_ROOTS for path in glob.glob(root + "/**/*.cs", recursive=True)
                          if not os.path.normpath(path).startswith(layout_project + os.sep))
    client_by_name = {}
    for path in client_files:
        for name, value in client_values(path).items():
            client_by_name.setdefault(name, (path, value))
    problems = []
    for line_number, line in enumerate(open(MIRRORS_TABLE, encoding="utf-8"), 1):
        row = MIRROR_ROW.match(line)
        if not row:
            continue
        where = f"{MIRRORS_TABLE}:{line_number}"
        client_name = row["client"].rpartition(".")[2]
        if client_name not in client_by_name:
            problems.append(f"{where}: client `{row['client']}` not found under {' or '.join(COPY_ROOTS)}")
            continue
        client_path, client_value = client_by_name[client_name]
        expected = original_value(types_by_name, row["original"])
        if expected is None:
            problems.append(f"{where}: original `{row['original']}` not found under {ObjectLayoutCatalog.OBJECT_LAYOUT_ROOT} or in {ObjectLayoutCatalog.VALUE_TABLES}")
        elif isinstance(client_value, dict):
            if not isinstance(expected, dict):
                problems.append(f"{where}: `{row['client']}` is an enum but `{row['original']}` is not")
                continue
            for member, value in client_value.items():
                if member not in expected:
                    problems.append(f"{client_path}: {client_name}.{member} has no counterpart in {row['original']}")
                elif expected[member] != value:
                    problems.append(f"{client_path}: {client_name}.{member} = {value}, {row['original']}.{member} = {expected[member]}")
        elif client_value != expected:
            problems.append(f"{client_path}: {client_name} = {format_value(client_value)}, {row['original']} = {format_value(expected)}")
    return problems


def format_value(value):
    return f"0x{value:08X}" if isinstance(value, int) and value > 0xFFFF else repr(value)


def check():
    types_by_name = ObjectLayoutCatalog.by_qualified_name(ObjectLayoutCatalog.load() + ObjectLayoutCatalog.load_value_tables())
    return doc_offset_problems(types_by_name) + mirror_problems(types_by_name)


if __name__ == "__main__":
    found = check()
    print("\n".join(found) or "ok")
    sys.exit(1 if found else 0)
