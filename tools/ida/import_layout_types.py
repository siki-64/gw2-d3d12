import os
import re
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ida_typeinf

import gw2re_ida

LAYOUTS_ROOT = os.path.join(gw2re_ida.REPOSITORY_ROOT, "gw2-research", "src", "Gw2.Exe.Objects")
MANIFEST_PATH = os.path.join(gw2re_ida.TOOL_DIRECTORY, "layout_types.txt")
CORPUS_PATH = Path(os.path.join(gw2re_ida.TOOL_DIRECTORY, "layout_types.til"))

STAMP = re.compile(r"^// Build ([^.]+)\.(.*)$")
ANCHOR_FUNCTION = re.compile(r"\bsub_([0-9A-Fa-f]{6,16})\b")
DECLARATION = re.compile(r"internal (?:static |readonly |unsafe |partial )*(struct|enum|class) (\w+)(?:\s*:\s*(\w+))?")
RECORD_STRUCT = re.compile(r"internal (?:readonly )?record struct (\w+)\s*\(([^)]*)\)")
FIELD = re.compile(r"\[FieldOffset\((0x[0-9A-Fa-f]+|\d+)\)(,\s*Base)?\]\s*internal\s+(?:fixed\s+)?([\w.<>]+)(\s*\*)?\s+(\w+)\s*(?:\[(\d+)\])?\s*;")
ELEMENT = re.compile(r"^(?:private |internal |public |protected )?([\w.<>]+)(\s*\*)?\s+(\w+)\s*;$")
CONSTANT = re.compile(r"internal const ([\w.]+) (\w+) = ([^;]+);")
SIZE_ATTRIBUTE = re.compile(r"Size = (0x[0-9A-Fa-f]+|\d+)")
SLOT_COUNT_ATTRIBUTE = re.compile(r"SlotCount = (\d+)")
INLINE_ARRAY = re.compile(r"\[InlineArray\(([^)]+)\)\]")
ENUM_MEMBER = re.compile(r"^(\w+)\s*=\s*([^,}]+?)\s*,?\s*$")
ENUM_SUFFIX = re.compile(r"(?i)(?:ul|ll|u|l)$")
VALUE_SHIFT = re.compile(r"^(0x[0-9A-Fa-f]+|\d+)\s*<<\s*(\d+)$")
PAD = "pad_"

SCALARS = {
    "byte": (1, False), "sbyte": (1, True), "bool": (1, False),
    "short": (2, True), "ushort": (2, False),
    "int": (4, True), "uint": (4, False), "float": (4, None),
    "long": (8, True), "ulong": (8, False), "nint": (8, True), "nuint": (8, False),
    "double": (8, None),
}
ENUM_BASE_SIZE = {"byte": 1, "sbyte": 1, "ushort": 2, "short": 2, "uint": 4, "int": 4,
                  "ulong": 8, "long": 8, "nint": 8, "nuint": 8}
ENUM_BASE_SIGNED = {"byte": False, "sbyte": True, "ushort": False, "short": True, "uint": False,
                    "int": True, "ulong": False, "long": True, "nuint": False, "nint": True}
EXTERNALS = {
    "Guid": (16, 4, "uint", 4),
    "Vector3": (12, 4, "float", 3),
    "Vector4": (16, 4, "float", 4),
    "Quaternion": (16, 4, "float", 4),
}
PRIMITIVE_ALIGNMENT = 8
BUILD_OUTPUT_FOLDERS = ("bin", "obj")
GAME_ROOT = os.path.join(gw2re_ida.REPOSITORY_ROOT, "src")
GAME_DECLARATION = re.compile(r"public (?:readonly )?(enum|record struct|struct|class) (\w+)(?:\s*:\s*(\w+))?")
GAME_CACHE = {}
BUILDS = set()


