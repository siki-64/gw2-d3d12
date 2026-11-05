import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ida_bytes
import ida_name
import ida_segment
import idaapi
import idautils
from ida_allins import NN_call, NN_lea, NN_mov

import gw2re_ida
import import_layout_types as layouts

POINTER_SIZE = 8
MAX_SLOTS = 4096
VIRTUAL_TABLE_SUFFIX = "_vftable"
VIRTUAL_BASE_TABLE_SUFFIX = "_vbtable"
VIRTUAL_TABLE_POINTER = "VirtualTablePointer"
VIRTUAL_BASE_TABLE = "VirtualBaseTable"
VIRTUAL_BASE_ENTRY_COUNT = 8
INDEXED_ELEMENT = re.compile(r"^(\w+?)(\d+)$")
TABLE_SEGMENT = ".rdata"
CODE_SEGMENT = ".text"
VOLATILE_REGISTERS = ("rax", "rcx", "rdx", "r8", "r9", "r10", "r11")
ARGUMENT_REGISTERS = ("rcx", "rdx", "r8", "r9")


def is_code_pointer(value):
    segment = ida_segment.get_segm_by_name(CODE_SEGMENT)
    return segment.start_ea <= value < segment.end_ea


def address_of(name):
    address = ida_name.get_name_ea(idaapi.BADADDR, name)
    return None if address == idaapi.BADADDR else address


def table_names():
    return sorted(name for _, name in idautils.Names()
                  if name.endswith(VIRTUAL_TABLE_SUFFIX) or name.endswith(VIRTUAL_BASE_TABLE_SUFFIX))


def slot_count(address):
    count = 0
    while count < MAX_SLOTS:
        slot = address + count * POINTER_SIZE
        if count > 0 and any(True for _ in idautils.XrefsTo(slot)):
            break
        if not is_code_pointer(gw2re_ida.qword_at(slot)):
            break
        count += 1
    return count


def virtual_base_entries(address):
    entries = []
    for index in range(1, VIRTUAL_BASE_ENTRY_COUNT):
        slot = address + index * 4
        if index > 1 and any(True for _ in idautils.XrefsTo(slot)):
            break
        value = gw2re_ida.database().bytes.get_dword_at(slot)
        entries.append(value - (1 << 32) if value & 0x80000000 else value)
    return entries


def is_virtual_base_table(address):
    first = gw2re_ida.database().bytes.get_dword_at(address)
    second = gw2re_ida.database().bytes.get_dword_at(address + 4)
    return (first == 0 or first >= 0xFFFF0000) and 0 < second < 0x10000


def referenced_tables():
    segment = ida_segment.get_segm_by_name(TABLE_SEGMENT)
    tables = {}
    first = segment.start_ea + (-segment.start_ea) % POINTER_SIZE
    for head in range(first, segment.end_ea, POINTER_SIZE):
        if not ida_bytes.has_xref(ida_bytes.get_flags(head)):
            continue
        if is_code_pointer(gw2re_ida.qword_at(head)):
            tables[head] = "vftable"
        elif is_virtual_base_table(head):
            tables[head] = "vbtable"
    return tables


def memory_operand_text(instruction):
    text = gw2re_ida.database().instructions.get_disassembly(instruction, remove_tags=True).split(";", 1)[0]
    return text[text.find("[") + 1:text.find("]")]


def memory_base(memory):
    terms = memory.split("+")
    first = terms[0].strip()
    if "*" in first or first not in gw2re_ida.POINTER_REGISTERS:
        return None
    return first


