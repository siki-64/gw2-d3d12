import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

MAX_CONTENT_TYPE = 0x1E3
CONTEXT_HOLDER_OFFSET = 224
CONTENT_TYPE_FIELD_OFFSET = 16
DEF_ITERATOR_SLOT = 72
DEF_COUNT_SLOT = 64
LOOKUP_TAIL_PATTERNS = (
    b"\x48\xff\x60\x70",
    b"\xff\x50\x70",
    b"\xff\x90\x70\x00\x00\x00",
    b"\x48\xff\x60\x58",
    b"\xff\x50\x58",
    b"\xff\x90\x58\x00\x00\x00",
)
ITERATOR_TAIL_PATTERNS = (
    b"\x48\xff\x60\x48",
    b"\xff\x50\x48",
    b"\xff\x90\x48\x00\x00\x00",
    b"\x48\xff\x60\x40",
    b"\xff\x50\x40",
    b"\xff\x90\x40\x00\x00\x00",
)
TYPE_FIELD_PATTERNS = (
    b"\x8b[\x40-\x4f]\x10",
    b"\x8b[\x50-\x5f]\x10",
    b"\x8b[\x60-\x6f]\x10",
    b"\x8b[\x70-\x7f]\x10",
    b"\x44\x8b[\x40-\x4f]\x10",
)
COMPARE_PATTERNS = ((b"\x81[\x70-\x7f]\x10", 4), (b"\x83[\x70-\x7f]\x10", 1))
CMP_IMMEDIATE_PATTERNS = (
    b"\x83\xf8", b"\x83\xf9", b"\x83\xfa", b"\x83\xfb", b"\x83\xfe", b"\x83\xff",
    b"\x81\xf8", b"\x81\xf9", b"\x81\xfa", b"\x81\xfb", b"\x81\xfe", b"\x81\xff", b"\x3d",
)
ACCESSOR_TYPE_IDS = (
    0x01, 0x02, 0x03, 0x07, 0x08, 0x09, 0x0A, 0x0C, 0x0E, 0x11, 0xB7, 0x13, 0x14, 0x17,
    0x18, 0x19, 0x1B, 0x1C, 0x1D, 0x22, 0x23, 0x25, 0x26, 0x29, 0x2B, 0x2C, 0x2D, 0x10C,
    0x10F, 0x125, 0x30, 0x12E, 0x33, 0x34, 0x35, 0x141, 0x37, 0x146, 0x154, 0x147, 0x150,
    0x39, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x183, 0x182, 0x44, 0x48, 0x191, 0x49, 0x19A,
    0x4D, 0x1A5, 0x4F, 0x53, 0x54, 0x57, 0x58,
)
ERROR_REPORT_STRING = "ExeError.cpp"
INLINE_LOOKUP = re.compile(
    r"\*\*\(_\w+ \*\*\)\(\w+ \+ %d\) \+ (\d+)LL\)\)\(\s*[^,;]+,\s*(\d+),"
    % CONTEXT_HOLDER_OFFSET
)
DEF_ITERATION = re.compile(
    r"\*\*\(_\w+ \*\*\)\(\w+ \+ %d\)\)? \+ (\d+)LL\)\)\(\s*[^,;]+,\s*(\d+),"
    % CONTEXT_HOLDER_OFFSET
)
DEF_ITERATION_SLOT = re.compile(
    r"\((\w+) \+ %d(?:LL)?\)\)\(\s*[^,;]+,\s*(\d+)," % DEF_ITERATOR_SLOT
)
HOLDER_LOAD = re.compile(r"\+ %d\)" % CONTEXT_HOLDER_OFFSET)
DIRECT_COMPARE = re.compile(
    r"\*\(_DWORD \*\)\(\w+ \+ %d\) (==|!=) (\d+)(?![\w.])" % CONTENT_TYPE_FIELD_OFFSET
)
LOADED_COMPARE = re.compile(
    r"([A-Za-z_]\w*) = \*\(_DWORD \*\)\(\w+ \+ %d\);" % CONTENT_TYPE_FIELD_OFFSET
)
EXPRESSION_ASSERT = re.compile(r'sub_\w+\(\s*"([^"]+)"')
FORMATTED_ASSERT = re.compile(r'sub_\w+\(\s*(?:\(\w+\))?"[^"]*",\s*(\d+),\s*"([A-Za-z_]\w*)')
SOURCE_FILE = re.compile(r"([A-Za-z0-9_]+\.cpp)\"")


def error_report_functions():
    db = gw2re_ida.database()
    functions = set()
    for item in db.strings.get_all():
        if ERROR_REPORT_STRING not in item.contents.decode("utf-8", "replace"):
            continue
        references = list(db.xrefs.code_refs_to_ea(item.address))
        references.extend(db.xrefs.data_refs_to_ea(item.address))
        for reference in references:
            function = gw2re_ida.containing_function(reference)
            if function is not None:
                functions.add(function.start_ea)
    return sorted(functions)


