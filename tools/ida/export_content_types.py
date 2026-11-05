import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

THUNK_FORMS = (
    ("jump", b"\x48\x8b\x01\x44\x8b\xc2"),
    ("push", b"\x40\x53\x48\x83\xec\x20\x48\x8b\x01\x48\x8b\xda"),
    ("stack", b"\x48\x83\xec\x28\x48\x83\xc1\x08\xc7\x44\x24\x30\x00\x00\x00\x00\x4c\x8d\x44\x24\x30"),
    ("store", b"\x44\x8b\xc2\x48\x83\xc1\x08"),
)
MAX_CONTENT_TYPE = 0x1E3


def accessors():
    rows = {}
    for shape, prefix in THUNK_FORMS:
        offset = len(prefix)
        for address in gw2re_ida.find_pattern(prefix + b"\xba"):
            immediate = gw2re_ida.bytes_at(address + offset + 1, 4)
            if immediate is None:
                continue
            type_id = struct.unpack("<I", immediate)[0]
            if type_id <= MAX_CONTENT_TYPE and address not in rows:
                rows[address] = (type_id, shape)
    return sorted((type_id, address, shape) for address, (type_id, shape) in rows.items())


def content_type_asserts():
    rows = []
    for item in gw2re_ida.database().strings.get_all():
        value = item.contents.decode("utf-8", "replace")
        if "CONTENT_TYPE_" in value:
            rows.append({"address": gw2re_ida.address_text(item.address), "text": value})
    return rows


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".content-types.json")
    rows = accessors()
    table = []
    for type_id, thunk, shape in rows:
        table.append({
            "contentType": "%x" % type_id,
            "thunk": gw2re_ida.address_text(thunk),
            "shape": shape,
            "vtableSlots": sorted(gw2re_ida.address_text(reference)
                                  for reference in gw2re_ida.database().xrefs.data_refs_to_ea(thunk)),
        })

    payload = {
        "schemaVersion": 1,
        "kind": "content-type-accessor-export",
        "producedBy": "tools/ida/export_content_types.py",
        "classification": "build-local content type accessor table and assert names; not durable identities",
        "image": gw2re_ida.image_identity(),
        "accessors": table,
        "contentTypeAsserts": content_type_asserts(),
        "note": (
            "Each accessor is a getter that loads one content type and reaches the lookup, in one of four shapes; "
            "the slot fixes the id and never the name. A slot address is absolute in one analysed build. "
            "The CnContext vtable base is not derived here, so slot offsets need it from elsewhere."
        ),
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "content-type-accessor-export",
        accessors=len(table), contentTypeAsserts=len(payload["contentTypeAsserts"]))
    print("[gw2re] wrote %d accessor(s) and %d CONTENT_TYPE_ assert(s) to %s"
          % (summary["accessors"], summary["contentTypeAsserts"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
