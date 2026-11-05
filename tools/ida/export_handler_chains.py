import csv
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ida_hexrays

import gw2re_ida

FUNCTION_PARAMETERS_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "function_parameters.txt")
LAYOUT_DIRECTORY = os.path.join(gw2re_ida.REPOSITORY_ROOT, "gw2-research", "src", "Gw2.Exe.Objects")
POINTER_SIZE = 8
VTABLE_SLOTS_LISTED = 8
EXECUTE_SLOT = 2
FOLLOW_DEPTH = 2
ALIAS_PASSES = 4
ALLOCATORS = ("ExeMemFlexible_Allocate",)
SKIPPED_CALLEES = ("Assert", "_guard")
VIRTUAL_TABLE_FIELD = "VirtualTablePointer"
VTABLE_SUFFIX = "_vftable"
ROOT_GETTER = re.compile(r"^(?:Get(\w+)|(\w+)_Get(?:Singleton)?|(\w+?)Get)$")
STRUCT_DECLARATION = re.compile(r"\bstruct (\w+)")
FIELD_DECLARATION = re.compile(r"\[FieldOffset\((0x[0-9A-Fa-f]+)\)(, Base)?\] internal (?:readonly )?([\w.<>]+)(\*?) (\w+);")
SLOT_DECLARATION = re.compile(r"\[Slot\((\d+)\)\] internal .*?partial ([\w.<>]+)(\*?) (\w+)\(([^)]*)\)")
COMPOUND_ASSIGNMENTS = {
    ida_hexrays.cot_asg: "=",
    ida_hexrays.cot_asgbor: "|=",
    ida_hexrays.cot_asgband: "&=",
    ida_hexrays.cot_asgxor: "^=",
    ida_hexrays.cot_asgadd: "+=",
    ida_hexrays.cot_asgsub: "-=",
}


class Layout:
    def __init__(self, name):
        self.name = name
        self.fields = {}
        self.bases = {}
        self.slots = {}
        self.slot_returns = {}
        self.slot_parameters = {}