def assert_sites(helpers):
    db = gw2re_ida.database()
    sites = set()
    for helper in helpers:
        sites.update(db.xrefs.code_refs_to_ea(helper))
    return sites


def function_body(start):
    function = gw2re_ida.containing_function(start)
    if function is None:
        return None
    return gw2re_ida.bytes_at(function.start_ea, function.end_ea - function.start_ea)


def candidate_functions(helpers, markers):
    found = set()
    for site in assert_sites(helpers):
        function = gw2re_ida.containing_function(site)
        if function is None:
            continue
        body = function_body(function.start_ea)
        if body is None or not any(marker in body for marker in markers):
            continue
        for match in re.finditer(rb"[\xb8-\xbf](....)", body, re.S):
            value = struct.unpack("<i", match.group(1))[0]
            if 0 < value <= MAX_CONTENT_TYPE and value not in ACCESSOR_TYPE_IDS:
                found.add(function.start_ea)
                break
    return sorted(found)


def compare_sites():
    sites = []
    for pattern, size in COMPARE_PATTERNS:
        for address in pattern_sites(pattern):
            prefix = gw2re_ida.byte_at(address - 1)
            offset = 4 if prefix is not None and 0x40 <= prefix <= 0x4F else 3
            operand = gw2re_ida.bytes_at(address + offset, size)
            if operand:
                value = operand[0] if size == 1 else struct.unpack("<i", operand)[0]
                if 0 < value <= MAX_CONTENT_TYPE:
                    sites.append((address, value))
    return sites


def pattern_sites(pattern):
    return gw2re_ida.find_regex(pattern)


def field_load_sites():
    sites = []
    for pattern in TYPE_FIELD_PATTERNS:
        sites.extend(pattern_sites(pattern))
    return sites


def compares_against_immediate(start):
    body = function_body(start)
    if body is None:
        return False
    return any(pattern in body for pattern in CMP_IMMEDIATE_PATTERNS)


def compared_functions():
    found = {}
    for address, value in compare_sites():
        function = gw2re_ida.containing_function(address)
        if function is not None:
            found[function.start_ea] = found.get(function.start_ea, set())
            if value is not None:
                found[function.start_ea].add(value)
    for address in field_load_sites():
        function = gw2re_ida.containing_function(address)
        if function is not None and function.start_ea not in found and compares_against_immediate(function.start_ea):
            found[function.start_ea] = set()
    return found


def normalized_text(start):
    try:
        return re.sub(r"\s+", " ", gw2re_ida.decompiled(start) or "")
    except Exception:
        return None


def statement_start(text, position):
    return max(text.rfind(";", 0, position), text.rfind("}", 0, position)) + 1


def asserted_name(text, position):
    tail = text[position:position + 600]
    expression = EXPRESSION_ASSERT.search(tail)
    formatted = FORMATTED_ASSERT.search(tail)
    if expression is not None:
        return leading_identifier(expression.group(1))
    if formatted is not None:
        return leading_identifier(formatted.group(2))
    return None


def leading_identifier(text):
    match = re.match(r"[A-Za-z_]\w*", text)
    return match.group(0) if match else None


def rows_for(start, kind):
    text = normalized_text(start)
    if text is None or kind == "type-compare" and ("EAF40" not in text and "EB0F0" not in text):
        return []
    found = []
    if kind == "inline-lookup":
        found.extend(inline_rows_for(start, text))
    elif kind == "def-iteration":
        found.extend(iteration_rows_for(start, text))
    else:
        found.extend(compare_rows_for(start, text))
    return found


def iteration_matches(text):
    for match in DEF_ITERATION.finditer(text):
        yield match, match.group(1), int(match.group(2))
    if HOLDER_LOAD.search(text):
        for match in DEF_ITERATION_SLOT.finditer(text):
            yield match, match.group(1), int(match.group(2))


def iteration_rows_for(start, text):
    rows = []
    for match, slot, content_type in iteration_matches(text):
        if content_type > MAX_CONTENT_TYPE:
            continue
        assigned = re.search(r"([A-Za-z_]\w*)\s*=(?!=)[^=]*$",
                             text[statement_start(text, match.start()):match.start()])
        if assigned is None:
            continue
        variable = assigned.group(1)
        tail = text[match.end():match.end() + 400]
        guard = re.compile(
            r";\s*(?:[^;{}()=]*=[^;{}]*;\s*)?if \( (?:" + re.escape(variable) + r"|!"
            + re.escape(variable) + r") == 0 \)"
        ).search(tail)
        if guard is None:
            guard = re.search(r"if \( !" + re.escape(variable) + r" \)", tail)
        if guard is None:
            continue
        name = asserted_name(tail, guard.end())
        if name is None:
            continue
        rows.append({
            "kind": "def-iteration",
            "contentType": "%x" % content_type,
            "name": name,
            "vtableSlotBytes": "%s + %dLL" % (slot, DEF_ITERATOR_SLOT),
            "function": gw2re_ida.function_name(start),
            "sourceFiles": sorted(set(SOURCE_FILE.findall(text)))[:4],
        })
    return rows


