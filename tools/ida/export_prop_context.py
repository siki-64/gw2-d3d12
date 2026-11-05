import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ida_domain.functions import FunctionFlags
from ida_domain.operands import OperandType

import gw2re_ida

PROP_CONTEXT_TLS_OFFSET = 0x10
TLS_ARRAY_OFFSET = 0x58
DESTRUCTOR_ASSERT = "context != GetContextSafe()"
ACCESSOR_MAX_INSTRUCTIONS = 8
REGISTRATION_MAX_INSTRUCTIONS = 6
REGISTRATION_MIN_CALLERS = 8
DESTROY_TABLE_REFERENCE_SLACK = 0x10
CALLEE_DEPTH = 2
INDEX_SEARCH_INSTRUCTION_COUNT = 12
SLOT_SIZE = 8
SLOT_INDEX_REGISTERS = ("ecx", "rcx", "cl")
CALLBACK_REGISTERS = ("rdx", "edx")


def instructions(function):
    return list(gw2re_ida.database().functions.get_instructions(function))


def call_reference_count(address):
    return sum(1 for _ in gw2re_ida.database().xrefs.code_refs_to_ea(address))


def small_functions(max_instructions):
    db = gw2re_ida.database()
    for function in db.functions.get_all():
        if db.functions.get_flags(function) & FunctionFlags.THUNK:
            continue
        if function.end_ea - function.start_ea > max_instructions * 15:
            continue
        heads = instructions(function)
        if 0 < len(heads) <= max_instructions and db.instructions.get_mnemonic(heads[-1]) == "retn":
            yield function, heads


def reads_tls_array(instruction):
    operand = gw2re_ida.database().instructions.get_operand(instruction, 1)
    if operand is None:
        return False
    formatted = operand.get_formatted_string() if hasattr(operand, "get_formatted_string") else None
    if not formatted or "gs:" not in formatted.lower():
        return False
    return gw2re_ida.memory_target(instruction, operand) == TLS_ARRAY_OFFSET


def loads_offset(instruction, offset):
    operand = gw2re_ida.database().instructions.get_operand(instruction, 1)
    if operand is None:
        return False
    if operand.type == OperandType.IMMEDIATE:
        return operand.get_value() == offset
    if gw2re_ida.is_memory_operand(operand):
        return operand.get_displacement() == offset
    return False


def find_tls_accessor():
    candidates = []
    for function, heads in small_functions(ACCESSOR_MAX_INSTRUCTIONS):
        if not any(reads_tls_array(head) for head in heads):
            continue
        if any(loads_offset(head, PROP_CONTEXT_TLS_OFFSET) for head in heads if not reads_tls_array(head)):
            candidates.append(function.start_ea)
    if not candidates:
        raise Exception("PropContext TLS accessor not found")
    return max(candidates, key=call_reference_count)


def find_destructor(accessor):
    for reference in gw2re_ida.database().xrefs.code_refs_to_ea(accessor):
        caller = gw2re_ida.containing_function(reference)
        if caller is not None and DESTRUCTOR_ASSERT in gw2re_ida.referenced_strings(caller.start_ea):
            return caller.start_ea
    raise Exception("PropContext destructor not found")


def is_table_store(instruction):
    db = gw2re_ida.database()
    if db.instructions.get_mnemonic(instruction) != "mov":
        return False
    operands = db.instructions.get_operands(instruction)
    if len(operands) < 2 or not gw2re_ida.is_memory_operand(operands[0]):
        return False
    destination = (operands[0].get_formatted_string() or "").replace(" ", "")
    return "*8" in destination and gw2re_ida.register_name(operands[1]) == "rdx"


def stored_table(heads):
    if not any(is_table_store(head) for head in heads):
        return None
    for head in heads:
        for target in gw2re_ida.database().xrefs.data_refs_from_ea(head.ea):
            return target
    return None


def registrars(address):
    found = set()
    for reference in gw2re_ida.database().xrefs.code_refs_to_ea(address):
        caller = gw2re_ida.containing_function(reference)
        if caller is not None:
            found.add(caller.start_ea)
    return found


def destructor_targets(destructor):
    db = gw2re_ida.database()
    targets = set()
    function = db.functions.get_at(destructor)
    for head in db.functions.get_instructions(function):
        targets.update(db.xrefs.data_refs_from_ea(head.ea))
        for operand in db.instructions.get_operands(head)[:2]:
            if operand.type == OperandType.IMMEDIATE:
                targets.add(operand.get_value())
    return targets