class Layouts:
    def __init__(self, directory):
        self.layouts = {}
        self.declared_types = {}
        for root, _, files in os.walk(directory):
            for file in files:
                if file.endswith(".cs") and os.sep + "obj" + os.sep not in root + os.sep:
                    self.read(os.path.join(root, file))
        self.resolve()

    def read(self, path):
        stack = []
        depth = 0
        with open(path, encoding="utf-8") as file:
            for line in file:
                declaration = STRUCT_DECLARATION.search(line)
                if declaration:
                    name = ".".join([entry[0] for entry in stack] + [declaration.group(1)])
                    stack.append((name, depth))
                    self.layouts.setdefault(name, Layout(name))
                elif stack:
                    current = self.layouts[stack[-1][0]]
                    field = FIELD_DECLARATION.search(line)
                    slot = SLOT_DECLARATION.search(line)
                    if field:
                        offset = int(field.group(1), 16)
                        entry = (field.group(5), field.group(3), field.group(4) == "*", stack[-1][0])
                        (current.bases if field.group(2) else current.fields)[offset] = entry
                    elif slot:
                        current.slots[int(slot.group(1))] = slot.group(4)
                        current.slot_parameters[int(slot.group(1))] = [parameter.split()[-1] for parameter in slot.group(5).split(",") if parameter.strip()]
                        if slot.group(3):
                            current.slot_returns[int(slot.group(1))] = (slot.group(2), stack[-1][0])
                depth += line.count("{") - line.count("}")
                while stack and depth <= stack[-1][1] and "}" in line:
                    stack.pop()

    def resolve(self):
        for layout in self.layouts.values():
            for table in (layout.fields, layout.bases):
                for offset, (name, type_name, is_pointer, scope) in list(table.items()):
                    table[offset] = (name, self.type_in_scope(type_name, scope), is_pointer)

    def type_in_scope(self, type_name, scope):
        parts = scope.split(".")
        for length in range(len(parts), -1, -1):
            candidate = ".".join(parts[:length] + [type_name])
            if candidate in self.layouts:
                return candidate
        return None

    def field_at(self, type_name, offset, path=""):
        layout = self.layouts.get(type_name)
        if layout is None:
            return None
        if offset in layout.fields:
            name, field_type, is_pointer = layout.fields[offset]
            return path + name, field_type, is_pointer
        for base_offset in sorted(layout.bases, reverse=True):
            name, base_type, _ = layout.bases[base_offset]
            if base_offset <= offset:
                found = self.field_at(base_type, offset - base_offset, path + name + ".")
                if found:
                    return found
        return None

    def subobject_at(self, type_name, offset):
        layout = self.layouts.get(type_name)
        if layout is None or offset == 0:
            return type_name if offset == 0 else None
        for base_offset, (_, base_type, _) in layout.bases.items():
            if base_offset == offset:
                return base_type
            nested = self.subobject_at(base_type, offset - base_offset) if base_offset < offset else None
            if nested:
                return nested
        return None

    def container_of(self, type_name, offset):
        for layout in self.layouts.values():
            if layout.bases.get(-offset, (None, None))[1] == type_name:
                return layout.name
        return None

    def slot_name(self, type_name, slot):
        layout = self.layouts.get(type_name)
        if layout is None:
            return None
        if slot in layout.slots:
            return layout.slots[slot]
        if 0 in layout.bases:
            return self.slot_name(layout.bases[0][1], slot)
        return None

    def slot_parameter_names(self, type_name, slot):
        layout = self.layouts.get(type_name)
        if layout is None:
            return None
        if slot in layout.slot_parameters:
            return layout.slot_parameters[slot]
        if 0 in layout.bases:
            return self.slot_parameter_names(layout.bases[0][1], slot)
        return None

    def slot_return(self, type_name, slot):
        layout = self.layouts.get(type_name)
        if layout is None:
            return None
        if slot in layout.slot_returns:
            return_type, scope = layout.slot_returns[slot]
            return self.type_in_scope(return_type, scope)
        if 0 in layout.bases:
            return self.slot_return(layout.bases[0][1], slot)
        return None

    def vtable_owner(self, table_name):
        if not table_name or not table_name.endswith(VTABLE_SUFFIX):
            return None
        owner = table_name[:-len(VTABLE_SUFFIX)]
        return owner if owner in self.layouts else None

    def root_of(self, function_name):
        match = ROOT_GETTER.match(function_name or "")
        if not match:
            return None
        owner = match.group(1) or match.group(2) or match.group(3)
        return owner if owner in self.layouts else None


def read_function_parameters(path):
    parameters = {}
    with open(path, encoding="utf-8") as file:
        for line in file:
            name, separator, names = line.partition(":")
            if separator:
                parameters[name.strip()] = [entry.strip() for entry in names.split(",")]
    return parameters


FUNCTION_PARAMETERS = read_function_parameters(FUNCTION_PARAMETERS_PATH)


def slot_function_name(type_name, slot):
    table = gw2re_ida.name_address((type_name or "").split(".")[-1] + VTABLE_SUFFIX)
    if table is None:
        return None
    return gw2re_ida.function_name(gw2re_ida.qword_at(table + slot * POINTER_SIZE))


class Walk:
    def __init__(self, layouts, parameters):
        self.layouts = layouts
        self.types = {}
        self.aliases = {}
        self.parameters = {}
        for index, (name, type_name) in parameters.items():
            self.parameters[index] = name
            self.types[name] = type_name

    def typed(self, text, type_name):
        if type_name:
            self.types.setdefault(text, type_name)
        return text


def stripped(expression):
    while expression.op == ida_hexrays.cot_cast:
        expression = expression.x
    return expression


def offset_text(base, offset):
    return base if offset == 0 else "%s%+#x" % (base, offset)


def pointer_value(walk, base, offset):
    type_name = walk.types.get(base)
    if offset == 0:
        return base
    if type_name:
        subobject = walk.layouts.subobject_at(type_name, offset)
        if subobject:
            return walk.typed(offset_text(base, offset), subobject)
        if offset < 0:
            container = walk.layouts.container_of(type_name, offset)
            if container:
                return walk.typed(offset_text(base, offset), container)
    return offset_text(base, offset)


def loaded(walk, base, offset):
    type_name = walk.types.get(base)
    field = walk.layouts.field_at(type_name, offset) if type_name else None
    if field:
        name, field_type, is_pointer = field
        return walk.typed("%s.%s" % (base, name), field_type if is_pointer else None), True
    return "[%s]" % offset_text(base, offset), False


