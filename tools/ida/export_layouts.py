import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

LAYOUTS_ROOT = os.path.join(gw2re_ida.REPOSITORY_ROOT, "gw2-research", "src", "Gw2.Exe.Objects")
PROJECT_FOLDERS = {"objects": "", "enums": "Enums"}
MANIFEST_PATH = os.path.join(gw2re_ida.TOOL_DIRECTORY, "layout_types.txt")
NAMESPACE = "Gw2.Exe.Objects"

INDENT = "    "
META_KEYS = ("const", "member", "field", "vtable", "slot")
VTABLE_OFFSET_SUFFIX = "VtableOffset"

PRIMITIVE_NAMES = {
    "unsigned __int8": "byte",
    "unsigned char": "byte",
    "char": "byte",
    "signed __int8": "sbyte",
    "signed char": "sbyte",
    "unsigned __int16": "ushort",
    "unsigned short": "ushort",
    "__int16": "short",
    "short": "short",
    "unsigned int": "uint",
    "int": "int",
    "unsigned __int64": "ulong",
    "__int64": "nint",
    "float": "float",
    "double": "double",
    "bool": "bool",
    "_BOOL": "byte",
    "void": "void",
}
ENUM_SHIFT = re.compile(r"(?i)^(\d+)(?:ul|ll|u|l)?\s*<<\s*(\d+)$")
ENUM_SUFFIX = re.compile(r"(?i)(?:ul|ll|u|l)$")
ENUM_HEX_SUFFIX = {"ulong": "UL", "long": "L"}


def enum_number(text):
    text = text.strip()
    shift = ENUM_SHIFT.match(text)
    if shift:
        return int(shift.group(1)) << int(shift.group(2))
    try:
        return int(ENUM_SUFFIX.sub("", text), 0)
    except ValueError:
        return None


def enum_value_text(value, underlying):
    number = enum_number(value)
    if number is None or number <= 0:
        return value
    return "0x%X%s" % (number, ENUM_HEX_SUFFIX.get(underlying or "", ""))


def manifest_names():
    with open(MANIFEST_PATH, encoding="utf-8") as manifest:
        return [line.strip() for line in manifest if line.strip()]


def read_metadata(text):
    metadata = {key: [] for key in META_KEYS}
    for line in (text or "").splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key in metadata:
            metadata[key].append(value)
        else:
            metadata[key] = value
    return metadata


def load_types():
    db = gw2re_ida.database()
    names = manifest_names()
    types = {}
    for name in names:
        type_info = db.types.get_by_name(name)
        if type_info is None:
            continue
        metadata = read_metadata(db.types.get_comment(type_info))
        types[name] = {
            "typeInfo": type_info,
            "enum": type_info.is_enum(),
            "meta": metadata,
            "order": int(metadata.get("order", "0")),
        }
    missing = [name for name in names if name not in types]
    if missing:
        raise RuntimeError("%d of %d manifest types are not in the database, first: %s"
                           % (len(missing), len(names), ", ".join(missing[:5])))
    return types


def raw_names(types):
    return {name: entry["meta"].get("raw", name) for name, entry in types.items()}


def reference(type_info, raws, override):
    if override:
        return override
    text = type_info.get_type_name() or type_info.dstr()
    if text in PRIMITIVE_NAMES:
        return PRIMITIVE_NAMES[text]
    if text:
        return raws.get(text, text)
    return "void"


def member_overrides(entry):
    overrides = {}
    for item in entry["meta"].get("field", []):
        member, _, value = item.partition("|")
        overrides[member] = value
    return overrides


def vtable_members(entry):
    members = []
    for item in entry["meta"].get("vtable", []):
        member, _, _ = item.partition("|")
        members.append(member)
    return members


def slot_names(entry):
    names = {}
    for item in entry["meta"].get("const", []):
        _, _, rest = item.partition("|")
        constant_name, _, constant_value = rest.partition("|")
        number = enum_number(constant_value)
        if constant_name.endswith(VTABLE_OFFSET_SUFFIX) and number is not None:
            names[number] = constant_name[:-len(VTABLE_OFFSET_SUFFIX)]
    for item in entry["meta"].get("slot", []):
        offset, _, slot_name = item.partition("|")
        number = enum_number(offset)
        if slot_name and number is not None:
            names[number] = slot_name
    return names


def field_declarations(entry, raws):
    db = gw2re_ida.database()
    overrides = member_overrides(entry)
    vtables = vtable_members(entry)
    declarations = []
    table_members = []
    unsafe = False
    for member in db.types.get_udt_members(entry["typeInfo"]):
        if member.name.startswith("pad_"):
            continue
        offset = member.offset
        if member.name in vtables:
            unsafe = True
            declarations.append("[FieldOffset(0x%03X)] %s* %sPtr;" % (offset, member.name, member.name))
            table_members.append((offset, member.name))
            continue
        declared = overrides.get(member.name)
        if declared is not None:
            if "*" in declared:
                unsafe = True
            declarations.append("[FieldOffset(0x%03X)] %s %s;" % (offset, declared, member.name))
            continue
        member_type = member.type
        count = None
        if member_type.is_array():
            element = db.types.get_array_element_type(member_type)
            if element.get_size() == 1:
                continue
            count = db.types.get_array_length(member_type)
            member_type = element
        pointer = member_type.is_ptr()
        if pointer:
            member_type = db.types.get_pointed_type(member_type)
        if pointer:
            unsafe = True
            declarations.append("[FieldOffset(0x%03X)] %s* %s;"
                                % (offset, reference(member_type, raws, None), member.name))
        elif count is not None:
            unsafe = True
            declarations.append("[FieldOffset(0x%03X)] fixed %s %s[%d];"
                                % (offset, reference(member_type, raws, None), member.name, count))
        else:
            declarations.append("[FieldOffset(0x%03X)] %s %s;"
                                % (offset, reference(member_type, raws, None), member.name))
    return declarations, unsafe, table_members


