import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

SOURCE_PATH_PATTERN = re.compile(r"(?:0)?[A-Za-z]:\\.*?\\Code\\.*?\.(?:c|cpp|h|hpp)$", re.IGNORECASE)
MAX_STRING_HITS = 4000
MAX_VTABLE_CANDIDATES = 4000
MIN_VTABLE_SLOTS = 3
POINTER_SIZE = 8


def source_path_anchors():
    anchors = []
    for item in gw2re_ida.database().strings.get_all():
        if len(anchors) >= MAX_STRING_HITS:
            break
        value = item.contents.decode("utf-8", "replace")
        if "\\Code\\" not in value and "\\Code/" not in value:
            continue
        if not SOURCE_PATH_PATTERN.search(value):
            continue
        anchors.append({
            "address": gw2re_ida.address_text(item.address),
            "value": value,
            "byteLength": item.length,
        })
    return anchors


def vtable_candidates():
    candidates = []
    starts = gw2re_ida.function_starts()

    def close_run(run_start, run_length, segment):
        if run_length >= MIN_VTABLE_SLOTS and len(candidates) < MAX_VTABLE_CANDIDATES:
            candidates.append({
                "address": gw2re_ida.address_text(run_start),
                "slotCount": run_length,
                "section": gw2re_ida.segment_name(segment),
            })

    for segment in gw2re_ida.data_segments():
        data = gw2re_ida.bytes_at(segment.start_ea, segment.end_ea - segment.start_ea)
        if not data:
            continue
        run_start = None
        run_length = 0
        for offset in range(0, len(data) - POINTER_SIZE + 1, POINTER_SIZE):
            target = struct.unpack_from("<Q", data, offset)[0]
            if target in starts:
                if run_start is None:
                    run_start = segment.start_ea + offset
                run_length += 1
            else:
                close_run(run_start, run_length, segment)
                run_start = None
                run_length = 0
        close_run(run_start, run_length, segment)
    return candidates


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".anchor-export.json")
    payload = {
        "schemaVersion": 1,
        "kind": "image-anchor-export",
        "producedBy": "tools/ida/export_anchors.py",
        "classification": "build-local analysis coordinates; not durable identities",
        "image": gw2re_ida.image_identity(),
        "sourcePathAnchors": source_path_anchors(),
        "vtableCandidates": vtable_candidates(),
        "note": (
            "Absolute addresses are coordinates inside one analysed build. Do not copy them into "
            "runtime code and do not reuse them for another build."
        ),
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "image-anchor-export",
        sourcePathAnchors=len(payload["sourcePathAnchors"]),
        vtableCandidates=len(payload["vtableCandidates"]))
    print("[gw2re] wrote anchor export to %s" % summary["artifact"])
    print("[gw2re] source path anchors: %d, vtable candidates: %d" % (
        summary["sourcePathAnchors"], summary["vtableCandidates"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