def location(walk, expression):
    expression = stripped(expression)
    if expression.op == ida_hexrays.cot_var:
        name = walk.aliases.get(expression.v.idx)
        return (name, 0) if name else None
    if expression.op in (ida_hexrays.cot_add, ida_hexrays.cot_sub):
        inner = location(walk, expression.x)
        amount = stripped(expression.y)
        if inner and amount.op == ida_hexrays.cot_num:
            offset = amount.numval() if expression.op == ida_hexrays.cot_add else -amount.numval()
            return inner[0], inner[1] + offset
    if expression.op == ida_hexrays.cot_memptr:
        inner = location(walk, expression.x)
        if inner:
            return inner[0], inner[1] + expression.m
    if expression.op == ida_hexrays.cot_ptr:
        inner = location(walk, expression.x)
        if inner:
            return loaded(walk, *inner)[0], 0
    if expression.op == ida_hexrays.cot_call:
        callee = stripped(expression.x)
        if callee.op == ida_hexrays.cot_obj:
            root = walk.layouts.root_of(gw2re_ida.function_name(callee.obj_ea))
            if root:
                return walk.typed(root, root), 0
        slot = virtual_slot(walk, callee)
        if slot:
            receiver, index, name = slot
            returned = walk.layouts.slot_return(walk.types.get(receiver), index)
            if returned:
                text = "%s.%s()" % (receiver, name or "slot%d" % index)
                return walk.typed(text, returned), 0
    return None


def global_name(address):
    try:
        return gw2re_ida.name_at(address) or "%x" % address
    except Exception:
        return "%#x" % address


def described(walk, expression):
    expression = stripped(expression)
    if expression.op == ida_hexrays.cot_num:
        return "%#x" % expression.numval()
    if expression.op == ida_hexrays.cot_fnum:
        return "float"
    if expression.op == ida_hexrays.cot_obj:
        return global_name(expression.obj_ea)
    if expression.op == ida_hexrays.cot_ref and stripped(expression.x).op == ida_hexrays.cot_obj:
        return "&" + described(walk, expression.x)
    if expression.op in (ida_hexrays.cot_ptr, ida_hexrays.cot_memptr):
        inner = location(walk, expression.x) if expression.op == ida_hexrays.cot_ptr else location(walk, expression)
        if inner:
            text, is_field = loaded(walk, *inner)
            size = expression.ptrsize if expression.op == ida_hexrays.cot_ptr else expression.type.get_size()
            return text if is_field else "%s:%d" % (text, size)
    address = location(walk, expression)
    if address:
        return pointer_value(walk, *address)
    if expression.op == ida_hexrays.cot_call:
        return (callee_text(walk, expression) or "?") + "()"
    return None


def virtual_slot(walk, callee):
    callee = stripped(callee)
    if callee.op != ida_hexrays.cot_ptr:
        return None
    table = stripped(callee.x)
    offset = 0
    if table.op == ida_hexrays.cot_add and stripped(table.y).op == ida_hexrays.cot_num:
        offset = stripped(table.y).numval()
        table = stripped(table.x)
    address = location(walk, table)
    if address is None or address[1] != 0:
        return None
    text = address[0]
    if text.endswith("." + VIRTUAL_TABLE_FIELD):
        receiver = text[:-len(VIRTUAL_TABLE_FIELD) - 1]
    elif text.startswith("[") and text.endswith("]"):
        receiver = text[1:-1]
    else:
        return None
    slot = offset // POINTER_SIZE
    type_name = walk.types.get(receiver)
    return receiver, slot, walk.layouts.slot_name(type_name, slot) if type_name else None


def callee_text(walk, call):
    callee = stripped(call.x)
    if callee.op == ida_hexrays.cot_obj:
        return gw2re_ida.function_name(callee.obj_ea) or "sub_%X" % callee.obj_ea
    if callee.op == ida_hexrays.cot_helper:
        return None
    slot = virtual_slot(walk, callee)
    if slot:
        receiver, index, name = slot
        return "%s.%s" % (receiver, name) if name else "%s slot %d" % (receiver, index)
    return "indirect"


