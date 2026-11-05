import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

ANCHOR_NAME_PATTERN = re.compile(r"\b[A-Za-z_][A-Za-z0-9_]*\b")
VTABLE_NAME_SUFFIX = "_vftable"
STRUCT_NAME_PATTERN = re.compile(r"\bstruct (\w+)")
CALLEE_DEPTH = 1
VTABLE_SLOT_COUNT = 4
POINTER_SIZE = 8


def repository_root():
    return os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def layout_directory():
    return os.path.join(repository_root(), "gw2-research", "src", "Gw2.Exe.Objects")


def anchors_by_struct():
    anchors = {}
    for directory, _, file_names in os.walk(layout_directory()):
        for file_name in sorted(file_names):
            if not file_name.endswith(".cs"):
                continue
            pending_functions = []
            pending_vtables = []
            with open(os.path.join(directory, file_name), encoding="utf-8") as layout_file:
                for line in layout_file:
                    if line.lstrip().startswith("//"):
                        for name in ANCHOR_NAME_PATTERN.findall(line):
                            address = gw2re_ida.name_addresses().get(name)
                            if address is None:
                                continue
                            if name.endswith(VTABLE_NAME_SUFFIX):
                                pending_vtables.append(address)
                            elif gw2re_ida.function_start(address) == address:
                                pending_functions.append(address)
                        continue
                    struct_match = STRUCT_NAME_PATTERN.search(line)
                    if struct_match and (pending_functions or pending_vtables):
                        anchors[struct_match.group(1)] = (pending_functions, pending_vtables)
                        pending_functions = []
                        pending_vtables = []
    return anchors


def vtable_slot_functions(vtable_address):
    starts = gw2re_ida.function_starts()
    functions = []
    slot_address = vtable_address
    for _ in range(VTABLE_SLOT_COUNT):
        target = gw2re_ida.qword_at(slot_address)
        if target in starts:
            functions.append(target)
        slot_address += POINTER_SIZE
    return functions


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".layout-anchors.c")
    anchors = anchors_by_struct()
    with open(output_path, "w", encoding="utf-8", newline="\n") as output:
        output.write(gw2re_ida.program_header())
        for struct_name in sorted(anchors):
            function_addresses, vtable_addresses = anchors[struct_name]
            roots = list(function_addresses)
            for vtable_address in vtable_addresses:
                roots.extend(vtable_slot_functions(vtable_address))
            output.write("\n// ===== %s =====\n" % struct_name)
            gw2re_ida.write_decompiled_functions(output, gw2re_ida.with_callees(roots, CALLEE_DEPTH))
    summary = gw2re_ida.artifact_summary(output_path, "layout-anchors", structs=len(anchors))
    print("wrote %d structs to %s" % (summary["structs"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
