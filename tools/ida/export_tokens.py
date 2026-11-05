import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ida_domain.operands import OperandType

import gw2re_ida

HASH_FUNCTION_SYMBOL = "NameHash"
MAX_BACK_INSTRUCTIONS = 32
MAX_NAME_LENGTH = 64
NAME_HASH_BIAS = 0x30000000
NAME_HASH_LETTER_WEIGHT = 23


def name_hash_digit(character):
    if not "a" <= character <= "z":
        return 0
    value = ord(character) - ord("a")
    if character == "z":
        value -= 1
    if character > "y":
        value -= 1
    if character == "v":
        value -= 4
    if character > "u":
        value -= 1
    if character > "j":
        value -= 1
    return value


def name_hash(name):
    value = 0
    for character in reversed(name):
        weight = NAME_HASH_LETTER_WEIGHT if "a" <= character <= "z" else 1
        value = (value * weight + name_hash_digit(character)) & 0xFFFFFFFF
    return (value + NAME_HASH_BIAS) & 0xFFFFFFFF


def string_argument_for_call(call_address):
    db = gw2re_ida.database()
    text = None
    string_address = None
    length = None
    address = call_address
    for _ in range(MAX_BACK_INSTRUCTIONS):
        instruction = db.instructions.get_previous(address)
        if instruction is None:
            break
        address = instruction.ea
        for target in db.xrefs.data_refs_from_ea(address):
            candidate = gw2re_ida.read_c_string(target)
            if candidate is not None and 1 <= len(candidate) <= MAX_NAME_LENGTH:
                text = candidate
                string_address = target
        operands = db.instructions.get_operands(instruction)
        if db.instructions.get_mnemonic(instruction) == "mov" and len(operands) > 1 \
                and operands[1].type == OperandType.IMMEDIATE:
            immediate = operands[1].get_value()
            if 1 <= immediate <= MAX_NAME_LENGTH:
                length = immediate
        if text is not None and length is not None:
            break
    if text is None:
        return None
    return string_address, text, length if length is not None else len(text)


def call_context(call_address, count=5):
    db = gw2re_ida.database()
    lines = []
    address = call_address
    for _ in range(count):
        instruction = db.instructions.get_previous(address)
        if instruction is None:
            break
        address = instruction.ea
        lines.append("%x %s" % (address, db.instructions.get_disassembly(instruction)))
    lines.reverse()
    return lines


def registry(hash_function):
    db = gw2re_ida.database()
    entries = {}
    unresolved = []
    references = list(db.xrefs.code_refs_to_ea(hash_function))
    for call_address in references:
        found = string_argument_for_call(call_address)
        if found is None:
            unresolved.append({
                "callSite": gw2re_ida.address_text(call_address),
                "caller": gw2re_ida.function_name(call_address),
                "context": call_context(call_address),
            })
            continue
        _, text, length = found
        name = text[:length].rstrip(" ")
        if name in entries:
            continue
        entries[name] = {
            "name": name,
            "token": name_hash(name),
            "length": length,
            "callSite": gw2re_ida.address_text(call_address),
            "caller": gw2re_ida.function_name(call_address),
        }
    print("[gw2re] references=%d resolved=%d unresolved=%d" % (len(references), len(entries), len(unresolved)))
    return sorted(entries.values(), key=lambda entry: entry["name"]), unresolved


def export(output_path=None, hash_function=None):
    output_path = output_path or gw2re_ida.default_output_path(".tokens.json")
    hash_function = int(hash_function, 16) if isinstance(hash_function, str) else (hash_function or gw2re_ida.name_address(HASH_FUNCTION_SYMBOL))
    function = gw2re_ida.containing_function(hash_function)
    entries, unresolved = registry(function.start_ea) if function is not None else ([], [])
    payload = {
        "schemaVersion": 1,
        "kind": "constant-token-export",
        "producedBy": "tools/ida/export_tokens.py",
        "classification": "build-local constant-token registry; not durable identities",
        "image": gw2re_ida.image_identity(),
        "hashFunction": gw2re_ida.address_text(function.start_ea) if function is not None else None,
        "hashFunctionC": gw2re_ida.decompiled(function.start_ea) if function is not None else None,
        "entries": entries,
        "unresolvedCalls": unresolved,
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "constant-token-export", entries=len(entries), unresolvedCalls=len(unresolved))
    print("[gw2re] wrote %d constant token(s) to %s" % (summary["entries"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(2)[:2])
