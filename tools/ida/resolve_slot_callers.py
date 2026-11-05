import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ida_hexrays
import idautils
import idc

import gw2re_ida
from export_sender_triggers import SourceFiles, function_start, is_function_pointer

CONTEXT_ACCESSOR = "PropContextGet"
CONTEXT_CREATE_REGISTRATION = "PropContext_RegisterCreate"
CONTEXT_SLOT_SIZE = 8
VTABLE_SUFFIX = "_vftable"
CREATE_SUFFIXES = ("_CreateContext", "_Create")
SCANNED_SOURCE_ROOT = "Gw2\\Game\\"
VALUE_DEPTH = 6
TYPE_DEPTH = 5
PROPAGATION_PASSES = 4
CREATE_CALLEE_DEPTH = 2
THUNK_HOPS = 3
FORWARDER_INSTRUCTIONS = 4
NULL_VALUE = ("number", 0)


def strip_casts(expression):
    while expression.op == ida_hexrays.cot_cast:
        expression = expression.x
    return expression


def visit(decompiled, expression_operation=None, statement_operation=None):
    found = []

    class Visitor(ida_hexrays.ctree_visitor_t):
        def __init__(self):
            super().__init__(ida_hexrays.CV_FAST)

        def visit_expr(self, expression):
            if expression.op == expression_operation:
                found.append(expression)
            return 0

        def visit_insn(self, instruction):
            if instruction.op == statement_operation:
                found.append(instruction)
            return 0

    Visitor().apply_to(decompiled.body, None)
    return found


def jump_target(address):
    for _ in range(THUNK_HOPS):
        if idc.print_insn_mnem(address) == "jmp" and idc.get_operand_type(address, 0) in (idc.o_near, idc.o_far):
            address = idc.get_operand_value(address, 0)
        else:
            break
    return address


def forwarded_slot(address):
    for _ in range(FORWARDER_INSTRUCTIONS):
        mnemonic = idc.print_insn_mnem(address)
        if mnemonic == "jmp" and idc.get_operand_type(address, 0) == idc.o_displ:
            return idc.get_operand_value(address, 0)
        if mnemonic in ("call", "ret", "retn"):
            return None
        address = idc.next_head(address)
    return None


def is_vtable(address):
    return idc.get_segm_name(address) == ".rdata" and is_function_pointer(address)


class FunctionFacts:
    def __init__(self, address, decompiled):
        self.address = address
        self.decompiled = decompiled
        self.sources = collections.defaultdict(list)
        for assignment in visit(decompiled, ida_hexrays.cot_asg):
            target = strip_casts(assignment.x)
            if target.op == ida_hexrays.cot_var:
                self.sources[target.v.idx].append(assignment.y)
        self.returns = self.single_return()
        self.stores = []
        self.vtable_stores = []
        for assignment in visit(decompiled, ida_hexrays.cot_asg):
            target = strip_casts(assignment.x)
            if target.op != ida_hexrays.cot_ptr:
                continue
            place = self.value(target.x)
            if place is None:
                continue
            stored = strip_casts(assignment.y)
            if stored.op == ida_hexrays.cot_ref:
                stored = strip_casts(stored.x)
            if stored.op == ida_hexrays.cot_obj and is_vtable(stored.obj_ea):
                self.vtable_stores.append((place, stored.obj_ea))
            else:
                value = self.value(assignment.y)
                if value is not None:
                    self.stores.append((place, value))
        self.calls = []
        self.virtual_calls = []
        for call in visit(decompiled, ida_hexrays.cot_call):
            callee = strip_casts(call.x)
            arguments = tuple(self.value(argument) for argument in call.a)
            if callee.op == ida_hexrays.cot_obj:
                self.calls.append((jump_target(callee.obj_ea), arguments))
                continue
            target = self.value(call.x)
            if not target or target[0] != "load" or not target[1] or target[1][0] != "add" or not arguments:
                continue
            table, offset = target[1][1], target[1][2]
            if table and table[0] == "load" and arguments[0] == table[1]:
                self.virtual_calls.append((arguments[0], offset, call.ea))
        self.decompiled = None
        self.sources = None

    def value(self, expression, depth=0):
        expression = strip_casts(expression)
        if depth > VALUE_DEPTH:
            return None
        if expression.op == ida_hexrays.cot_obj:
            return ("address", expression.obj_ea)
        if expression.op == ida_hexrays.cot_ptr:
            inner = strip_casts(expression.x)
            if inner.op == ida_hexrays.cot_obj:
                return ("global", inner.obj_ea)
            base = self.value(inner, depth + 1)
            return ("load", base) if base else None
        if expression.op == ida_hexrays.cot_add and strip_casts(expression.y).op == ida_hexrays.cot_num:
            base = self.value(expression.x, depth + 1)
            return ("add", base, strip_casts(expression.y).numval()) if base else None
        if expression.op == ida_hexrays.cot_call and strip_casts(expression.x).op == ida_hexrays.cot_obj:
            arguments = tuple(self.value(argument, depth + 1) for argument in expression.a)
            return ("call", jump_target(strip_casts(expression.x).obj_ea), arguments)
        if expression.op == ida_hexrays.cot_var:
            sources = self.sources.get(expression.v.idx, [])
            if self.decompiled.lvars[expression.v.idx].is_arg_var and not sources:
                return ("argument", expression.v.idx)
            if len(sources) == 1:
                return self.value(sources[0], depth + 1)
            values = {self.value(source, depth + 1) for source in sources} - {None, NULL_VALUE}
            return values.pop() if len(values) == 1 else None
        if expression.op == ida_hexrays.cot_num:
            return ("number", expression.numval())
        return None

    def single_return(self):
        values = []
        for instruction in visit(self.decompiled, statement_operation=ida_hexrays.cit_return):
            if instruction.creturn is not None:
                values.append(self.value(instruction.creturn.expr))
        values = [value for value in values if value and value != NULL_VALUE]
        return values[0] if values and all(value == values[0] for value in values) else None