def game_types():
    if GAME_CACHE:
        return GAME_CACHE
    for directory, _, names in os.walk(GAME_ROOT):
        for name in sorted(names):
            if not name.endswith(".cs"):
                continue
            for line in open(os.path.join(directory, name), encoding="utf-8"):
                match = GAME_DECLARATION.search(line)
                if match:
                    GAME_CACHE[match.group(2)] = {"kind": match.group(1), "base": match.group(3)}
    return GAME_CACHE


def game_type(text):
    found = game_types().get(text)
    return found if found is not None else game_types().get(text.split(".")[-1])


def referenced_names(catalog):
    names = set()
    for spec in catalog.values():
        if spec["kind"] == "enum":
            continue
        for row in spec["members"]:
            names.add(row["type"])
        if spec["element"]:
            names.add(spec["element"]["type"])
    return {name for name in names if name and not name.endswith("*") and name not in catalog}


def number(text):
    text = text.strip()
    shift = VALUE_SHIFT.match(text)
    if shift:
        return int(shift.group(1), 0) << int(shift.group(2))
    try:
        return int(ENUM_SUFFIX.sub("", text), 0)
    except ValueError:
        return None


def offset_number(text):
    return int(text, 16) if text.lower().startswith("0x") else int(text)


def attribute_prefixes(line):
    attributes = []
    text = line.strip()
    while text.startswith("["):
        depth = 0
        for index, character in enumerate(text):
            if character == "[":
                depth += 1
            elif character == "]":
                depth -= 1
                if depth == 0:
                    attributes.append(text[:index + 1])
                    text = text[index + 1:].strip()
                    break
        else:
            break
    return attributes, text


def parse_layout_file(path):
    lines = open(path, encoding="utf-8").read().splitlines()
    types = []
    constants = {}
    stamps = []
    stack = []
    depth = 0
    pending = []
    stamp = (None, [])

    def open_braces(text):
        nonlocal depth
        for character in text:
            if character == "{":
                depth += 1
                if stack:
                    stack[-1]["entered"] = True
            elif character == "}":
                depth -= 1
                while stack and stack[-1]["entered"] and depth <= stack[-1]["depth"]:
                    stack.pop()

    def new_spec(name, kind, base):
        return {
            "name": name,
            "kind": kind,
            "base": base,
            "build": stamp[0],
            "anchors": list(stamp[1]),
            "attributes": list(pending),
            "members": [],
            "element": None,
            "constants": [],
            "size": None,
            "parent": stack[-1]["name"] if stack else None,
            "parent_ida": stack[-1]["ida_name"] if stack else None,
            "ancestors": ([stack[-1]["ida_name"]] + stack[-1]["ancestors"]) if stack else [],
            "ida_name": "%s_%s" % (stack[-1]["ida_name"], name) if stack else name,
            "depth": depth,
            "entered": False,
            "inline_argument": None,
            "inline_count": None,
            "sequential": False,
            "flags": False,
            "positional": False,
            "virtual": False,
            "slot_count": None,
        }

    def finish_spec(spec, statement):
        for attribute in pending:
            size = SIZE_ATTRIBUTE.search(attribute)
            if size:
                spec["size"] = offset_number(size.group(1))
        inline = next((INLINE_ARRAY.search(item) for item in pending if INLINE_ARRAY.search(item)), None)
        spec["inline_argument"] = inline.group(1).strip() if inline else None
        spec["inline_count"] = number(spec["inline_argument"]) if spec["inline_argument"] else None
        spec["sequential"] = any("Sequential" in attribute for attribute in pending)
        spec["flags"] = any("Flags" in attribute for attribute in pending)
        spec["virtual"] = any(attribute.startswith("[VirtualLayout(") for attribute in pending)
        slot_count = next((SLOT_COUNT_ATTRIBUTE.search(item) for item in pending if SLOT_COUNT_ATTRIBUTE.search(item)), None)
        spec["slot_count"] = int(slot_count.group(1)) if slot_count and spec["virtual"] else None
        types.append(spec)
        stack.append(spec)
        open_braces(statement)

    for line in lines:
        attributes, statement = attribute_prefixes(line)
        comment = STAMP.match(statement)
        if comment:
            stamp = (comment.group(1),
                     ["0x%s" % name for name in ANCHOR_FUNCTION.findall(comment.group(2))])
            stamps.append(stamp[0])
            continue
        if attributes:
            pending = attributes
        if not statement or statement.startswith("//"):
            continue
        opener = stack[-1] if stack else None
        if opener is not None and opener["kind"] == "enum" and opener["entered"]:
            member = ENUM_MEMBER.match(statement)
            if member:
                opener["members"].append((member.group(1), member.group(2)))
                continue
        record = RECORD_STRUCT.search(statement)
        if record and statement.startswith("internal"):
            spec = new_spec(record.group(1), "struct", None)
            spec["positional"] = True
            for parameter in record.group(2).split(","):
                pieces = parameter.strip().split()
                if len(pieces) == 2:
                    spec["members"].append({"offset": None, "type": pieces[0], "array": None, "name": pieces[1]})
            finish_spec(spec, statement)
            pending = []
            continue
        declaration = DECLARATION.search(statement)
        if declaration and statement.startswith("internal"):
            spec = new_spec(declaration.group(2), declaration.group(1), declaration.group(3))
            finish_spec(spec, statement)
            pending = []
            continue
        field = FIELD.search(line)
        if field and stack and stack[-1]["kind"] == "struct" and stack[-1]["entered"]:
            stack[-1]["members"].append({
                "offset": offset_number(field.group(1)),
                "type": field.group(3) + ("*" if field.group(4) else ""),
                "array": int(field.group(6)) if field.group(6) else None,
                "name": field.group(5),
                "base": bool(field.group(2)),
            })
            continue
        element = ELEMENT.match(statement)
        if element and stack and stack[-1]["kind"] == "struct" and stack[-1]["entered"]:
            stack[-1]["element"] = {
                "type": element.group(1) + ("*" if element.group(2) else ""),
                "name": element.group(3),
            }
            continue
        constant = CONSTANT.search(statement)
        if constant and stack and stack[-1]["kind"] == "struct" and stack[-1]["entered"]:
            stack[-1]["constants"].append((constant.group(2), constant.group(3)))
            constants[constant.group(2)] = constant.group(3)
            continue
        open_braces(statement)
    return types, constants, stamps