class ChainVisitor(ida_hexrays.ctree_visitor_t):
    def __init__(self, walk):
        super().__init__(ida_hexrays.CV_FAST)
        self.walk = walk
        self.assignments = []
        self.calls = []
        self.direct_calls = []

    def visit_expr(self, expression):
        operator = COMPOUND_ASSIGNMENTS.get(expression.op)
        if operator is not None:
            if stripped(expression.x).op == ida_hexrays.cot_var:
                return 0
            self.assignments.append({
                "target": described(self.walk, expression.x),
                "operator": operator,
                "value": described(self.walk, expression.y),
            })
        elif expression.op == ida_hexrays.cot_call:
            name = callee_text(self.walk, expression)
            if name is None:
                return 0
            arguments = [described(self.walk, argument) for argument in expression.a]
            callee = stripped(expression.x)
            if callee.op == ida_hexrays.cot_obj:
                self.direct_calls.append((callee.obj_ea, arguments))
            call = {"callee": name, "arguments": arguments}
            slot = virtual_slot(self.walk, callee)
            parameter_names = None
            if slot:
                receiver_type = self.walk.types.get(slot[0])
                parameter_names = self.walk.layouts.slot_parameter_names(receiver_type, slot[1])
                if parameter_names:
                    parameter_names = ["this"] + parameter_names
                else:
                    function = slot_function_name(receiver_type, slot[1])
                    parameter_names = FUNCTION_PARAMETERS.get(function)
                    if function:
                        call["function"] = function
            elif stripped(callee).op == ida_hexrays.cot_obj:
                parameter_names = FUNCTION_PARAMETERS.get(name)
            if parameter_names:
                call["parameterNames"] = parameter_names
            self.calls.append(call)
        return 0


class AliasVisitor(ida_hexrays.ctree_visitor_t):
    def __init__(self):
        super().__init__(ida_hexrays.CV_FAST)
        self.sources = {}

    def visit_expr(self, expression):
        if expression.op in COMPOUND_ASSIGNMENTS and stripped(expression.x).op == ida_hexrays.cot_var:
            self.sources.setdefault(stripped(expression.x).v.idx, []).append(expression.y)
        return 0


def is_null(value):
    value = stripped(value)
    return value.op == ida_hexrays.cot_num and value.numval() == 0


def aliased_source(walk, value):
    address = location(walk, value)
    return pointer_value(walk, *address) if address is not None else None


def assign_aliases(walk, function):
    for index, variable_index in enumerate(function.argidx):
        walk.aliases[variable_index] = walk.parameters.get(index, "arg%d" % index)
    finder = AliasVisitor()
    finder.apply_to(function.body, None)
    for _ in range(ALIAS_PASSES):
        for variable_index, values in finder.sources.items():
            if variable_index in walk.aliases:
                continue
            sources = {aliased_source(walk, value) for value in values if not is_null(value)}
            if len(sources) == 1 and None not in sources:
                walk.aliases[variable_index] = sources.pop()


def walked(layouts, address, parameters):
    try:
        function = ida_hexrays.decompile(address)
    except ida_hexrays.DecompilationFailure:
        return None, None
    if function is None:
        return None, None
    walk = Walk(layouts, parameters)
    assign_aliases(walk, function)
    visitor = ChainVisitor(walk)
    visitor.apply_to(function.body, None)
    return walk, visitor


def routine(address):
    return {"address": "%x" % address, "name": gw2re_ida.function_name(address) or "sub_%X" % address}


def argument_parameters(walk, arguments):
    return {index: (argument, walk.types.get(argument)) for index, argument in enumerate(arguments) if argument}


def followed(layouts, address, parameters, depth):
    walk, visitor = walked(layouts, address, parameters)
    if visitor is None:
        return {**routine(address), "decompiled": False}
    entry = {**routine(address), "assignments": visitor.assignments, "calls": visitor.calls}
    if depth < FOLLOW_DEPTH:
        children = []
        seen = set()
        for callee, arguments in visitor.direct_calls:
            name = gw2re_ida.function_name(callee) or ""
            if callee in seen or name.startswith(SKIPPED_CALLEES):
                continue
            seen.add(callee)
            children.append(followed(layouts, callee, argument_parameters(walk, arguments), depth + 1))
        entry["callees"] = children
    return entry


