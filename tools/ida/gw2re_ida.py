import json
import os
import re

import ida_domain
from ida_domain import Database
from ida_domain.functions import FunctionFlags
from ida_domain.operands import OperandType
from ida_domain.segments import SegmentPermissions

_database = None
_thunk_targets = None
_function_names = {}
_name_addresses = None
_rtti_names = {}
IMAGE_SPAN = 0x4000000
RIP_REGISTER = 28
MEMORY_OPERAND_TYPES = (OperandType.MEMORY, OperandType.DISPLACEMENT, OperandType.PHRASE)
AUTO_NAME = re.compile(r"^(?:sub|loc|unk|byte|word|dword|qword|flt|dbl|off|asc|str)_([0-9A-Fa-f]+)$")
BUILD_BANNER = b"Build: %u"
BUILD_BANNER_ARGUMENT = re.compile(r'"Build: %u[^"]*",\s*([^)]+)\)')
BUILD_FILE_LABEL = re.compile(r"\d{5,}")
GLOBAL_NAME = re.compile(r"^(?:dword|qword|word|byte|flt|dbl)_[0-9A-F]+$")
CALL_EXPRESSION = re.compile(r"^([A-Za-z_$][A-Za-z_0-9$]*)\(\)$")
PARAMETER_NAME = re.compile(r"^a(\d+)$")
TYPE_CAST = re.compile(r"^\(\s*[A-Za-z_][A-Za-z_0-9 ]*\*?\s*\)\s*")
RETURN_STATEMENT = re.compile(r"^\s*return ([^;]+);", re.M)
ASSIGNMENT = r"\b%s = ([^;]+);"
BUILD_TRACE_DEPTH = 8
CALL_REGISTERS = ("rcx", "rdx", "r8", "r9")
ARGUMENT_REGISTERS = {"rcx": 1, "rdx": 2, "r8": 3, "r9": 4}
TOOL_DIRECTORY = os.path.dirname(os.path.abspath(__file__))
REPOSITORY_ROOT = os.path.abspath(os.path.join(TOOL_DIRECTORY, "..", ".."))
ARTIFACT_DIRECTORY = os.path.join(REPOSITORY_ROOT, "artifacts", "ida-exports")
BUILDS_DIRECTORY = os.path.join(REPOSITORY_ROOT, "research")


def database():
    global _database
    if _database is None:
        _database = Database.open()
    return _database


def script_arguments(default_count=0):
    import idc
    arguments = list(idc.ARGV[1:]) if idc.ARGV else []
    return arguments + [None] * max(0, default_count - len(arguments))


def artifact_path(name):
    os.makedirs(ARTIFACT_DIRECTORY, exist_ok=True)
    return os.path.join(ARTIFACT_DIRECTORY, name)


def build_path(build, name):
    directory = os.path.join(BUILDS_DIRECTORY, str(build))
    os.makedirs(directory, exist_ok=True)
    return os.path.join(directory, name)


def default_output_path(suffix):
    return artifact_path(database().module + suffix)


def filed_path(path):
    """A path as it is filed in the repository, so an artifact never carries a machine path."""
    relative = os.path.relpath(path, REPOSITORY_ROOT)
    return relative.replace(os.sep, "/") if not relative.startswith("..") else path


def image_identity():
    db = database()
    return {
        "build": build_label(),
        "programName": db.module,
        "executablePath": filed_path(db.path),
        "executableMd5": db.md5,
        "executableSha256": db.sha256,
        "processor": db.architecture,
        "imageBase": "%x" % db.base_address,
        "minAddress": "%x" % db.minimum_ea,
        "maxAddress": "%x" % db.maximum_ea,
        "idaVersion": ida_domain.ida_kernwin.get_kernel_version(),
    }


def literal_address(literal):
    occurrences = find_pattern(literal)
    return occurrences[0] if occurrences else None


def function_text(address):
    start = function_start(address)
    return decompiled(start) if start is not None else None


def strip_casts(expression):
    expression = expression.strip()
    while True:
        cast = TYPE_CAST.match(expression)
        if not cast:
            return expression
        expression = expression[cast.end():].strip()


