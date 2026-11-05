import bisect
import collections
import csv
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ida_funcs
import ida_hexrays
import ida_nalt
import ida_name
import ida_segment
import ida_typeinf
import idautils
import idc

import gw2re_ida

SEND_FUNCTION = "MsgConn_Send"
AGENT_COMMAND_SOURCE = "Gw2\\Engine\\Agent\\AgCommand.cpp"
AGENT_COMMAND_RELEASE = "AgCommand_Release"
AGENT_WORLD_QUEUE = "AgWorld_QueueCommand"
AGENT_WORLD_REPORT_ID = 0x11
LOGIN_COMMAND_PREFIX = "GcAuthCmd_"
CLIENT_CONTEXT_PARAMETER = "a2"
CLIENT_CONTEXT_TYPE = 1
SINK_PARAMETER = "a3"
ANCESTOR_DEPTH = 12
ANCESTOR_LIMIT = 800
POINTER_SIZE = 8
IGNORED_DATA_SEGMENTS = (".pdata", ".xdata")
UNWIND_DATA_PREFIX = "stru_"
NAMED_ROOTS = {
    "VdfContext_ViewAdvance": "frame",
    "UiRoot_WndProc": "window-input",
    "j_UiRoot_WndProc": "window-input",
    "VdfContext_Startup": "startup",
    "MainCli_Startup": "startup",
    "AgWorld_Tick": "agent-simulation",
    AGENT_COMMAND_RELEASE: "agent-simulation",
}
TRANSACTION_TABLE = re.compile(r"Trans|Transaction")
CLASS_TABLE = re.compile(r"^(\w+?)(?:_VirtualTable|_vftable)$")
CLIENT_BRANCH = re.compile(r"if \( %s == %d \)\s*(\w+) = (\d+);" % (CLIENT_CONTEXT_PARAMETER, CLIENT_CONTEXT_TYPE))
CLIENT_FLAG = re.compile(r"(\w+) = %s == %d;" % (CLIENT_CONTEXT_PARAMETER, CLIENT_CONTEXT_TYPE))
FLAGGED_BRANCH = r"if \( %s \)\s*(\w+) = (\d+);"
SINK_CALL = re.compile(r"\(\*+\w*\)\(\s*%s,\s*([^,]+),\s*([^,]+),\s*&?(\w+)" % SINK_PARAMETER)
PART_HELPERS = {
    "LOBYTE": 0, "BYTE1": 1, "BYTE2": 2, "BYTE3": 3, "BYTE4": 4, "BYTE5": 5, "BYTE6": 6, "HIBYTE": -1,
    "LOWORD": 0, "WORD1": 2, "WORD2": 4, "HIWORD": -2, "LODWORD": 0, "HIDWORD": -4,
}
SOURCE_EXTENSION = ".cpp"
CONFIDENT_PLACEMENTS = ("asserted", "enclosed")
INSTALLER_CALLER_DEPTH = 4
SLOT_MAJORITY_MINIMUM = 2
CODE_SEGMENT = ".text"
FLOW_END_MNEMONICS = ("jmp", "retn", "ret", "int3")
SOURCE_ROOT_MARKER = "\\Code\\"
OBSERVED_CALLER_DEPTH = 8
SERVER_CONTEXT_ASSERT = "type == CONTEXT_TYPE_SERVER"


def function_start(address):
    function = ida_funcs.get_func(address)
    return function.start_ea if function else None


def name_of(address):
    return ida_funcs.get_func_name(address) or ida_name.get_name(address) or "%x" % address


def segment_of(address):
    segment = ida_segment.getseg(address)
    return ida_segment.get_segm_name(segment) if segment else ""


def decompile(address):
    try:
        return ida_hexrays.decompile(address)
    except ida_hexrays.DecompilationFailure:
        return None


def strip(expression):
    while expression.op == ida_hexrays.cot_cast:
        expression = expression.x
    return expression