def layout_files(root):
    for directory, folders, names in os.walk(root):
        folders[:] = sorted(folder for folder in folders if folder not in BUILD_OUTPUT_FOLDERS)
        for name in sorted(names):
            if name.endswith(".cs"):
                yield os.path.join(directory, name)


def manifest_names():
    with open(MANIFEST_PATH, encoding="utf-8") as manifest:
        return [line.strip() for line in manifest if line.strip()]


def collect(root=None, only=None):
    root = root or LAYOUTS_ROOT
    catalog = {}
    for path in layout_files(root):
        relative = os.path.relpath(path, root).replace("\\", "/")
        if only and only not in relative:
            continue
        types, constants, stamps = parse_layout_file(path)
        BUILDS.update(stamps)
        for spec in types:
            if spec["kind"] not in ("struct", "enum"):
                continue
            if spec["inline_argument"] and spec["inline_count"] is None:
                spec["inline_count"] = number(constants.get(spec["inline_argument"], ""))
            spec["file"] = relative
            catalog[spec["ida_name"]] = spec
    for spec in catalog.values():
        if not spec.get("positional"):
            continue
        for row in spec["members"]:
            row["offset"] = 0
        cursor = 0
        for row in spec["members"]:
            size = member_size(catalog, spec, row["type"])
            alignment = member_alignment(catalog, spec, row["type"]) or 1
            if size is None:
                break
            cursor = (cursor + alignment - 1) // alignment * alignment
            row["offset"] = cursor
            cursor += size
    return catalog


def type_index(catalog):
    return dict(catalog)