def assigned_expression(text, name):
    assignment = re.search(ASSIGNMENT % re.escape(name), text)
    return assignment.group(1).strip() if assignment else None


def returned_expression(address):
    text = function_text(address)
    if text is None:
        return None, None
    returned = RETURN_STATEMENT.search(text)
    if not returned:
        return text, None
    return text, strip_casts(returned.group(1))


def stored_value(function_start_address, address, value):
    """The value a store to `address` writes: its argument when the store copies an argument register."""
    if value is None:
        return None
    argument = ARGUMENT_REGISTERS.get(store_register(function_start_address, address) or "")
    return "a%d" % argument if argument else value


def written_global(name):
    address = name_address(name)
    if address is None:
        return None
    for reference in database().xrefs.data_refs_to_ea(address):
        writer = containing_function(reference)
        if writer is None:
            continue
        text = decompiled(writer.start_ea)
        if text is None:
            continue
        value = stored_value(writer.start_ea, address, assigned_expression(text, name))
        if value is not None:
            return writer.start_ea, text, value
    return None


def calls_of(callee):
    name = function_name(callee) or ""
    for reference in database().xrefs.code_refs_to_ea(callee):
        text = function_text(reference)
        if text is None:
            continue
        call = re.search(r"\b%s\(([^;]*)\);" % re.escape(name), text)
        arguments = [argument.strip() for argument in call.group(1).split(",") if argument.strip()] if call else []
        if not arguments:
            arguments = call_arguments(reference)
        if arguments:
            yield text, arguments


def call_arguments(call_site):
    """The arguments at a call, read from the registers when the decompilation shows none."""
    arguments = []
    for register in CALL_REGISTERS:
        expression = register_expression(function_start(call_site), call_site, register)
        if expression is None:
            break
        arguments.append(expression)
    return arguments


def traced_build(expression, text, depth=0):
    if depth > BUILD_TRACE_DEPTH:
        raise RuntimeError("build trace is too deep at %s" % expression)
    expression = strip_casts(expression)
    if expression.isdigit():
        return int(expression)
    if GLOBAL_NAME.match(expression) or is_named_data(expression):
        writer = written_global(expression)
        if writer is None:
            raise RuntimeError("no writer for %s" % expression)
        start, writer_text, value = writer
        parameter = PARAMETER_NAME.match(value)
        if parameter:
            index = int(parameter.group(1))
            for caller_text, arguments in calls_of(start):
                if len(arguments) >= index:
                    return traced_build(arguments[index - 1], caller_text, depth + 1)
            raise RuntimeError("no caller passes %s" % value)
        return traced_build(value, writer_text, depth + 1)
    call = CALL_EXPRESSION.match(expression)
    if call:
        callee_text, returned = returned_expression(name_address(call.group(1)))
        if returned is None:
            raise RuntimeError("%s does not return a value" % expression)
        return traced_build(returned, callee_text, depth + 1)
    value = assigned_expression(text, expression)
    if value is not None:
        return traced_build(value, text, depth + 1)
    raise RuntimeError("cannot trace %s" % expression)


def printed_build():
    banner = literal_address(BUILD_BANNER)
    if banner is None:
        raise RuntimeError("no %r banner in the image" % BUILD_BANNER)
    for reference in database().xrefs.data_refs_to_ea(banner):
        text = function_text(reference)
        if text is None:
            continue
        argument = BUILD_BANNER_ARGUMENT.search(text)
        if argument:
            return traced_build(argument.group(1), text)
    raise RuntimeError("no %r banner in the image" % BUILD_BANNER)


def build_label():
    """The build number in the image name, which is how a capture is filed."""
    label = BUILD_FILE_LABEL.search(os.path.basename(database().path))
    return int(label.group(0)) if label else None


def image_build():
    """The build the image reports, checked against the name it is filed under."""
    reported = printed_build()
    label = build_label()
    if label is not None and label != reported:
        raise RuntimeError("image prints build %d but is named %d" % (reported, label))
    return str(reported)