def located_variable(expression):
    expression = strip(expression)
    if expression.op == ida_hexrays.cot_var:
        return expression.v.idx, 0
    if expression.op == ida_hexrays.cot_ref:
        inner = strip(expression.x)
        if inner.op == ida_hexrays.cot_var:
            return inner.v.idx, 0
        if inner.op == ida_hexrays.cot_idx and strip(inner.x).op == ida_hexrays.cot_var and inner.y.op == ida_hexrays.cot_num:
            return strip(inner.x).v.idx, inner.y.numval() * inner.type.get_size()
    if expression.op == ida_hexrays.cot_idx and strip(expression.x).op == ida_hexrays.cot_var and expression.y.op == ida_hexrays.cot_num:
        return strip(expression.x).v.idx, expression.y.numval() * expression.type.get_size()
    if expression.op == ida_hexrays.cot_call and expression.x.op == ida_hexrays.cot_helper and expression.x.helper in PART_HELPERS and len(expression.a) == 1:
        base = located_variable(expression.a[0])
        if base:
            whole = expression.a[0].type.get_size()
            part = PART_HELPERS[expression.x.helper]
            return base[0], base[1] + (whole + part if part < 0 else part)
    if expression.op == ida_hexrays.cot_ptr:
        return located_variable(expression.x)
    if expression.op == ida_hexrays.cot_add and expression.y.op == ida_hexrays.cot_num:
        base = located_variable(expression.x)
        if base:
            pointed = strip(expression.x).type
            scale = pointed.get_pointed_object().get_size() if pointed.is_ptr() else 1
            return base[0], base[1] + expression.y.numval() * max(scale, 1)
    return None


def value_source(function, expression, constants):
    expression = strip(expression)
    if expression.op == ida_hexrays.cot_num:
        return {"kind": "constant", "value": expression.numval()}
    if expression.op == ida_hexrays.cot_var:
        variable = function.lvars[expression.v.idx]
        if variable.is_arg_var:
            return {"kind": "parameter", "index": list(function.lvars).index(variable) + 1, "text": variable.name}
        if expression.v.idx in constants and len(constants[expression.v.idx]) == 1:
            return {"kind": "constant", "value": next(iter(constants[expression.v.idx]))}
    if expression.op == ida_hexrays.cot_obj:
        return {"kind": "global", "text": name_of(expression.obj_ea)}
    if expression.op == ida_hexrays.cot_call and expression.x.op == ida_hexrays.cot_obj:
        return {"kind": "call", "text": name_of(expression.x.obj_ea)}
    return {"kind": "expression", "text": expression.dstr()}


class RecordStores(ida_hexrays.ctree_visitor_t):
    def __init__(self, function):
        ida_hexrays.ctree_visitor_t.__init__(self, ida_hexrays.CV_FAST)
        self.function = function
        self.stores = []
        self.constants = collections.defaultdict(set)
        self.send_calls = []

    def visit_expr(self, expression):
        if expression.op == ida_hexrays.cot_asg:
            target = located_variable(expression.x)
            if target:
                self.stores.append((target, expression.x.type.get_size(), expression.y, expression.ea))
            if strip(expression.x).op == ida_hexrays.cot_var and strip(expression.y).op == ida_hexrays.cot_num:
                self.constants[strip(expression.x).v.idx].add(strip(expression.y).numval())
        elif expression.op == ida_hexrays.cot_call:
            self.send_calls.append(expression)
        return 0


def stack_offset(function, variable_index):
    variable = function.lvars[variable_index]
    return variable.location.stkoff() if variable.location.is_stkoff() else None


def nearest_stores(stores, call_address):
    by_target = collections.defaultdict(list)
    for store in stores:
        by_target[(store[0], store[1])].append(store)
    chosen = []
    for candidates in by_target.values():
        before = [store for store in candidates if store[3] != idc.BADADDR and store[3] <= call_address]
        chosen.append(max(before, key=lambda store: store[3]) if before else min(candidates, key=lambda store: store[3]))
    return chosen