class TypeGraph:
    def __init__(self, source_files):
        self.source_files = source_files
        self.facts = {}
        self.accessor = idc.get_name_ea_simple(CONTEXT_ACCESSOR)
        self.slot_types = {}
        self.subobjects = {}
        self.field_types = collections.defaultdict(collections.Counter)
        self.parameter_types = collections.defaultdict(collections.Counter)
        self.file_of = {}

    def of(self, address):
        if address not in self.facts:
            try:
                decompiled = ida_hexrays.decompile(address)
                self.facts[address] = FunctionFacts(address, decompiled) if decompiled is not None else None
            except Exception:
                self.facts[address] = None
        return self.facts[address]

    def source_file(self, address):
        if address not in self.file_of:
            placement = self.source_files.of(address) or {}
            self.file_of[address] = (placement.get("file") or "").rsplit("\\", 1)[-1]
        return self.file_of[address]

    def constructed_vtables(self, address):
        facts = self.of(address)
        return [vtable for place, vtable in facts.vtable_stores if place == ("argument", 0)] if facts else []

    def created_vtables(self, create, depth=0):
        create = jump_target(create)
        vtables = set(self.constructed_vtables(create))
        facts = self.of(create)
        if facts is not None and depth < CREATE_CALLEE_DEPTH:
            for callee, _ in facts.calls:
                if callee != create:
                    vtables |= set(self.constructed_vtables(callee))
                    if not vtables:
                        vtables |= self.created_vtables(callee, depth + 1)
        return vtables

    def class_of_create(self, create):
        name = idc.get_func_name(jump_target(create)) or ""
        for suffix in CREATE_SUFFIXES:
            if name.endswith(suffix):
                named = idc.get_name_ea_simple(name[:-len(suffix)] + VTABLE_SUFFIX)
                if named != idc.BADADDR:
                    return named
        vtables = self.created_vtables(create)
        named = sorted(vtable for vtable in vtables if (idc.get_name(vtable) or "").endswith(VTABLE_SUFFIX))
        if named:
            return named[0]
        return next(iter(vtables)) if len(vtables) == 1 else None

    def collect_context_slots(self):
        registration = idc.get_name_ea_simple(CONTEXT_CREATE_REGISTRATION)
        for reference in idautils.XrefsTo(registration):
            registrar = function_start(reference.frm)
            facts = self.of(registrar) if registrar is not None else None
            if facts is None:
                continue
            for callee, arguments in facts.calls:
                if callee != registration or len(arguments) != 2:
                    continue
                index, create = arguments
                if not index or index[0] != "number" or not create or create[0] != "address":
                    continue
                vtable = self.class_of_create(create[1])
                if vtable is not None:
                    self.slot_types[index[1] * CONTEXT_SLOT_SIZE] = vtable

    def subobject_vtables(self, vtable):
        if vtable not in self.subobjects:
            found = {}
            for reference in idautils.XrefsTo(vtable):
                constructor = function_start(reference.frm)
                facts = self.of(constructor) if constructor is not None else None
                if facts is None or (("argument", 0), vtable) not in facts.vtable_stores:
                    continue
                for place, stored in facts.vtable_stores:
                    if place[0] == "add" and place[1] == ("argument", 0):
                        found.setdefault(place[2], stored)
            self.subobjects[vtable] = found
        return self.subobjects[vtable]

    def owner_keys(self, base, function, environment, depth):
        keys = []
        owner = self.type_of(base, function, environment, depth + 1)
        if owner is not None:
            keys.append(owner)
        if base == ("argument", 0):
            keys.append(("file", self.source_file(function)))
        return keys

    def field_type(self, keys, offset):
        for key in keys:
            counter = self.field_types.get((key, offset))
            if counter:
                return counter.most_common(1)[0][0]
        return None

    def type_of(self, value, function, environment=None, depth=0):
        if value is None or depth > TYPE_DEPTH:
            return None
        kind = value[0]
        if kind == "argument":
            if environment is not None and value[1] in environment:
                return environment[value[1]]
            counter = self.parameter_types.get((function, value[1]))
            return counter.most_common(1)[0][0] if counter else None
        if kind == "load" and value[1] and value[1][0] == "add":
            base, offset = value[1][1], value[1][2]
            if base and base[0] == "call" and base[1] == self.accessor:
                return self.slot_types.get(offset)
            return self.field_type(self.owner_keys(base, function, environment, depth), offset)
        if kind == "load" and value[1]:
            return self.field_type(self.owner_keys(value[1], function, environment, depth), 0)
        if kind == "add":
            owner = self.type_of(value[1], function, environment, depth + 1)
            return self.subobject_vtables(owner).get(value[2]) if owner is not None else None
        if kind == "call":
            callee, arguments = value[1], value[2]
            constructed = self.constructed_vtables(callee)
            if constructed:
                return constructed[0]
            facts = self.of(callee)
            if facts is None or facts.returns is None:
                return None
            inner = {index: self.type_of(argument, function, environment, depth + 1) for index, argument in enumerate(arguments)}
            inner = {index: argument_type for index, argument_type in inner.items() if argument_type is not None}
            return self.type_of(facts.returns, callee, inner, depth + 1)
        return None

    def propagate(self, functions):
        for function in functions:
            facts = self.of(function)
            if facts is None:
                continue
            for place, stored in facts.stores:
                stored_type = self.type_of(stored, function)
                if stored_type is None:
                    continue
                if place[0] == "add":
                    base, offset = place[1], place[2]
                else:
                    base, offset = place, 0
                for key in self.owner_keys(base, function, None, 0):
                    self.field_types[(key, offset)][stored_type] += 1
            for callee, arguments in facts.calls:
                for index, argument in enumerate(arguments):
                    argument_type = self.type_of(argument, function)
                    if argument_type is not None:
                        self.parameter_types[(callee, index)][argument_type] += 1

    def virtual_calls(self, functions):
        calls = collections.defaultdict(set)
        for function in functions:
            facts = self.of(function)
            if facts is None:
                continue
            for receiver, offset, call in facts.virtual_calls:
                vtable = self.type_of(receiver, function)
                if vtable is not None:
                    calls[(vtable, offset)].add((function, call))
        for (vtable, offset), sites in list(calls.items()):
            slot = idc.get_qword(vtable + offset)
            forwarded = forwarded_slot(slot) if function_start(slot) == slot else None
            if forwarded is not None and forwarded != offset:
                calls[(vtable, forwarded)] |= sites
        return calls


