import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida


def function_addresses():
    return sorted(function.start_ea for function in gw2re_ida.database().functions.get_all())


def named_addresses():
    names = {}
    for address, name in gw2re_ida.database().names.get_all():
        names[name] = address
    return names


def export(output_path=None):
    build = gw2re_ida.image_build()
    output_path = output_path or gw2re_ida.build_path(build, "symbols.json")
    functions = function_addresses()
    names = named_addresses()
    payload = {
        "schemaVersion": 1,
        "kind": "build-symbols",
        "producedBy": "tools/ida/export_symbols.py",
        "classification": "one build's names beside its image identity; not durable across builds",
        "image": gw2re_ida.image_identity(),
        "functionCount": len(functions),
        "nameCount": len(names),
        "functions": ["%x" % address for address in functions],
        "names": {name: "%x" % names[name] for name in sorted(names)},
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "build-symbols",
        build=build, functions=len(functions), names=len(names))
    print("[export_symbols] wrote %s" % summary["artifact"])
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