def resolve_spec(index, scope, text):
    key = text.replace(".", "_")
    found = index.get(key)
    if found is not None:
        return found
    if scope:
        for prefix in [scope["ida_name"]] + scope["ancestors"]:
            candidate = index.get(prefix + "_" + key)
            if candidate is not None:
                return candidate
    return None


def member_size(index, scope, text, seen=None):
    if text.endswith("*"):
        return 8
    if text in SCALARS:
        return SCALARS[text][0]
    if text in EXTERNALS:
        return EXTERNALS[text][0]
    if text.startswith("System.Numerics."):
        return 16
    game = game_type(text)
    if game is not None and game["kind"] == "enum":
        return ENUM_BASE_SIZE.get(game["base"] or "int", 4)
    spec = resolve_spec(index, scope, text)
    if spec is None:
        return None
    if spec["kind"] == "enum":
        return ENUM_BASE_SIZE.get(spec["base"] or "int", 4)
    return struct_size(index, spec, seen)


def member_alignment(index, scope, text, seen=None):
    if text.endswith("*"):
        return PRIMITIVE_ALIGNMENT
    if text in SCALARS:
        return SCALARS[text][0]
    if text in EXTERNALS:
        return EXTERNALS[text][1]
    if text.startswith("System.Numerics."):
        return 4
    game = game_type(text)
    if game is not None and game["kind"] == "enum":
        return ENUM_BASE_SIZE.get(game["base"] or "int", 4)
    spec = resolve_spec(index, scope, text)
    if spec is None:
        return None
    if spec["kind"] == "enum":
        return ENUM_BASE_SIZE.get(spec["base"] or "int", 4)
    return struct_alignment(index, spec, seen)


def struct_alignment(index, spec, seen=None):
    seen = seen or set()
    if spec["ida_name"] in seen or spec["sequential"]:
        return 1 if spec["sequential"] else None
    seen = seen | {spec["ida_name"]}
    if spec["inline_count"]:
        if spec["element"] is None:
            return None
        return member_alignment(index, spec, spec["element"]["type"], seen)
    alignment = 1
    for row in spec["members"]:
        row_alignment = member_alignment(index, spec, row["type"], seen)
        if row_alignment is None:
            return None
        alignment = max(alignment, min(row_alignment, PRIMITIVE_ALIGNMENT))
    return alignment


def struct_size(index, spec, seen=None):
    seen = seen or set()
    if spec["ida_name"] in seen:
        return None
    seen = seen | {spec["ida_name"]}
    if spec["size"] is not None:
        return spec["size"]
    if spec["inline_count"]:
        element = spec["element"]["type"] if spec["element"] else None
        size = element and member_size(index, spec, element, seen)
        return size and size * spec["inline_count"]
    if not spec["members"]:
        return None
    end = 0
    for row in sorted(spec["members"], key=lambda item: item["offset"]):
        size = member_size(index, spec, row["type"], seen)
        if size is None:
            return None
        end = row["offset"] + size * (row["array"] or 1)
    return end or None


def rows_for(index, spec):
    if spec["sequential"]:
        return [{"offset": 0, "name": PAD + "000", "type": "byte", "array": spec["size"], "pad": True}]
    if spec["inline_count"]:
        element = spec["element"]
        if element is None:
            return None
        width = member_size(index, spec, element["type"])
        if width is None:
            return None
        total = width * spec["inline_count"]
        rows = [{"offset": 0, "name": element["name"], "type": element["type"], "array": None}]
        if total > width:
            rows.append({"offset": width, "name": "%s%03X" % (PAD, width), "type": "byte",
                         "array": total - width, "pad": True})
        return rows
    rows = []
    cursor = 0
    for row in sorted(spec["members"], key=lambda item: item["offset"]):
        size = member_size(index, spec, row["type"])
        if size is None:
            return None
        width = size * (row["array"] or 1)
        if row["offset"] < cursor:
            return None
        if row["offset"] > cursor:
            rows.append({"offset": cursor, "name": "%s%03X" % (PAD, cursor), "type": "byte",
                         "array": row["offset"] - cursor, "pad": True})
        rows.append(dict(row))
        cursor = row["offset"] + width
    total = spec["size"] if spec["size"] is not None else cursor
    if total < cursor:
        return None
    if total > cursor:
        rows.append({"offset": cursor, "name": "%s%03X" % (PAD, cursor), "type": "byte",
                     "array": total - cursor, "pad": True})
    return rows