def inline_element(entry, raws):
    db = gw2re_ida.database()
    for member in db.types.get_udt_members(entry["typeInfo"]):
        member_type = member.type
        if member_type.is_array():
            member_type = db.types.get_array_element_type(member_type)
        pointer = member_type.is_ptr()
        if pointer:
            member_type = db.types.get_pointed_type(member_type)
        return reference(member_type, raws, None) + ("*" if pointer else ""), pointer
    return "byte", False


def render(name, types, raws, children, depth):
    entry = types[name]
    metadata = entry["meta"]
    indent = INDENT * depth
    attributes = metadata.get("attrs", "").split(",")
    options = {}
    for attribute in attributes:
        if "=" in attribute:
            key, value = attribute.split("=", 1)
            options[key] = value
    caption = metadata.get("raw", name)
    size = options.get("size")
    size_suffix = ", Size = %s" % size if size else ""
    if entry["enum"]:
        lines = []
        flags = "flags" in attributes
        if flags:
            lines.append(indent + "[Flags]")
        underlying = metadata.get("underlying") or "int"
        lines.append(indent + "internal enum %s : %s" % (caption, underlying))
        lines.append(indent + "{")
        for item in metadata.get("member", []):
            member, _, value = item.partition("|")
            if flags:
                value = enum_value_text(value, underlying)
            lines.append(indent + INDENT + "%s = %s," % (member, value))
        lines.append(indent + "}")
        return lines
    if "sequential" in attributes:
        return [indent + "[StructLayout(LayoutKind.Sequential%s)]" % size_suffix,
                indent + "internal struct %s" % caption,
                indent + "{" + "}"]
    if "inline" in options:
        element, pointer = inline_element(entry, raws)
        return [indent + "[InlineArray(%s)]" % options["inline"],
                indent + "internal %sstruct %s" % ("unsafe " if pointer else "", caption),
                indent + "{",
                indent + INDENT + "internal %s Element0;" % element,
                indent + "}"]
    declarations, unsafe, table_members = field_declarations(entry, raws)
    names_by_slot = sorted(slot_names(entry).items())
    lines = [indent + "[StructLayout(LayoutKind.Explicit%s)]" % size_suffix,
             indent + "internal %sstruct %s" % ("unsafe " if unsafe or "unsafe" in attributes else "", caption),
             indent + "{"]
    for item in metadata.get("const", []):
        constant_type, _, rest = item.partition("|")
        constant_name, _, constant_value = rest.partition("|")
        if table_members and constant_name.endswith(VTABLE_OFFSET_SUFFIX):
            continue
        lines.append(indent + INDENT + "internal const %s %s = %s;" % (constant_type, constant_name, constant_value))
    for declaration in declarations:
        lines.append(indent + INDENT + declaration)
    for index, (_, member_name) in enumerate(table_members):
        slots = names_by_slot if index == 0 else []
        lines.append("")
        lines.append(indent + INDENT + "[StructLayout(LayoutKind.Explicit)]")
        lines.append(indent + INDENT + "internal struct %s" % member_name)
        lines.append(indent + INDENT + "{")
        for slot_offset, slot_name in slots:
            lines.append(indent + INDENT + INDENT + "[FieldOffset(0x%02X)] nint %s;" % (slot_offset, slot_name))
        lines.append(indent + INDENT + "}")
    for child in sorted(children.get(name, []), key=lambda item: types[item]["order"]):
        lines.append("")
        lines.extend(render(child, types, raws, children, depth + 1))
    lines.append(indent + "}")
    return lines


def file_usings(names, types):
    usings = []
    for name in names:
        for using in (types[name]["meta"].get("usings") or "").split("|"):
            if using and using not in usings:
                usings.append(using)
    return usings


def write_file(layouts_root, stamp, relative_path, names, types, raws, children):
    lines = []
    usings = file_usings(names, types)
    for using in usings:
        lines.append("using %s;" % using)
    if usings:
        lines.append("")
    lines.append("namespace %s;" % NAMESPACE)
    lines.append("")
    if any(not types[name]["enum"] for name in names):
        lines.append(stamp)
        lines.append("")
    for name in sorted(names, key=lambda item: types[item]["order"]):
        if types[name]["meta"].get("parent", ""):
            continue
        lines.extend(render(name, types, raws, children, 0))
        lines.append("")
    folder, _, rest = relative_path.replace("\\", "/").partition("/")
    destination = os.path.join(layouts_root, PROJECT_FOLDERS.get(folder, folder), rest) if rest else os.path.join(layouts_root, folder)
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    with open(destination, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines).rstrip() + "\n")
    return destination


def export(layouts_root=None):
    layouts_root = layouts_root or LAYOUTS_ROOT
    build = gw2re_ida.image_build()
    stamp = "// Build %s." % build
    types = load_types()
    raws = raw_names(types)
    children = {}
    files = {}
    for name, entry in types.items():
        children.setdefault(entry["meta"].get("parent", ""), []).append(name)
        files.setdefault(entry["meta"]["path"], []).append(name)
    written = []
    for relative_path, names in sorted(files.items()):
        written.append(write_file(layouts_root, stamp, relative_path, names, types, raws, children))
    summary = gw2re_ida.artifact_summary(
        layouts_root, "layout-sources", files=len(written), types=len(types), build=build)
    print("exported %d files for %d types, build %s to %s"
          % (summary["files"], summary["types"], summary["build"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
