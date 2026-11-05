import json
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import export_prop_context
import gw2re_ida

SOURCE_PREFIX = r"D:\Perforce\Live\NAEU\v2\Code"
REPORTED_FILES = 10

CONTEXT_FIELDS = [
    (0x000, "RandomContext"),
    (0x008, "MsgProp"),
    (0x010, "ScContext"),
    (0x018, "Unknown018"),
    (0x020, "TimeEventContext"),
    (0x028, "AgContext"),
    (0x030, "EvtContext"),
    (0x038, "Unknown038"),
    (0x040, "Unknown040"),
    (0x048, "Unknown048"),
    (0x050, "TextContext"),
    (0x058, "Unknown058"),
    (0x060, "ParameterOverrideContext"),
    (0x068, "AcctCliContext"),
    (0x070, "Unknown070"),
    (0x078, "AlContext"),
    (0x080, "GaeCliContext"),
    (0x088, "BigbCliContext"),
    (0x090, "Unknown090"),
    (0x098, "ChCliContext"),
    (0x0A0, "CtCliContext"),
    (0x0A8, "CinCliContext"),
    (0x0B0, "ClmCliContext"),
    (0x0B8, "CmbtCliContext"),
    (0x0C0, "CommCliContext"),
    (0x0C8, "CommerceContext"),
    (0x0D0, "Unknown0D0"),
    (0x0D8, "CfgContext"),
    (0x0E0, "CnContext"),
    (0x0E8, "ConFunCliContext"),
    (0x0F0, "ConvCliContext"),
    (0x0F8, "DiCliContext"),
    (0x100, "DlgCliContext"),
    (0x108, "DypCliContext"),
    (0x110, "Unknown110"),
    (0x118, "EmContext"),
    (0x120, "EmoteManager"),
    (0x128, "EvtCliContext"),
    (0x130, "DlCliInt"),
    (0x138, "GdCliContext"),
]


def source_tree(path):
    if not path:
        return "unknown"
    text = path.replace("/", "\\").lower()
    if text.startswith(SOURCE_PREFIX.lower()):
        text = text[len(SOURCE_PREFIX):].lstrip("\\")
    if text.startswith("gw2\\"):
        return "services" if text.startswith("gw2\\services\\") else "game"
    if text.startswith("arena\\"):
        return "engine"
    return "unknown"


def assert_files_by_function(asserts):
    index = defaultdict(Counter)
    for row in asserts.get("rows", []):
        function = row.get("function")
        file_name = row.get("file")
        if function and file_name:
            index[function][file_name] += 1
    return index


def prop_context_address():
    return export_prop_context.find_tls_accessor()


def callers_of(address):
    db = gw2re_ida.database()
    found = set()
    for reference in db.xrefs.code_refs_to_ea(address):
        function = gw2re_ida.containing_function(reference)
        if function is not None:
            found.add(function.start_ea)
    return found


def referenced_fields(function_address):
    text = gw2re_ida.decompiled(function_address)
    if text is None:
        return []
    found = []
    for offset, name in CONTEXT_FIELDS:
        if name in text:
            found.append((offset, name))
            continue
        if ("+ %#x" % offset) in text or ("+%xh" % offset) in text:
            found.append((offset, name))
    return found


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".context-tree-split.json")
    address = prop_context_address()
    print("[classify-contexts] PropContext accessor %x" % address)

    with open(gw2re_ida.default_output_path(".assert-sites.json"), "r", encoding="utf-8") as handle:
        asserts = json.load(handle)

    files_by_function = assert_files_by_function(asserts)
    callers = sorted(callers_of(address))
    print("[classify-contexts] %d direct callers" % len(callers))

    offset_sources = defaultdict(Counter)
    offset_functions = defaultdict(set)
    for index, function_address in enumerate(callers):
        if index % 25 == 0:
            print("[classify-contexts] %d/%d" % (index, len(callers)), flush=True)
        function_name = "sub_%X" % function_address
        for offset, _ in referenced_fields(function_address):
            offset_functions[offset].add(function_address)
            for file_name, count in files_by_function.get(function_name, {}).items():
                offset_sources[offset][file_name] += count

    report = []
    for offset, name in CONTEXT_FIELDS:
        sources = offset_sources.get(offset, Counter())
        top_file = sources.most_common(1)[0][0] if sources else None
        report.append({
            "offset": "%#06x" % offset,
            "field": name,
            "tree": source_tree(top_file),
            "topFile": top_file,
            "functionCount": len(offset_functions.get(offset, set())),
            "allFiles": [file_name for file_name, _ in sources.most_common(REPORTED_FILES)],
        })

    gw2re_ida.write_json(output_path, {"callers": len(callers), "fields": report})
    summary = gw2re_ida.artifact_summary(
        output_path, "context-tree-split", callers=len(callers), fields=len(report))
    print("[classify-contexts] wrote %s" % summary["artifact"])
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