def find_registration_functions(destructor):
    candidates = []
    for function, heads in small_functions(REGISTRATION_MAX_INSTRUCTIONS):
        table = stored_table(heads)
        if table is not None and call_reference_count(function.start_ea) >= REGISTRATION_MIN_CALLERS:
            candidates.append((function.start_ea, table))
    destroy = None
    targets = destructor_targets(destructor)
    for address, table in candidates:
        if any(0 <= table - target <= DESTROY_TABLE_REFERENCE_SLACK for target in targets):
            destroy = address
    if destroy is None:
        raise Exception("PropContext destroy registration not found")
    destroy_registrars = registrars(destroy)
    others = [address for address, _ in candidates if address != destroy]
    create = max(others, key=lambda address: len(registrars(address) & destroy_registrars)) if others else None
    if create is None or not (registrars(create) & destroy_registrars):
        raise Exception("PropContext create registration not found")
    return create, destroy


def previous_instruction(instruction):
    previous = gw2re_ida.database().heads.get_previous(instruction.ea)
    if previous is None:
        return None
    return gw2re_ida.database().instructions.get_at(getattr(previous, "ea", previous))


def slot_index_before(call_address):
    db = gw2re_ida.database()
    instruction = db.instructions.get_at(call_address)
    for _ in range(INDEX_SEARCH_INSTRUCTION_COUNT):
        instruction = previous_instruction(instruction)
        if instruction is None:
            return None
        operands = db.instructions.get_operands(instruction)
        if not operands:
            continue
        mnemonic = db.instructions.get_mnemonic(instruction)
        destination = (gw2re_ida.register_name(operands[0]) or "").lower()
        if mnemonic in ("mov", "lea") and destination in SLOT_INDEX_REGISTERS:
            if len(operands) > 1 and operands[1].type == OperandType.IMMEDIATE:
                return operands[1].get_value()
            return None
        if mnemonic == "xor" and destination == "ecx":
            if len(operands) > 1 and (gw2re_ida.register_name(operands[1]) or "").lower() == "ecx":
                return 0
    return None


def callback_function_before(call_address):
    db = gw2re_ida.database()
    instruction = db.instructions.get_at(call_address)
    for _ in range(INDEX_SEARCH_INSTRUCTION_COUNT):
        instruction = previous_instruction(instruction)
        if instruction is None:
            return None
        mnemonic = db.instructions.get_mnemonic(instruction)
        operands = db.instructions.get_operands(instruction)
        if mnemonic not in ("mov", "lea") or not operands:
            continue
        if (gw2re_ida.register_name(operands[0]) or "").lower() not in CALLBACK_REGISTERS:
            continue
        targets = list(db.xrefs.data_refs_from_ea(instruction.ea))
        targets.extend(db.xrefs.code_refs_from_ea(instruction.ea))
        for target in targets:
            function = gw2re_ida.containing_function(target)
            if function is None:
                db.instructions.create(target)
                db.functions.create(target)
                function = gw2re_ida.containing_function(target)
            if function is not None:
                return function.start_ea
        return None
    return None


def registrations(registration_roles):
    db = gw2re_ida.database()
    found = []
    for registration_function, callback_role in registration_roles:
        for call_address in db.xrefs.code_refs_to_ea(registration_function):
            registrar = gw2re_ida.containing_function(call_address)
            if registrar is None:
                continue
            found.append((slot_index_before(call_address), callback_role, registrar.start_ea,
                          callback_function_before(call_address)))
    return sorted(found, key=lambda registration: (registration[0] is None, registration[0] or 0, registration[1]))


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".prop-context.c")
    accessor = find_tls_accessor()
    destructor = find_destructor(accessor)
    create_registration, destroy_registration = find_registration_functions(destructor)
    collection_functions = [accessor, destructor, create_registration, destroy_registration]
    seen = {create_registration, destroy_registration}
    found = registrations([(create_registration, "create"), (destroy_registration, "destroy")])
    with open(output_path, "w", encoding="utf-8", newline="\n") as output:
        output.write(gw2re_ida.program_header())
        output.write("// accessor %x destructor %x create registration %x destroy registration %x\n"
                     % tuple(collection_functions))
        output.write("\n// ===== PropContext =====\n")
        gw2re_ida.write_decompiled_functions(output, [(0, address) for address in collection_functions])
        for slot_index, callback_role, registrar, callback in found:
            slot_label = "unknown" if slot_index is None else "0x%X offset 0x%03X" % (slot_index, slot_index * SLOT_SIZE)
            callback_label = "%x" % callback if callback is not None else "unresolved"
            output.write("\n// ===== slot %s %s %s from registrar %x =====\n"
                         % (slot_label, callback_role, callback_label, registrar))
            if callback is not None:
                gw2re_ida.write_decompiled_functions(output, gw2re_ida.with_callees([callback], CALLEE_DEPTH, seen))
    summary = gw2re_ida.artifact_summary(
        output_path, "prop-context",
        slotRegistrations=len(found),
        accessor="%x" % accessor, destructor="%x" % destructor,
        createRegistration="%x" % create_registration, destroyRegistration="%x" % destroy_registration)
    print("wrote %d slot registrations to %s" % (summary["slotRegistrations"], summary["artifact"]))
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
