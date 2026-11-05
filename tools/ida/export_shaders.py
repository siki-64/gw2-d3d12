import hashlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

MIN_LENGTH = 1000
MAX_STRINGS = 4000
MARKERS = (
    "uniform ",
    "SamplerState",
    "Texture2D",
    "SV_Target",
    "SV_TARGET",
    "struct ",
    "half4 ",
    "float4 ",
)


def shader_strings():
    results = []
    for item in gw2re_ida.database().strings.get_all():
        if len(results) >= MAX_STRINGS:
            break
        if item.length < MIN_LENGTH:
            continue
        value = item.contents.decode("utf-8", "replace")
        if len(value) < MIN_LENGTH or not any(marker in value for marker in MARKERS):
            continue
        results.append({
            "address": gw2re_ida.address_text(item.address),
            "length": len(value),
            "sha256": hashlib.sha256(value.encode("utf-8", "replace")).hexdigest(),
            "text": value,
        })
    return results


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".shaders.json")
    payload = {
        "schemaVersion": 1,
        "kind": "embedded-shader-export",
        "producedBy": "tools/ida/export_shaders.py",
        "classification": "build-local embedded engine shader source; not durable identities",
        "image": gw2re_ida.image_identity(),
        "strings": shader_strings(),
        "note": (
            "Absolute addresses are coordinates inside one analysed build. Do not copy them into "
            "runtime code and do not reuse them for another build."
        ),
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "embedded-shader-export", strings=len(payload["strings"]))
    print("[gw2re] wrote %d embedded shader string(s) to %s" % (summary["strings"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