def scanned_functions(source_files):
    return [function for function in idautils.Functions() if ((source_files.of(function) or {}).get("file") or "").startswith(SCANNED_SOURCE_ROOT)]


def resolve():
    ida_hexrays.init_hexrays_plugin()
    source_files = SourceFiles()
    graph = TypeGraph(source_files)
    graph.collect_context_slots()
    functions = scanned_functions(source_files)
    for _ in range(PROPAGATION_PASSES):
        graph.field_types.clear()
        graph.parameter_types.clear()
        graph.propagate(functions)
    return graph, graph.virtual_calls(functions)


def export(output_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".slot-callers.json")
    graph, calls = resolve()
    payload = {
        "schemaVersion": 1,
        "kind": "slot-caller-export",
        "producedBy": "tools/ida/resolve_slot_callers.py",
        "image": gw2re_ida.image_identity(),
        "contextSlots": {"%x" % offset: idc.get_name(vtable) or "%x" % vtable for offset, vtable in sorted(graph.slot_types.items())},
        "slots": [
            {
                "vtable": idc.get_name(vtable) or "%x" % vtable,
                "address": "%x" % vtable,
                "offset": "%x" % offset,
                "callers": [{"function": "%x" % function, "call": "%x" % call} for function, call in sorted(sites)],
            }
            for (vtable, offset), sites in sorted(calls.items())
        ],
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(output_path, "slot-caller-export", contextSlots=len(graph.slot_types), slots=len(calls))
    print("[resolve_slot_callers] wrote %s" % summary["artifact"], flush=True)
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