def record_fields(function, visitor, record_expression, record_bytes, call_address, client_identifier=None):
    record = located_variable(record_expression)
    if record is None:
        return None, []
    record_start = stack_offset(function, record[0])
    message_id = client_identifier
    fields = []
    for (variable_index, offset), width, value, _ in nearest_stores(visitor.stores, call_address):
        start = stack_offset(function, variable_index)
        if record_start is None or start is None:
            if variable_index != record[0]:
                continue
            field_offset = offset - record[1]
        else:
            field_offset = start + offset - record_start - record[1]
        if field_offset < 0 or (record_bytes and field_offset >= record_bytes):
            continue
        source = value_source(function, value, visitor.constants)
        if field_offset == 0 and width == 2:
            if message_id is None and source["kind"] == "constant":
                message_id = source["value"]
            elif message_id is None and source["kind"] == "expression":
                message_id = resolved_identifier(function, value, visitor.constants)
            continue
        fields.append({"offset": field_offset, "bytes": width, "value": source})
    unique = {}
    for field in fields:
        unique.setdefault((field["offset"], field["bytes"], str(field["value"])), field)
    return message_id, sorted(unique.values(), key=lambda field: (field["offset"], field["bytes"]))


def resolved_identifier(function, value, constants):
    value = strip(value)
    if value.op == ida_hexrays.cot_var and len(constants.get(value.v.idx, ())) == 1:
        return next(iter(constants[value.v.idx]))
    return None


def callers_of(address):
    return sorted({start for start in (function_start(reference) for reference in idautils.CodeRefsTo(address, 0)) if start is not None})


def direct_sends(send_function):
    sends = []
    for site in idautils.CodeRefsTo(send_function, 0):
        start = function_start(site)
        function = decompile(start) if start is not None else None
        if function is None:
            sends.append({"address": "%x" % start if start else "%x" % site, "site": "%x" % site, "failure": "not decompiled"})
            continue
        visitor = RecordStores(function)
        visitor.apply_to(function.body, None)
        call = next((expression for expression in visitor.send_calls
                     if expression.x.op == ida_hexrays.cot_obj and expression.x.obj_ea == send_function and expression.ea == site), None)
        call = call or next((expression for expression in visitor.send_calls
                             if expression.x.op == ida_hexrays.cot_obj and expression.x.obj_ea == send_function), None)
        if call is None or len(call.a) < 3:
            sends.append({"address": "%x" % start, "site": "%x" % site, "failure": "call not found"})
            continue
        size = strip(call.a[1])
        record_bytes = size.numval() if size.op == ida_hexrays.cot_num else None
        message_id, fields = record_fields(function, visitor, call.a[2], record_bytes, call.ea)
        sends.append({
            "address": "%x" % start,
            "site": "%x" % site,
            "name": name_of(start),
            "path": "direct",
            "messageId": message_id,
            "recordBytes": record_bytes,
            "connection": call.a[0].dstr(),
            "fields": fields,
            "failure": None if message_id is not None else "identifier not resolved",
        })
    return sends


def source_span(source_suffix):
    starts = set()
    for string in idautils.Strings():
        if str(string).endswith(source_suffix):
            for reference in idautils.XrefsTo(string.ea):
                start = function_start(reference.frm)
                if start is not None:
                    starts.add(start)
    return (min(starts), max(starts)) if starts else (None, None)


def agent_command_sends():
    first, last = source_span(AGENT_COMMAND_SOURCE)
    sends = []
    if first is None:
        return sends
    for start in idautils.Functions(first, last + 1):
        function = decompile(start)
        if function is None:
            continue
        text = str(function)
        match = SINK_CALL.search(text)
        if not match or SERVER_CONTEXT_ASSERT in text:
            continue
        visitor = RecordStores(function)
        visitor.apply_to(function.body, None)
        call = next((expression for expression in visitor.send_calls
                     if expression.x.op != ida_hexrays.cot_obj and len(expression.a) >= 4
                     and strip(expression.a[0]).op == ida_hexrays.cot_var and function.lvars[strip(expression.a[0]).v.idx].name == SINK_PARAMETER), None)
        if call is None:
            continue
        size = strip(call.a[2])
        record_bytes = size.numval() if size.op == ida_hexrays.cot_num else None
        for client_identifier in client_identifiers(text) or [None]:
            message_id, fields = record_fields(function, visitor, call.a[3], record_bytes, call.ea, client_identifier)
            sends.append({
                "address": "%x" % start,
                "site": "%x" % call.ea,
                "name": name_of(start),
                "path": "agent-command",
                "messageId": message_id,
                "recordBytes": record_bytes if record_bytes is not None else size.dstr(),
                "connection": "agent command sink",
                "fields": fields,
                "failure": None if message_id is not None else "identifier not resolved",
            })
    return sends