def address_text(address):
    return "%x" % address


def normalize_address(value, delta=0):
    if isinstance(value, int):
        address = value
    else:
        text = str(value).strip()
        if text.startswith("sub_"):
            text = text[4:]
        if "_" in text:
            text = text.split("_", 1)[0]
        if not text:
            raise ValueError("empty address: %r" % (value,))
        address = int(text, 16)
    return address + delta


def try_normalize_address(value, delta=0):
    try:
        return normalize_address(value, delta)
    except Exception:
        return None


def name_at(address):
    return database().names.get_at(address)


def name_addresses():
    global _name_addresses
    if _name_addresses is None:
        _name_addresses = {name: address for address, name in database().names.get_all()}
    return _name_addresses


def is_named_data(name):
    address = name_addresses().get(name)
    return address is not None and containing_function(address) is None


def name_address(name):
    address = name_addresses().get(name)
    if address is not None:
        return address
    automatic = AUTO_NAME.match(name)
    return int(automatic.group(1), 16) if automatic else None


def bytes_at(address, size):
    try:
        return database().bytes.get_bytes_at(address, size)
    except Exception:
        return None


def is_initialized(address):
    try:
        return database().bytes.is_value_initialized_at(address)
    except Exception:
        return False


def qword_at(address):
    try:
        return database().bytes.get_qword_at(address)
    except Exception:
        return 0


def byte_at(address):
    try:
        return database().bytes.get_byte_at(address)
    except Exception:
        return None


def dword_at(address):
    try:
        return database().bytes.get_dword_at(address)
    except Exception:
        return 0


def find_binary_sequence(pattern):
    db = database()
    return db.bytes.find_binary_sequence(pattern, db.minimum_ea, db.maximum_ea)


_segment_bytes = None


def segment_bytes():
    global _segment_bytes
    if _segment_bytes is None:
        _segment_bytes = []
        for segment in database().segments.get_all():
            data = bytes_at(segment.start_ea, segment.end_ea - segment.start_ea)
            if data:
                _segment_bytes.append((segment.start_ea, data))
    return _segment_bytes


def find_pattern(expression):
    return find_regex(re.escape(expression))


def find_regex(expression):
    compiled = re.compile(b"(?=" + expression + b")", re.S)
    found = []
    for start, data in segment_bytes():
        found.extend(start + match.start() for match in compiled.finditer(data))
    return sorted(found)


def is_string_literal(address):
    return database().bytes.is_string_literal_at(address)


REGISTER_ALIASES = {
    "al": "rax", "ah": "rax", "ax": "rax", "eax": "rax",
    "bl": "rbx", "bh": "rbx", "bx": "rbx", "ebx": "rbx",
    "cl": "rcx", "ch": "rcx", "cx": "rcx", "ecx": "rcx",
    "dl": "rdx", "dh": "rdx", "dx": "rdx", "edx": "rdx",
    "sil": "rsi", "si": "rsi", "esi": "rsi",
    "dil": "rdi", "di": "rdi", "edi": "rdi",
    "bpl": "rbp", "bp": "rbp", "ebp": "rbp",
    "spl": "rsp", "sp": "rsp", "esp": "rsp",
}

POINTER_REGISTERS = frozenset(
    "rax rbx rcx rdx rsi rdi rbp rsp r8 r9 r10 r11 r12 r13 r14 r15".split()
)


def register_name(operand):
    try:
        return operand.get_register_name()
    except Exception:
        return None


def canonical_register(name):
    if not name:
        return None
    alias = REGISTER_ALIASES.get(name)
    if alias:
        return alias
    if len(name) in (3, 4) and name[0] == "r" and name[1:-1].isdigit() and name[-1] in "dwb":
        return name[:-1]
    return name


def store_register(function_start_address, address):
    """The register a store to `address` copies, read from the instructions."""
    db = database()
    function = db.functions.get_at(function_start_address)
    if function is None:
        return None
    for instruction in db.functions.get_instructions(function):
        operands = db.instructions.get_operands(instruction)
        if len(operands) < 2 or not is_memory_operand(operands[0]):
            continue
        if memory_target(instruction, operands[0]) != address:
            continue
        return canonical_register(register_name(operands[1]))
    return None