def row_problem(index, spec):
    if spec["sequential"]:
        return None
    if spec["inline_count"] and spec["element"] is None:
        return "inline element missing"
    cursor = 0
    for row in sorted(spec["members"], key=lambda item: item["offset"]):
        size = member_size(index, spec, row["type"])
        if size is None:
            return "unknown type %s" % row["type"]
        if row["offset"] < cursor:
            return "overlap at 0x%X after 0x%X" % (row["offset"], cursor)
        cursor = row["offset"] + size * (row["array"] or 1)
    if spec["size"] is not None and spec["size"] < cursor:
        return "members end at 0x%X beyond Size 0x%X" % (cursor, spec["size"])
    return None


def misaligned_rows(index, spec):
    if spec["sequential"] or spec["inline_count"]:
        return []
    bad = []
    for row in spec["members"]:
        alignment = member_alignment(index, spec, row["type"])
        if alignment and alignment > 1 and row["offset"] % alignment:
            bad.append(row)
    return bad


def member_tinfo(db, index, built, scope, text):
    if text.endswith("*"):
        spec = resolve_spec(index, scope, text[:-1].strip())
        target = built.get(spec["ida_name"]) if spec else None
        return db.types.create_pointer(target or db.types.create_void())
    if text in SCALARS:
        size, signed = SCALARS[text]
        if text == "float":
            return db.types.create_float()
        if text == "double":
            return db.types.create_primitive(8, False)
        return db.types.create_primitive(size, signed)
    if text in EXTERNALS:
        return built.get(text)
    if text.startswith("System.Numerics."):
        return built.get(text.split(".")[-1])
    game = game_type(text)
    if game is not None and game["kind"] == "enum":
        return built.get(text.split(".")[-1])
    spec = resolve_spec(index, scope, text)
    if spec is None:
        return None
    return built.get(spec["ida_name"])


def drop_local(name):
    til = ida_typeinf.get_idati()
    ordinal = ida_typeinf.get_type_ordinal(til, name)
    if ordinal:
        ida_typeinf.del_numbered_type(til, ordinal)


def drop_library_type(library, name):
    ordinal = ida_typeinf.get_type_ordinal(library, name)
    if ordinal:
        ida_typeinf.del_numbered_type(library, ordinal)


def build_enum(db, name, base, members, flags, signed=True):
    data = ida_typeinf.enum_type_data_t()
    for member_name, value in members:
        number_value = number(value)
        if number_value is not None:
            data.add_constant(member_name, number_value)
    type_info = ida_typeinf.tinfo_t()
    if not type_info.create_enum(data):
        return None
    type_info.set_enum_width(base)
    type_info.set_enum_sign(signed)
    if flags:
        type_info.set_enum_is_bitmask(True)
    if type_info.set_named_type(ida_typeinf.get_idati(), name, ida_typeinf.NTF_REPLACE) < 0:
        return None
    return db.types.get_by_name(name)