def function_stores(start, tables):
    db = gw2re_ida.database()
    function = db.functions.get_at(start)
    stores = []
    if function is None:
        return stores
    tracked = {}
    pointers = {register: (register, 0) for register in ARGUMENT_REGISTERS}
    for instruction in db.functions.get_instructions(function):
        if instruction.itype == NN_call:
            for register in VOLATILE_REGISTERS:
                tracked.pop(register, None)
                pointers.pop(register, None)
            pointers["rax"] = ("call_%X" % instruction.ea, 0)
            continue
        operands = db.instructions.get_operands(instruction)
        if not operands:
            continue
        destination = gw2re_ida.canonical_register(gw2re_ida.register_name(operands[0]))
        if instruction.itype == NN_lea and len(operands) == 2:
            target = gw2re_ida.memory_target(instruction, operands[1])
            tracked[destination] = target
            pointers.pop(destination, None)
            if target is None:
                memory = memory_operand_text(instruction)
                base = memory_base(memory)
                if base is not None and memory.count("+") <= 1:
                    root, offset = pointers.get(base, (base, 0))
                    pointers[destination] = (root, offset + gw2re_ida.memory_offset("[" + memory + "]"))
            continue
        if instruction.itype == NN_mov and len(operands) == 2 and gw2re_ida.is_memory_operand(operands[0]):
            source = gw2re_ida.register_name(operands[1])
            table = tracked.get(source) if source in gw2re_ida.POINTER_REGISTERS else None
            if table not in tables:
                continue
            memory = memory_operand_text(instruction)
            base = memory_base(memory)
            if base is None:
                continue
            root, offset = pointers.get(base, (base, 0))
            indexed = memory.count("+") >= 2 or "*" in memory
            stores.append((table, root, offset + gw2re_ida.memory_offset("[" + memory + "]"), indexed))
            continue
        if destination is None:
            continue
        if instruction.itype == NN_mov and len(operands) == 2:
            source = gw2re_ida.register_name(operands[1])
            if source in gw2re_ida.POINTER_REGISTERS:
                tracked[destination] = tracked.get(source)
                pointers[destination] = pointers.get(source, (source, 0))
                continue
        tracked.pop(destination, None)
        pointers.pop(destination, None)
    return stores


def table_stores(address):
    stores = set()
    functions = {gw2re_ida.function_start(reference.frm) for reference in idautils.XrefsTo(address)}
    for start in sorted(function for function in functions if function is not None):
        stores.update((offset, indexed) for table, root, offset, indexed in function_stores(start, {address})
                      if table == address)
    return stores


class Catalog:
    def __init__(self):
        self.specs = layouts.collect()
        self.index = layouts.type_index(self.specs)
        self.top_level = {spec["name"]: spec for spec in self.specs.values()
                          if spec["kind"] == "struct" and spec["parent"] is None}

    def field_spec(self, scope, row):
        return layouts.resolve_spec(self.index, scope, row["type"])

    def primary_bases(self, spec):
        chain = [spec]
        while True:
            primary = next((row for row in chain[-1]["members"] if row.get("base") and row["offset"] == 0), None)
            target = self.field_spec(chain[-1], primary) if primary else None
            if target is None:
                return chain
            chain.append(target)

    def own_field(self, spec, name):
        for candidate in self.primary_bases(spec):
            row = next((row for row in candidate["members"] if row["name"] == name), None)
            if row is not None:
                return candidate, row
        return None, None

    def virtual_elements(self, spec, name):
        owner, row = self.own_field(spec, name)
        array = self.field_spec(owner, row) if row is not None and not row.get("base") else None
        if array is None or not array["inline_count"] or array["element"] is None:
            return None, []
        element = layouts.resolve_spec(self.index, array, array["element"]["type"])
        if element is None or not element["virtual"]:
            return None, []
        size = layouts.struct_size(self.index, element)
        return element, [row["offset"] + index * size for index in range(array["inline_count"])]

    def nested_type(self, spec, name):
        nested = self.index.get(spec["ida_name"] + "_" + name)
        return nested if nested is not None and nested["parent_ida"] == spec["ida_name"] else None

    def step(self, spec, token):
        owner, row = self.own_field(spec, token)
        if row is not None:
            target = self.field_spec(owner, row)
            if target is not None and target["virtual"] and not target["inline_count"]:
                return target, [row["offset"]]
        element, offsets = self.virtual_elements(spec, token)
        if element is not None:
            return element, offsets
        indexed = INDEXED_ELEMENT.match(token)
        if indexed:
            element, offsets = self.virtual_elements(spec, indexed.group(1))
            element_index = int(indexed.group(2))
            if element is not None and element_index < len(offsets):
                return element, [offsets[element_index]]
        nested = self.nested_type(spec, token)
        if nested is not None and nested["virtual"]:
            return nested, None
        return None, None

    def resolve(self, name):
        suffix = VIRTUAL_TABLE_SUFFIX if name.endswith(VIRTUAL_TABLE_SUFFIX) else VIRTUAL_BASE_TABLE_SUFFIX
        tokens = name[:-len(suffix)].split("_")
        spec = self.top_level.get(tokens[0])
        if spec is None:
            return None
        placements = [[(spec, 0)]]
        for token in tokens[1:]:
            spec, offsets = self.step(spec, token)
            if spec is None:
                return None
            if offsets is None:
                placements = [[(spec, 0)]]
                continue
            placements = [frames + [(spec, frames[-1][1] + offset)] for frames in placements for offset in offsets]
        if not spec["virtual"]:
            return None
        field_offset = 0
        if suffix == VIRTUAL_BASE_TABLE_SUFFIX:
            owner, row = self.own_field(spec, VIRTUAL_BASE_TABLE)
            if row is None:
                return None
            field_offset = row["offset"]
        kind = "vftable" if suffix == VIRTUAL_TABLE_SUFFIX else "vbtable"
        return {"kind": kind, "spec": spec, "placements": placements,
                "offsets": [frames[-1][1] + field_offset for frames in placements]}

    def virtual_base_table_offset(self, spec):
        owner, row = self.own_field(spec, VIRTUAL_BASE_TABLE)
        return row["offset"] if row is not None else None