def inline_rows_for(start, text):
    rows = []
    for match in INLINE_LOOKUP.finditer(text):
        content_type = int(match.group(2))
        if content_type > MAX_CONTENT_TYPE:
            continue
        assigned = re.search(r"([A-Za-z_]\w*)\s*=(?!=)[^=]*$", text[statement_start(text, match.start()):match.start()])
        if assigned is None:
            continue
        variable = assigned.group(1)
        tail = text[match.end():match.end() + 400]
        guard = re.compile(
            r";\s*(?:[^;{}()=]*=[^;{}]*;\s*)?if \( (?:" + re.escape(variable) + r"|!"
            + re.escape(variable) + r") == 0 \)"
        ).search(tail)
        if guard is None:
            guard = re.search(r"if \( !" + re.escape(variable) + r" \)", tail)
        if guard is None:
            continue
        name = asserted_name(tail, guard.end())
        if name is None:
            continue
        rows.append({
            "kind": "inline-lookup",
            "contentType": "%x" % content_type,
            "name": name,
            "vtableSlotBytes": match.group(1),
            "function": gw2re_ida.function_name(start),
            "sourceFiles": sorted(set(SOURCE_FILE.findall(text)))[:4],
        })
    return rows


def compare_rows_for(start, text):
    rows = []
    for match in DIRECT_COMPARE.finditer(text):
        content_type = int(match.group(2))
        name = asserted_name(text, match.end())
        if name is None:
            continue
        rows.append({
            "kind": "type-compare",
            "contentType": "%x" % content_type,
            "name": name,
            "compare": match.group(1),
            "function": gw2re_ida.function_name(start),
            "sourceFiles": sorted(set(SOURCE_FILE.findall(text)))[:4],
        })
    for match in LOADED_COMPARE.finditer(text):
        window = text[match.end():match.end() + 300]
        compared_value = re.search(re.escape(match.group(1)) + r" (==|!=) (\d+)(?![\w.])", window)
        if compared_value is None:
            continue
        content_type = int(compared_value.group(2))
        if not 0 < content_type <= MAX_CONTENT_TYPE:
            continue
        name = asserted_name(text, match.end() + compared_value.end())
        if name is None:
            continue
        rows.append({
            "kind": "type-compare",
            "contentType": "%x" % content_type,
            "name": name,
            "compare": "loaded " + compared_value.group(1),
            "function": gw2re_ida.function_name(start),
            "sourceFiles": sorted(set(SOURCE_FILE.findall(text)))[:4],
        })
    return rows


def candidate_starts(helpers):
    lookups = candidate_functions(helpers, LOOKUP_TAIL_PATTERNS)
    iterations = candidate_functions(helpers, ITERATOR_TAIL_PATTERNS)
    compares = sorted(compared_functions())
    return lookups, iterations, compares


def export(output_path=None, bounds=None, iteration_bounds=None):
    output_path = output_path or gw2re_ida.default_output_path(".content-type-names.json")
    helpers = error_report_functions()
    lookups, iterations, compares = candidate_starts(helpers)
    rows = []
    for starts, kind, bound in ((lookups, "inline-lookup", bounds),
                                (iterations, "def-iteration", iteration_bounds)):
        low, high = (0, len(starts)) if bound is None else bound
        for start in starts[low:high]:
            rows.extend(rows_for(start, kind))
    for start in compares:
        rows.extend(rows_for(start, "type-compare"))
    pairs = sorted({(row["contentType"], row["name"]) for row in rows}, key=lambda pair: (int(pair[0], 16), pair[1]))
    payload = {
        "schemaVersion": 1,
        "kind": "content-type-name-export",
        "producedBy": "tools/ida/export_content_type_names.py",
        "classification": "build-local asserted names for content lookups; the assert text is evidence, not a type identity",
        "image": gw2re_ida.image_identity(),
        "assertHelperFunctions": [gw2re_ida.function_name(helper) for helper in helpers],
        "candidates": {
            "inlineLookupFunctions": len(lookups),
            "defIterationFunctions": len(iterations),
            "typeCompareFunctions": len(compares),
        },
        "rows": rows,
        "pairs": [{"contentType": content_type, "name": name} for content_type, name in pairs],
        "note": (
            "An inline-lookup row is a content context lookup that passes the type as an immediate and is "
            "immediately followed by a null check asserting the result. A def-iteration row is the same shape "
            "through the per-type def walker, whose second argument is the type and whose third is its state; "
            "a walk whose assert sits inside the loop names a member, so those rows come from the manual pass. "
            "A type-compare row is a comparison of the content type field against an immediate followed by an "
            "assert. Either fixes an id to an asserted name; a name asserted for two ids is a variable name, "
            "not a type name."
        ),
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "content-type-name-export", rows=len(rows), pairs=len(pairs), **payload["candidates"])
    print("[gw2re] wrote %d row(s), %d pair(s) to %s" % (summary["rows"], summary["pairs"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