def client_identifiers(text):
    identifiers = [int(value) for _, value in CLIENT_BRANCH.findall(text)]
    for flag in CLIENT_FLAG.findall(text):
        identifiers += [int(value) for _, value in re.findall(FLAGGED_BRANCH % flag, text)]
    return sorted(set(identifiers))


def agent_sink_forwarder():
    release = idc.get_name_ea_simple(AGENT_COMMAND_RELEASE)
    function = decompile(release) if release != idc.BADADDR else None
    match = re.search(r"(?:off|unk)_([0-9A-F]+);", str(function)) if function else None
    if not match:
        return None
    return "%x" % idc.get_qword(int(match.group(1), 16))


def agent_world_report():
    address = idc.get_name_ea_simple(AGENT_WORLD_QUEUE)
    if address == idc.BADADDR:
        return []
    return [{
        "address": "%x" % address,
        "site": "%x" % address,
        "name": AGENT_WORLD_QUEUE,
        "path": "agent-world-report",
        "messageId": AGENT_WORLD_REPORT_ID,
        "recordBytes": 4,
        "connection": "world+0x10 connection",
        "fields": [{"offset": 2, "bytes": 2, "value": {"kind": "expression", "text": "min(agent+0x0c, 0xfffe)"}}],
        "failure": None,
    }]


class SourceFiles:
    def __init__(self):
        placed = {}
        for string in idautils.Strings():
            text = str(string)
            if not text.lower().endswith(SOURCE_EXTENSION) or SOURCE_ROOT_MARKER not in text:
                continue
            relative = text[text.find(SOURCE_ROOT_MARKER) + len(SOURCE_ROOT_MARKER):]
            for reference in idautils.XrefsTo(string.ea):
                start = function_start(reference.frm)
                if start is not None:
                    placed.setdefault(start, relative)
        self.starts = sorted(placed)
        self.files = [placed[start] for start in self.starts]

    def of(self, address):
        index = bisect.bisect_right(self.starts, address) - 1
        before = self.files[index] if index >= 0 else None
        after = self.files[index + 1] if index + 1 < len(self.files) else None
        if index >= 0 and self.starts[index] == address:
            return {"file": before, "placement": "asserted"}
        if before and before == after:
            return {"file": before, "placement": "enclosed"}
        return {"file": before, "placement": "preceding"} if before else None


def is_function_pointer(address):
    target = idc.get_qword(address)
    return function_start(target) == target


def is_code_pointer(address):
    return segment_of(idc.get_qword(address)) == CODE_SEGMENT


def table_reference(address):
    start = address
    while is_code_pointer(start - POINTER_SIZE) and not ida_name.get_name(start):
        start -= POINTER_SIZE
    name = ida_name.get_name(start) or "table_%x" % start
    return name, address - start


def define_orphan_code(address):
    start = idc.get_item_head(address)
    while True:
        previous = idc.prev_head(start, 0)
        if previous == idc.BADADDR or not idc.is_code(idc.get_full_flags(previous)) or idc.print_insn_mnem(previous) in FLOW_END_MNEMONICS:
            break
        start = previous
    return start if ida_funcs.add_func(start) else None


def ancestry(start, receive_handlers):
    depth_of = {start: 0}
    frontier = [start]
    evidence = {"inbound": set(), "roots": set(), "tables": set(), "functionPointers": set()}
    truncated = False
    for depth in range(ANCESTOR_DEPTH):
        following = []
        for address in frontier:
            name = name_of(address)
            if address in receive_handlers:
                evidence["inbound"].update(receive_handlers[address])
                continue
            if name in NAMED_ROOTS:
                evidence["roots"].add(name)
            for reference in idautils.XrefsTo(address):
                if segment_of(reference.frm) in IGNORED_DATA_SEGMENTS:
                    continue
                caller = function_start(reference.frm)
                if caller is None and reference.iscode:
                    caller = define_orphan_code(reference.frm)
                if caller is not None and reference.iscode:
                    if caller not in depth_of:
                        depth_of[caller] = depth + 1
                        following.append(caller)
                elif caller is not None:
                    evidence["functionPointers"].add(name_of(caller))
                elif is_function_pointer(reference.frm):
                    table, back = table_reference(reference.frm)
                    if table and not table.startswith(UNWIND_DATA_PREFIX):
                        evidence["tables"].add("%s+%x" % (table, back))
        frontier = following
        if len(depth_of) > ANCESTOR_LIMIT:
            truncated = True
            break
    result = {key: sorted(value) for key, value in evidence.items()}
    result["ancestorCount"] = len(depth_of)
    result["truncated"] = truncated
    return result