def store_depth(catalog, resolved, stores, base_tables):
    depths = []
    for frames, offset in zip(resolved["placements"], resolved["offsets"]):
        for depth, (frame_spec, frame_offset) in enumerate(frames):
            relative = offset - frame_offset
            table_offset = catalog.virtual_base_table_offset(frame_spec)
            for displacement, indexed in stores:
                if not indexed and displacement == relative:
                    depths.append(depth)
                elif indexed and table_offset == displacement:
                    if relative - displacement in base_tables.get(frame_spec["ida_name"], []):
                        depths.append(depth)
    return min(depths) if depths else None


def database_tables():
    return {name: address_of(name) for name in table_names()}


def verify(named=None):
    named = database_tables() if named is None else named
    catalog = Catalog()
    problems = []
    resolved_by_name = {}
    for name in sorted(named):
        resolved = catalog.resolve(name)
        if resolved is None:
            problems.append("%s: names no virtual layout field" % name)
            continue
        resolved_by_name[name] = resolved
    base_tables = {}
    for name, resolved in resolved_by_name.items():
        if resolved["kind"] == "vbtable":
            base_tables.setdefault(resolved["spec"]["ida_name"], []).extend(virtual_base_entries(named[name]))
    covered = set()
    for name, resolved in sorted(resolved_by_name.items()):
        address = named[name]
        if resolved["kind"] == "vftable":
            covered.add(resolved["spec"]["ida_name"])
            declared = resolved["spec"]["slot_count"]
            counted = slot_count(address)
            if declared is not None and declared != counted:
                problems.append("%s: %d slots, %s declares SlotCount = %d"
                                % (name, counted, resolved["spec"]["ida_name"], declared))
        if store_depth(catalog, resolved, table_stores(address), base_tables) is None:
            problems.append("%s: no function stores it at +0x%X" % (name, resolved["offsets"][0]))
    for spec in sorted(catalog.specs.values(), key=lambda item: item["ida_name"]):
        if (spec["virtual"] and spec["parent_ida"] is None and spec["slot_count"] is not None
                and spec["ida_name"] not in covered):
            problems.append("%s: SlotCount = %d but no table is named for it" % (spec["ida_name"], spec["slot_count"]))
    return problems


if __name__ == "__main__":
    found = verify()
    print("\n".join(found) or "ok")