def written_register(instruction):
    db = database()
    if db.instructions.get_mnemonic(instruction) == "call":
        return "rax"
    operands = db.instructions.get_operands(instruction)
    if not operands:
        return None
    return canonical_register(register_name(operands[0]))


def instruction_expression(instruction, function_start_address):
    """What an instruction leaves behind: a call that returns it, an immediate, or a global."""
    db = database()
    if db.instructions.get_mnemonic(instruction) == "call":
        for target in db.instructions.call_targets(instruction):
            return "%s()" % (function_name(target) or name_at(target))
        return None
    operands = db.instructions.get_operands(instruction)
    if len(operands) < 2:
        return None
    source = operands[1]
    if source.type == OperandType.IMMEDIATE:
        return str(source.get_value())
    if is_memory_operand(source):
        return name_at(memory_target(instruction, source))
    register = canonical_register(register_name(source))
    if register is None:
        return None
    if register == written_register(instruction):
        return "0"
    return register_expression(function_start_address, instruction_address(instruction), register)


def register_expression(function_start_address, address, register):
    """The value `register` holds at `address`, read from the instructions."""
    db = database()
    instruction = db.instructions.get_previous(address)
    while instruction is not None:
        current = instruction_address(instruction)
        if current < function_start_address:
            return None
        if written_register(instruction) == register:
            return instruction_expression(instruction, function_start_address)
        instruction = db.instructions.get_previous(current)
    return None


def instruction_address(instruction):
    return getattr(instruction, "ea", instruction)


def is_memory_operand(operand):
    return operand.type in MEMORY_OPERAND_TYPES


def memory_target(instruction, operand):
    if operand.type not in (OperandType.MEMORY, OperandType.DISPLACEMENT):
        return None
    try:
        displacement = operand.get_displacement()
    except Exception:
        return None
    try:
        phrase = operand.get_phrase_number()
    except Exception:
        phrase = None
    if phrase == RIP_REGISTER:
        return instruction.ea + instruction.size + displacement
    if operand.type != OperandType.MEMORY:
        return None
    try:
        return operand.get_address()
    except Exception:
        return None


def memory_offset(text):
    start = text.find("[")
    end = text.find("]", start + 1) if start >= 0 else -1
    if start < 0 or end < 0:
        return 0
    inner = text[start + 1:end]
    if "+" not in inner:
        return 0
    displacement = inner.split("+")[-1].strip()
    try:
        if displacement.endswith("h"):
            return int(displacement[:-1], 16)
        return int(displacement, 0)
    except Exception:
        return 0


def function_start(address):
    function = containing_function(address)
    return function.start_ea if function is not None else None


def containing_function(address):
    if not address:
        return None
    try:
        return database().functions.get_at(address)
    except Exception:
        return None


_function_starts = None


def function_starts():
    global _function_starts
    if _function_starts is None:
        _function_starts = {function.start_ea for function in database().functions.get_all()}
    return _function_starts


def read_c_string(address, maximum_length=256):
    characters = []
    for offset in range(maximum_length):
        value = byte_at(address + offset)
        if value is None:
            return None
        if value == 0:
            break
        if value < 0x20 or value >= 0x7F:
            return None
        characters.append(chr(value))
    return "".join(characters) if characters else None


def data_segments():
    for segment in database().segments.get_all():
        if segment.perm & SegmentPermissions.EXEC:
            continue
        yield segment


def segment_name(segment):
    return database().segments.get_name(segment)


def write_json(path, payload):
    path = os.path.abspath(path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2)
        handle.write("\n")


def artifact_summary(path, kind, **counts):
    summary = {"artifact": os.path.abspath(path), "kind": kind}
    summary.update(counts)
    return summary


def function_name(address):
    function = containing_function(address)
    if function is None:
        return None
    try:
        return database().functions.get_name(function)
    except Exception:
        return None