def installer_caller_files(installers, source_files):
    visited = set(installers)
    frontier = list(installers)
    for _ in range(INSTALLER_CALLER_DEPTH):
        files = []
        following = []
        for function in frontier:
            for reference in idautils.XrefsTo(function):
                caller = function_start(reference.frm)
                if caller is None or caller in visited:
                    continue
                visited.add(caller)
                following.append(caller)
                placement = source_files.of(caller) or {}
                if placement.get("placement") in CONFIDENT_PLACEMENTS:
                    files.append(placement["file"].rsplit("\\", 1)[-1])
        if files:
            return files
        frontier = following
    return []


def slot_majority_files(start, source_files):
    files = collections.Counter()
    address = start
    while is_code_pointer(address) and (address == start or not ida_name.get_name(address)):
        slot = function_start(idc.get_qword(address))
        placement = (source_files.of(slot) or {}) if slot is not None else {}
        if placement.get("placement") in CONFIDENT_PLACEMENTS:
            files[placement["file"].rsplit("\\", 1)[-1]] += 1
        address += POINTER_SIZE
    if not files:
        return []
    ranked = files.most_common(2)
    file, count = ranked[0]
    runner_up = ranked[1][1] if len(ranked) > 1 else 0
    return [file] if count >= SLOT_MAJORITY_MINIMUM and count * 2 >= sum(files.values()) and count > runner_up else []


def table_owners(tables, source_files):
    owners = {}
    for table in tables:
        name = table.split("+")[0]
        start = int(name[len("table_"):], 16) if name.startswith("table_") else idc.get_name_ea_simple(name)
        references = list(idautils.XrefsTo(start)) or list(idautils.XrefsTo(start - POINTER_SIZE))
        installers = [function_start(reference.frm) for reference in references if function_start(reference.frm) is not None]
        placements = [source_files.of(installer) or {} for installer in installers]
        files = [placement["file"].rsplit("\\", 1)[-1] for placement in placements if placement.get("placement") in CONFIDENT_PLACEMENTS]
        if not files:
            files = installer_caller_files(installers, source_files)
        if not files:
            files = slot_majority_files(start, source_files)
        if files:
            owners[name] = collections.Counter(files).most_common(1)[0][0]
    return owners


def trigger_kinds(send, evidence):
    kinds = []
    if send["path"] == "agent-command":
        kinds.append("agent-command")
    if send["path"] == "agent-world-report":
        kinds.append("agent-simulation")
    if evidence["inbound"]:
        kinds.append("inbound")
    for root in evidence["roots"]:
        kinds.append(NAMED_ROOTS[root])
    for table in evidence["tables"]:
        name = table.split("+")[0]
        owner = evidence["tableOwners"].get(name)
        if TRANSACTION_TABLE.search(name) or (owner and TRANSACTION_TABLE.search(owner)):
            kinds.append("login-transaction")
        elif CLASS_TABLE.match(name) or owner:
            kinds.append("client-api")
        else:
            kinds.append("function-table")
    if evidence["functionPointers"]:
        kinds.append("callback")
    if not kinds:
        kinds.append("unreached")
    return sorted(set(kinds))


def channel_of(send, send_ids):
    message_id = send["messageId"]
    in_login = ("login", message_id) in send_ids
    in_game = ("game", message_id) in send_ids
    if in_login and (not in_game or send["name"].startswith(LOGIN_COMMAND_PREFIX) or login_span_contains(send)):
        return "login"
    return "game" if in_game else None


_login_span = None


