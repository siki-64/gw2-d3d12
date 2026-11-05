import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ida_allins import NN_lea, NN_mov

import gw2re_ida

MIN_SLOTS = 2
MAX_SLOTS = 4096
MIN_LOOKAHEAD = 2
POINTER_SIZE = 8
IMAGE_SPAN = 0x4000000


def is_code_pointer(value):
    if value == 0:
        return False
    base = gw2re_ida.database().base_address
    if not base <= value < base + IMAGE_SPAN:
        return False
    try:
        return gw2re_ida.database().bytes.is_code_at(value)
    except Exception:
        return False


def looks_like_vtable(address):
    if not address:
        return False
    base = gw2re_ida.database().base_address
    if not base <= address < base + IMAGE_SPAN:
        return False
    code_count = 0
    for index in range(MIN_LOOKAHEAD + 2):
        if not is_code_pointer(gw2re_ida.qword_at(address + index * POINTER_SIZE)):
            break
        code_count += 1
        if code_count >= MIN_LOOKAHEAD:
            return True
    return False


def find_vptr_stores_in_function(function):
    db = gw2re_ida.database()
    stores = []
    tracked = {}

    for instruction in db.functions.get_instructions(function):
        if instruction.itype not in (NN_lea, NN_mov):
            continue
        mnemonic = db.instructions.get_mnemonic(instruction)
        operands = db.instructions.get_operands(instruction)
        if len(operands) < 2:
            continue
        written = gw2re_ida.canonical_register(gw2re_ida.register_name(operands[0]))

        if mnemonic == "lea":
            tracked.pop(written, None)
            destination = gw2re_ida.register_name(operands[0])
            target = gw2re_ida.memory_target(instruction, operands[1])
            if destination in gw2re_ida.POINTER_REGISTERS and target is not None:
                tracked[destination] = target
            continue

        if mnemonic != "mov":
            continue

        if gw2re_ida.is_memory_operand(operands[0]):
            source = gw2re_ida.register_name(operands[1])
            target = tracked.get(source)
            if target is None or not looks_like_vtable(target):
                continue
            text = db.instructions.get_disassembly(instruction, remove_tags=True)
            text = text.split(";", 1)[0].strip()
            stores.append({
                "vtable": "%x" % target,
                "storeAddress": "%x" % instruction.ea,
                "storeInstruction": text,
                "vptrOffset": gw2re_ida.memory_offset(text),
            })
            continue

        tracked.pop(written, None)

    return stores


def dedupe_stores(stores):
    seen = set()
    unique = []
    for store in stores:
        key = (store["vtable"], store["vptrOffset"])
        if key in seen:
            continue
        seen.add(key)
        unique.append(store)
    return unique


def read_slot_run(start_address, other_vtable_starts):
    slots = []
    for index in range(MAX_SLOTS):
        address = start_address + index * POINTER_SIZE
        if index > 0 and address in other_vtable_starts:
            break
        value = gw2re_ida.qword_at(address)
        if not is_code_pointer(value):
            break
        slots.append((index, value))
    return slots


def export(output_path=None):
    db = gw2re_ida.database()
    output_path = output_path or gw2re_ida.default_output_path(".vtables.json")

    constructors = []
    vtable_seen = set()
    duplicates_dropped = 0

    function_count = 0
    total_functions = sum(1 for _ in db.functions.get_all())
    print("[export_vtables] scanning %d functions..." % total_functions, flush=True)

    for function in db.functions.get_all():
        function_count += 1
        if function_count % 5000 == 0:
            print("[export_vtables] %d/%d" % (function_count, total_functions), flush=True)

        stores = find_vptr_stores_in_function(function)
        if not stores:
            continue

        before = len(stores)
        stores = dedupe_stores(stores)
        duplicates_dropped += before - len(stores)

        for store in stores:
            vtable_seen.add(int(store["vtable"], 16))
        constructors.append({
            "function": "sub_%X" % function.start_ea,
            "stores": stores,
        })

    print("[export_vtables] constructors: %d" % len(constructors), flush=True)
    print("[export_vtables] duplicates dropped: %d" % duplicates_dropped, flush=True)
    print("[export_vtables] unique vtable candidates: %d" % len(vtable_seen), flush=True)

    vtables = []
    total_slots = 0
    for index, vtable_address in enumerate(sorted(vtable_seen)):
        if index % 500 == 0:
            print("[export_vtables] vtables %d/%d" % (index, len(vtable_seen)), flush=True)
        slots = read_slot_run(vtable_address, vtable_seen)
        if len(slots) < MIN_SLOTS:
            continue
        slot_objects = []
        for slot_index, target in slots:
            function = gw2re_ida.containing_function(target)
            target_name = gw2re_ida.function_name(target) if function is not None else None
            slot_objects.append({
                "offset": slot_index * POINTER_SIZE,
                "targetAddress": "%x" % target,
                "target": target_name or "sub_%X" % target,
            })
        vtables.append({
            "address": "%x" % vtable_address,
            "slotCount": len(slot_objects),
            "slots": slot_objects,
        })
        total_slots += len(slot_objects)

    print("[export_vtables] kept %d vtables, %d slots" % (len(vtables), total_slots), flush=True)

    payload = {
        "schemaVersion": 1,
        "kind": "vtable-export",
        "producedBy": "tools/ida/export_vtables.py",
        "image": gw2re_ida.image_identity(),
        "vtableCount": len(vtables),
        "slotCount": total_slots,
        "constructorCount": len(constructors),
        "constructors": constructors,
        "vtables": vtables,
    }

    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "vtable-export",
        vtableCount=len(vtables), slotCount=total_slots, constructorCount=len(constructors))

    print("[export_vtables] wrote %s" % summary["artifact"], flush=True)
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
