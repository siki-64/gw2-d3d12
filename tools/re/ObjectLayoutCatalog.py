import glob
import os
import re
from dataclasses import dataclass, field

LAYOUT_PROJECT_ROOT = "gw2-research/src/Gw2.Exe.Objects"
OBJECT_LAYOUT_ROOT = LAYOUT_PROJECT_ROOT
ENUM_FOLDER = "Enums"
ENUM_ROOT = LAYOUT_PROJECT_ROOT + "/" + ENUM_FOLDER
BUILD_OUTPUT_FOLDERS = ("bin", "obj")
ENUM_ORIGINS = ("Server", "Client")
VALUE_TABLES = "gw2-research/docs/engine/value-tables.md"
VALUE_TABLE_HEADER = re.compile(r"^### `(\w+)`")
VALUE_TABLE_ROW = re.compile(r"^\| `(\w+)` \| (0x[0-9A-Fa-f]+|-?\d+) \|")
TYPE_HEADER = re.compile(r"(?m)^(?P<indent> *)(?:\[[^\]]*\]\s*)*internal (?:static |readonly |unsafe |partial )*(?P<kind>struct|enum|class) (?P<name>\w+)")
FIELD = re.compile(r"\[FieldOffset\((0x[0-9A-Fa-f]+|\d+)\)(?:, Base)?\] internal (?:fixed )?[\w*.]+ (\w+)")
ENUM_MEMBER = re.compile(r"(?m)^\s*(\w+)\s*=\s*([^,\n}]+?)\s*,?\s*$")
CONSTANT = re.compile(r"internal const (\w+) (\w+) = ([^;]+);")
NUMERIC_SUFFIX = re.compile(r"(?<=[0-9A-Fa-f.])(?:UL|LU|ul|U|u|L|l|f|F|m|M)\b")


TYPE_LIMITS = {f"{name}.{limit}": value for name, bounds in {
    "byte": (0, 0xFF), "sbyte": (-0x80, 0x7F), "ushort": (0, 0xFFFF), "short": (-0x8000, 0x7FFF),
    "uint": (0, 0xFFFFFFFF), "int": (-0x80000000, 0x7FFFFFFF),
    "ulong": (0, 0xFFFFFFFFFFFFFFFF), "long": (-0x8000000000000000, 0x7FFFFFFFFFFFFFFF),
}.items() for limit, value in zip(("MinValue", "MaxValue"), bounds)}


def evaluate(expression, known=None):
    text = NUMERIC_SUFFIX.sub("", expression.strip())
    text = re.sub(r"\((?:u?int|u?long|u?short|s?byte|float|double)\)", "", text)
    for limit, value in TYPE_LIMITS.items():
        text = text.replace(limit, str(value))
    try:
        return eval(text, {"__builtins__": {}}, dict(known or {}))
    except Exception:
        return None


@dataclass
class LayoutType:
    name: str
    kind: str
    path: str
    parent: str
    fields: dict = field(default_factory=dict)
    members: dict = field(default_factory=dict)
    constants: dict = field(default_factory=dict)

    @property
    def qualified_name(self):
        return f"{self.parent}.{self.name}" if self.parent else self.name


def type_body(text, start):
    depth = 0
    opened = text.index("{", start)
    for index in range(opened, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return opened, index
    return opened, len(text)


def own_text(text, start, end, nested_spans):
    pieces, cursor = [], start
    for nested_start, nested_end in sorted(nested_spans):
        if start <= nested_start and nested_end <= end:
            pieces.append(text[cursor:nested_start])
            cursor = nested_end + 1
    pieces.append(text[cursor:end])
    return "".join(pieces)


def parse_file(path):
    text = open(path, encoding="utf-8").read()
    headers = []
    for header in TYPE_HEADER.finditer(text):
        if text.find("{", header.end()) < 0:
            continue
        body_start, body_end = type_body(text, header.end())
        headers.append((header, body_start, body_end))
    types = []
    for header, body_start, body_end in headers:
        enclosing = [h for h, s, e in headers if s < header.start() and header.end() < e]
        parent = ".".join(h["name"] for h in sorted(enclosing, key=lambda h: h.start()))
        nested_spans = [(h.start(), e) for h, s, e in headers if body_start < h.start() and e <= body_end]
        body = own_text(text, body_start + 1, body_end, nested_spans)
        layout_type = LayoutType(header["name"], header["kind"], path, parent)
        if header["kind"] == "enum":
            known = {}
            for member, value in ENUM_MEMBER.findall(body):
                known[member] = evaluate(value, known)
                layout_type.members[member] = known[member]
        else:
            for offset, name in FIELD.findall(body):
                layout_type.fields.setdefault(int(offset, 0), []).append(name)
            known = {}
            for constant_type, name, value in CONSTANT.findall(body):
                known[name] = evaluate(value, known)
                layout_type.constants[name] = (constant_type, known[name])
        types.append(layout_type)
    return types


def object_paths(root=OBJECT_LAYOUT_ROOT):
    paths = []
    for path in glob.glob(root + "/**/*.cs", recursive=True):
        first_folder = os.path.relpath(path, root).replace("\\", "/").split("/")[0]
        if first_folder not in BUILD_OUTPUT_FOLDERS + (ENUM_FOLDER,):
            paths.append(path)
    return paths


def enum_paths():
    return glob.glob(ENUM_ROOT + "/**/*.cs", recursive=True)


def layout_paths(root=OBJECT_LAYOUT_ROOT):
    return object_paths(root) + enum_paths()


def load(root=OBJECT_LAYOUT_ROOT):
    types = []
    for path in sorted(layout_paths(root)):
        types += parse_file(path.replace("\\", "/"))
    return types


def load_value_tables(path=VALUE_TABLES):
    types, current = [], None
    for line in open(path, encoding="utf-8"):
        header = VALUE_TABLE_HEADER.match(line)
        if header:
            current = LayoutType(header[1], "enum", path, "")
            types.append(current)
            continue
        row = VALUE_TABLE_ROW.match(line)
        if row and current:
            current.members[row[1]] = int(row[2], 0)
    return types


def by_qualified_name(types):
    return {layout_type.qualified_name: layout_type for layout_type in types}