def login_span_contains(send):
    global _login_span
    if _login_span is None:
        starts = [address for address, name in idautils.Names() if name.startswith(LOGIN_COMMAND_PREFIX) and function_start(address) == address]
        _login_span = (min(starts), max(starts)) if starts else (0, -1)
    address = int(send["address"], 16)
    end = ida_funcs.get_func(_login_span[1])
    return _login_span[0] <= address < (end.end_ea if end else _login_span[1])


def parameter_arguments(send):
    sources = {}
    start = int(send["address"], 16)
    for field in send["fields"]:
        if field["value"]["kind"] != "parameter":
            continue
        index = field["value"]["index"]
        for caller in callers_of(start):
            function = decompile(caller)
            if function is None:
                continue
            visitor = RecordStores(function)
            visitor.apply_to(function.body, None)
            for call in visitor.send_calls:
                if call.x.op == ida_hexrays.cot_obj and call.x.obj_ea == start and len(call.a) >= index:
                    sources.setdefault(str(index), []).append({"caller": name_of(caller), "argument": call.a[index - 1].dstr()})
    return sources


def match_key(send, send_ids):
    source = (send.get("sourceFile") or {}).get("file") or ""
    return {
        "schemaShape": send_ids.get((send["channel"], send["messageId"])),
        "recordBytes": send["recordBytes"],
        "recordLayout": " ".join("%x:%d" % (field["offset"], field["bytes"]) for field in send["fields"]),
        "sourceFile": source.rsplit("\\", 1)[-1],
        "path": send["path"],
        "fieldValues": " ".join(field_signature(field["value"]) for field in send["fields"]),
        "tableSlots": " ".join(sorted({entry.split("+")[1] for entry in send["evidence"]["tables"]})),
        "inbound": " ".join(send["evidence"]["inbound"]),
        "callerFiles": " ".join(send["callerFiles"]),
    }


def field_signature(value):
    if value["kind"] == "constant":
        return "c%x" % value["value"]
    if value["kind"] == "parameter":
        return "p%d" % value["index"]
    if value["kind"] in ("call", "global") and not gw2re_ida.AUTO_NAME.match(value["text"]):
        return value["text"]
    return value["kind"][0]


def read_send_ids(path):
    with open(path, newline="") as file:
        return {(row["channel"], int(row["id"], 16)): row.get("shape", "") for row in csv.DictReader(file)}


def read_send_stacks(path):
    if not path or not os.path.exists(path):
        return []
    with open(path, newline="") as file:
        return [(int(row["id"], 16), int(row["count"]), [int(rva, 16) for rva in row["returnAddressRvas"].split()]) for row in csv.DictReader(file)]


def frame_text(return_address, source_files):
    start = function_start(return_address)
    if start is None:
        return "%x" % return_address
    source = ((source_files.of(start) or {}).get("file") or "").rsplit("\\", 1)[-1]
    return "%s%s" % (name_of(start), " (%s)" % source if source else "")


def attach_observed_callers(sends, send_stacks, source_files):
    image_base = ida_nalt.get_imagebase()
    by_sender = collections.defaultdict(list)
    for send in sends:
        if "name" in send and send["messageId"] is not None:
            by_sender[(int(send["address"], 16), send["messageId"])].append(send)
    unmatched = collections.Counter()
    for message_id, count, return_address_rvas in send_stacks:
        frames = [image_base + rva for rva in return_address_rvas]
        matched = None
        for index, frame in enumerate(frames):
            start = function_start(frame)
            if start is not None and (start, message_id) in by_sender:
                matched = (by_sender[(start, message_id)], frames[index + 1:index + 1 + OBSERVED_CALLER_DEPTH])
                break
        if matched is None:
            unmatched["%x" % message_id] += count
            continue
        chain = [frame_text(frame, source_files) for frame in matched[1]]
        for send in matched[0]:
            observed = send.setdefault("observedCallers", [])
            existing = next((entry for entry in observed if entry["chain"] == chain), None)
            if existing:
                existing["count"] += count
            else:
                observed.append({"chain": chain, "count": count})
    for send in sends:
        if send.get("observedCallers"):
            send["observedCallers"].sort(key=lambda entry: -entry["count"])
            send["trigger"] = sorted(set(send.get("trigger", [])) | {"observed"})
    return dict(unmatched)