def stored_tables(visitor):
    for assignment in visitor.assignments:
        if assignment["target"] in ("[object]:8", "object." + VIRTUAL_TABLE_FIELD) and assignment["value"]:
            value = assignment["value"].lstrip("&")
            address = gw2re_ida.name_address(value)
            if address is None and value.startswith("off_"):
                address = int(value[4:], 16)
            if address is not None:
                yield address, value


def vtable_slots(table):
    slots = []
    for index in range(VTABLE_SLOTS_LISTED):
        target = gw2re_ida.qword_at(table + index * POINTER_SIZE)
        if not target or gw2re_ida.containing_function(target) is None:
            break
        slots.append({"slot": index, **routine(target)})
    return slots


def built_object(layouts, address, record_position, allocation):
    parameters = {0: ("object", None), record_position: ("record", None)}
    walk, visitor = walked(layouts, address, parameters)
    if visitor is None:
        return {**routine(address), "decompiled": False}
    tables = list(stored_tables(visitor))
    owner = next((layouts.vtable_owner(name) for _, name in tables if layouts.vtable_owner(name)), None)
    if owner:
        parameters[0] = ("object", owner)
        walk, visitor = walked(layouts, address, parameters)
    entry = {
        **routine(address),
        "layout": owner,
        "allocationSize": allocation,
        "recordPosition": record_position,
        "assignments": visitor.assignments,
        "calls": visitor.calls,
        "vtables": [],
    }
    for table, name in tables:
        slots = vtable_slots(table)
        table_entry = {"address": "%x" % table, "name": name, "slots": slots}
        if len(slots) > EXECUTE_SLOT:
            table_entry["execute"] = followed(layouts, int(slots[EXECUTE_SLOT]["address"], 16), {0: ("object", owner)}, 0)
        entry["vtables"].append(table_entry)
    return entry


def allocation_size(visitor):
    for call in visitor.calls:
        if call["callee"] in ALLOCATORS and len(call["arguments"]) > 1:
            return call["arguments"][1]
    return None


def handler_chain(layouts, entry):
    handler = int(entry["handler"], 16)
    walk, visitor = walked(layouts, handler, {1: ("record", None)})
    chain = {"channel": entry["channel"], "messageId": entry["id"], "handler": routine(handler)}
    if visitor is None:
        chain["decompiled"] = False
        return chain
    chain["calls"] = visitor.calls
    allocation = allocation_size(visitor)
    builds = []
    others = []
    for callee, arguments in visitor.direct_calls:
        name = gw2re_ida.function_name(callee) or ""
        if "record" in arguments:
            builds.append(built_object(layouts, callee, arguments.index("record"), allocation))
        elif any("record" in (argument or "") for argument in arguments) and not name.startswith(SKIPPED_CALLEES):
            others.append(followed(layouts, callee, argument_parameters(walk, arguments), 1))
    chain["builds"] = builds
    chain["callees"] = others
    return chain


def receive_entries(path, wanted_ids):
    with open(path, newline="") as file:
        for row in csv.DictReader(file):
            if wanted_ids and (row["channel"], row["id"].lower()) not in wanted_ids:
                continue
            yield row


def wanted_message_ids(wanted):
    ids = set()
    for item in (wanted or "").split(","):
        if item:
            channel, _, message_id = item.rpartition(":")
            ids.add((channel or "game", message_id.lower()))
    return ids


def export(receive_handlers_path, output_path=None, wanted=None):
    ida_hexrays.init_hexrays_plugin()
    layouts = Layouts(LAYOUT_DIRECTORY)
    output_path = output_path or gw2re_ida.default_output_path(".handler-chains.json")
    chains = [handler_chain(layouts, entry) for entry in receive_entries(receive_handlers_path, wanted_message_ids(wanted))]
    payload = {
        "schemaVersion": 1,
        "kind": "handler-chain-export",
        "producedBy": "tools/ida/export_handler_chains.py",
        "image": gw2re_ida.image_identity(),
        "chainCount": len(chains),
        "chains": chains,
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(output_path, "handler-chain-export", chainCount=len(chains))
    print("[export_handler_chains] wrote %s" % summary["artifact"], flush=True)
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(3)[:3])