def function_name_cached(address):
    if address not in _function_names:
        _function_names[address] = function_name(address)
    return _function_names[address]


def thunk_targets():
    global _thunk_targets
    if _thunk_targets is None:
        db = database()
        targets = {}
        for function in db.functions.get_all():
            if not db.functions.get_flags(function) & FunctionFlags.THUNK:
                continue
            instruction = db.instructions.get_at(function.start_ea)
            if instruction is None:
                continue
            jumps = list(db.instructions.jump_targets(instruction))
            if jumps:
                targets[function.start_ea] = jumps[0]
        _thunk_targets = targets
    return _thunk_targets


def thunk_target(address):
    return thunk_targets().get(address)


def resolved_function(address):
    db = database()
    function = containing_function(address)
    if function is None:
        return None
    if not db.functions.get_flags(function) & FunctionFlags.THUNK:
        return function
    target = thunk_target(function.start_ea)
    if target is None:
        return function
    resolved = containing_function(target)
    return resolved if resolved is not None else function


def called_functions(function):
    db = database()
    targets = set()
    for instruction in db.functions.get_instructions(function):
        for target in db.instructions.call_targets(instruction):
            callee = containing_function(target)
            if callee is not None and callee.start_ea != function.start_ea:
                targets.add(callee.start_ea)
    return sorted(targets)


def with_callees(addresses, depth_limit, seen=None):
    seen = set() if seen is None else seen
    collected = []
    frontier = list(addresses)
    for depth in range(depth_limit + 1):
        next_frontier = []
        for address in frontier:
            function = resolved_function(address)
            if function is None or function.start_ea in seen:
                continue
            seen.add(function.start_ea)
            collected.append((depth, function.start_ea))
            if depth < depth_limit:
                next_frontier.extend(called_functions(function))
        frontier = next_frontier
    return collected


def decompiled(address):
    try:
        result = database().pseudocode.decompile(address)
    except Exception:
        return None
    if result is None:
        return None
    try:
        return str(result.raw_cfunc)
    except Exception:
        return None


def referenced_strings(address):
    db = database()
    function = containing_function(address)
    if function is None:
        return []
    values = []
    for instruction in db.functions.get_instructions(function):
        for target in db.xrefs.data_refs_from_ea(instruction.ea):
            if not db.is_valid_ea(target, strict_check=False):
                continue
            try:
                if not db.bytes.is_string_literal_at(target):
                    continue
                item = db.strings.get_at(target)
            except Exception:
                continue
            if item is not None:
                values.append(item.contents.decode("utf-8", "replace"))
    return values


def write_decompiled_functions(output, functions_with_depth):
    for depth, address in functions_with_depth:
        output.write("\n// ----- %s %x depth %d\n" % (function_name(address), address, depth))
        for value in referenced_strings(address):
            output.write("// string %r\n" % value)
        text = decompiled(address)
        output.write(text + "\n" if text is not None else "// decompile failed\n")


def program_header():
    db = database()
    return "// program %s sha256 %s\n" % (db.module, db.sha256)


def read_rtti_name(vtable_ea):
    db = database()
    try:
        class_layout = db.bytes.get_qword_at(vtable_ea - 8)
    except Exception:
        return None
    image_base = db.base_address
    if not class_layout or not image_base <= class_layout < image_base + IMAGE_SPAN:
        return None
    try:
        signature = db.bytes.get_dword_at(class_layout)
        type_descriptor = db.bytes.get_dword_at(class_layout + 12)
        self_reference = db.bytes.get_dword_at(class_layout + 20)
    except Exception:
        return None
    if signature != 1 or self_reference != class_layout - image_base or type_descriptor == 0:
        return None
    name = db.bytes.get_cstring_at(image_base + type_descriptor + 16, 256)
    if not name or not name.startswith(".?AV"):
        return None
    return "".join(character for character in name if 0x20 <= ord(character) < 0x7F)


def read_rtti_name_cached(vtable_ea):
    if vtable_ea not in _rtti_names:
        _rtti_names[vtable_ea] = read_rtti_name(vtable_ea)
    return _rtti_names[vtable_ea]