def read_slot_callers(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as file:
        payload = json.load(file)
    return {(int(slot["address"], 16), int(slot["offset"], 16)): [int(caller["function"], 16) for caller in slot["callers"]] for slot in payload["slots"]}


def table_start(name):
    return int(name[len("table_"):], 16) if name.startswith("table_") else idc.get_name_ea_simple(name)


def attach_static_callers(sends, slot_callers, source_files):
    for send in sends:
        if "evidence" not in send:
            continue
        found = set()
        for table in send["evidence"]["tables"]:
            name, offset = table.split("+")
            found |= set(slot_callers.get((table_start(name), int(offset, 16)), []))
        if found:
            send["staticCallers"] = sorted(frame_text(caller, source_files) for caller in found)
            send["trigger"] = sorted(set(send["trigger"]) | {"static-caller"})


def read_receive_handlers(path):
    handlers = collections.defaultdict(set)
    with open(path, newline="") as file:
        for row in csv.DictReader(file):
            handlers[int(row["handler"], 16)].add("%s:%s" % (row["channel"], row["id"]))
    return handlers


def export(send_ids_path=None, receive_handlers_path=None, output_path=None, send_stacks_path=None):
    ida_hexrays.init_hexrays_plugin()
    module = gw2re_ida.database().module
    send_ids_path = send_ids_path or gw2re_ida.artifact_path(module + ".send-ids.csv")
    receive_handlers_path = receive_handlers_path or gw2re_ida.artifact_path(module + ".receive-handlers.csv")
    output_path = output_path or gw2re_ida.default_output_path(".sender-triggers.json")
    send_stacks_path = send_stacks_path or gw2re_ida.artifact_path(module + ".send-stacks.csv")
    slot_callers_path = gw2re_ida.artifact_path(module + ".slot-callers.json")
    send_ids = read_send_ids(send_ids_path)
    receive_handlers = read_receive_handlers(receive_handlers_path)
    send_function = idc.get_name_ea_simple(SEND_FUNCTION)
    sink_forwarder = agent_sink_forwarder()
    sends = [send for send in direct_sends(send_function) if send["address"] != sink_forwarder] + agent_command_sends()
    if not any(send.get("name") == AGENT_WORLD_QUEUE and send.get("messageId") == AGENT_WORLD_REPORT_ID for send in sends):
        sends += agent_world_report()
    source_files = SourceFiles()
    for send in sends:
        if "name" not in send:
            continue
        send["sourceFile"] = source_files.of(int(send["address"], 16))
        send["callerFiles"] = sorted({((source_files.of(caller) or {}).get("file") or "").rsplit("\\", 1)[-1] for caller in callers_of(int(send["address"], 16))} - {""})
        evidence = ancestry(int(send["address"], 16), receive_handlers)
        evidence["tableOwners"] = table_owners(evidence["tables"], source_files)
        send["evidence"] = evidence
        send["trigger"] = trigger_kinds(send, evidence)
        send["channel"] = channel_of(send, send_ids) if send["messageId"] is not None else None
        send["parameterArguments"] = parameter_arguments(send)
        send["matchKey"] = match_key(send, send_ids)
    unmatched_stacks = attach_observed_callers(sends, read_send_stacks(send_stacks_path), source_files)
    attach_static_callers(sends, read_slot_callers(slot_callers_path), source_files)
    covered = {(send.get("channel"), send.get("messageId")) for send in sends}
    unsent = sorted("%s:%x" % key for key in send_ids if key not in covered)
    payload = {
        "schemaVersion": 1,
        "kind": "sender-trigger-export",
        "producedBy": "tools/ida/export_sender_triggers.py",
        "image": gw2re_ida.image_identity(),
        "sendCount": len(sends),
        "registeredSendIds": len(send_ids),
        "registeredIdsWithoutSender": unsent,
        "observedStacksWithoutSender": unmatched_stacks,
        "sends": sends,
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(output_path, "sender-trigger-export", sendCount=len(sends), registeredIdsWithoutSender=len(unsent))
    print("[export_sender_triggers] wrote %s" % summary["artifact"], flush=True)
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(3)[:3])