def build_type(db, index, built, spec):
    if spec["kind"] == "enum":
        stored = build_enum(db, spec["ida_name"], ENUM_BASE_SIZE.get(spec["base"] or "int", 4),
                            spec["members"], spec["flags"],
                            ENUM_BASE_SIGNED.get(spec["base"] or "int", True))
        if stored is None:
            return "not-saved"
        built[spec["ida_name"]] = stored
        return "enum"
    if misaligned_rows(index, spec):
        return "packed"
    rows = rows_for(index, spec)
    if rows is None:
        return "unsized:" + (row_problem(index, spec) or "unknown")
    builder = db.types.create_struct(spec["ida_name"])
    for row in rows:
        if row.get("pad"):
            builder.add_member(row["name"], db.types.create_array(db.types.create_primitive(1), row["array"]),
                               row["offset"])
            continue
        member_type = member_tinfo(db, index, built, spec, row["type"])
        if member_type is None:
            return "unresolved:" + row["type"]
        if row["array"]:
            member_type = db.types.create_array(member_type, row["array"])
        builder.add_member(row["name"], member_type, row["offset"])
    stored = builder.build_and_save()
    if stored is None:
        return "not-saved"
    offsets = {member.name: member.offset for member in db.types.get_udt_members(stored)}
    drift = [row["name"] for row in rows if offsets.get(row["name"]) != row["offset"]]
    if drift:
        drop_local(spec["ida_name"])
        return "drift:" + ",".join(drift[:3])
    built[spec["ida_name"]] = stored
    return "struct"


def build(db, index, catalog):
    ida_typeinf.del_til(str(CORPUS_PATH))
    externals = sorted(EXTERNALS)
    game_enums = {}
    for name in sorted(referenced_names(catalog)):
        game = game_type(name)
        if game is not None and game["kind"] == "enum":
            game_enums[name.split(".")[-1]] = game
    for key in list(catalog) + externals + list(game_enums):
        drop_local(key)
    built = {}
    for short, game in sorted(game_enums.items()):
        stored = build_enum(db, short, ENUM_BASE_SIZE.get(game["base"] or "int", 4), [], False)
        if stored is not None:
            built[short] = stored
    for name in externals:
        size, alignment, element, count = EXTERNALS[name]
        builder = db.types.create_struct(name)
        element_type = db.types.create_float() if element == "float" else db.types.create_primitive(4, False)
        builder.add_member(PAD + "000", db.types.create_array(element_type, count), 0)
        built[name] = builder.build_and_save()
    outcomes = {}
    pending = dict(catalog)
    progress = True
    while pending and progress:
        progress = False
        for key in list(pending):
            spec = pending[key]
            try:
                outcome = build_type(db, index, built, spec)
            except Exception as error:
                drop_local(key)
                outcome = "error:%s (%s)" % (type(error).__name__, str(error)[:70])
            base = outcome.split(":", 1)[0]
            if base in ("struct", "enum"):
                del pending[key]
                outcomes.pop(key, None)
                progress = True
            else:
                outcomes[key] = outcome
                if "unresolved" not in outcome and "drift" not in outcome:
                    del pending[key]
    for key in pending:
        outcomes[key] = outcomes.get(key, "unreached")
    til = export_corpus(db, built)
    return til, built, outcomes


def export_corpus(db, built):
    if CORPUS_PATH.exists():
        CORPUS_PATH.unlink()
    til = db.types.create_library(CORPUS_PATH, "object layout types for the reference build")
    for name in sorted(built):
        if db.types.get_by_name(name) is None:
            continue
        drop_library_type(til, name)
        try:
            db.types.export_type(til, name)
        except Exception:
            pass
    db.types.save_library(til, CORPUS_PATH)
    ida_typeinf.del_til(str(CORPUS_PATH))
    return CORPUS_PATH


def run(only=None):
    db = gw2re_ida.database()
    catalog = collect(only=only)
    index = type_index(catalog)
    til, built, outcomes = build(db, index, catalog)
    print("catalog %d built %d" % (len(catalog), len(built) - 1))
    counts = {}
    for outcome in outcomes.values():
        key = outcome.split(":", 1)[0]
        counts[key] = counts.get(key, 0) + 1
    print("outcomes:", dict(sorted(counts.items(), key=lambda item: -item[1])))
    return catalog, built, outcomes


if __name__ == "__main__":
    run(*gw2re_ida.script_arguments(1)[:1])
