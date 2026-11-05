import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ida_domain.operands import OperandType

import gw2re_ida

ASSERT_HELPERS = {
    "AssertMain": "main",
    "AssertFatal": "fatal",
    "AssertVerify": "verify",
    "AssertRareA": "rare_a",
    "AssertRareB": "rare_b",
    "AssertRareC": "rare_c",
    "AssertRareD": "rare_d",
}
SCAN_BACK_INSN = 10
STRING_MAX = 512
EXPRESSION_REGISTERS = ("rcx", "ecx")
FILE_REGISTERS = ("rdx", "edx")
LINE_REGISTERS = ("r8d", "r8")


def is_printable_ascii(text):
    if not text:
        return False
    return all(0x20 <= ord(character) < 0x7F for character in text)


def read_wide_string(address):
    data = gw2re_ida.bytes_at(address, STRING_MAX * 2)
    if not data:
        return None
    end = len(data)
    for index in range(0, len(data) - 1, 2):
        if data[index] == 0 and data[index + 1] == 0:
            end = index
            break
    if end == 0:
        return None
    text = data[:end].decode("utf-16-le", "replace")
    return text if is_printable_ascii(text) else None


def read_ascii_string(address):
    try:
        text = gw2re_ida.database().bytes.get_cstring_at(address, STRING_MAX)
    except Exception:
        return None
    if not text:
        return None
    return text if is_printable_ascii(text) else None


def find_assert_args(call_address):
    db = gw2re_ida.database()
    expression = None
    file_name = None
    line = None
    address = call_address

    for _ in range(SCAN_BACK_INSN):
        instruction = db.instructions.get_previous(address)
        if instruction is None:
            break
        address = instruction.ea
        mnemonic = db.instructions.get_mnemonic(instruction)
        operands = db.instructions.get_operands(instruction)

        if expression is not None and file_name is not None:
            break

        if mnemonic == "lea" and len(operands) >= 2:
            destination = gw2re_ida.register_name(operands[0])
            if destination in EXPRESSION_REGISTERS + FILE_REGISTERS:
                target = gw2re_ida.memory_target(instruction, operands[1])
                if target is None:
                    continue
                text = read_ascii_string(target) or read_wide_string(target)
                if not text:
                    continue
                if destination in EXPRESSION_REGISTERS:
                    expression = text
                else:
                    file_name = text
            continue

        if mnemonic == "mov" and len(operands) >= 2:
            if gw2re_ida.register_name(operands[0]) in LINE_REGISTERS:
                if operands[1].type == OperandType.IMMEDIATE:
                    line = operands[1].get_value()
                break
            continue

        if mnemonic in ("call", "jmp"):
            break

    return expression, file_name, line


def export(output_path=None):
    db = gw2re_ida.database()
    output_path = output_path or gw2re_ida.default_output_path(".assert-sites.json")
    rows = []
    dropped_count = 0

    for symbol, helper_name in ASSERT_HELPERS.items():
        helper_address = gw2re_ida.name_address(symbol)
        if helper_address is None:
            raise RuntimeError("the reference database names no %s" % symbol)
        references = list(db.xrefs.code_refs_to_ea(helper_address))
        print("[export_asserts] helper %x (%s): %d xrefs" % (helper_address, helper_name, len(references)), flush=True)
        for call_address in references:
            function = gw2re_ida.containing_function(call_address)
            if function is None:
                continue
            expression, file_name, line = find_assert_args(call_address)
            if not expression:
                dropped_count += 1
                continue
            rows.append({
                "address": "%x" % call_address,
                "function": "sub_%X" % function.start_ea,
                "helper": helper_name,
                "file": file_name,
                "line": line,
                "expression": expression,
            })

    print("[export_asserts] dropped (no expression): %d" % dropped_count, flush=True)

    file_counts = {}
    expression_counts = {}
    for row in rows:
        if row["file"]:
            file_counts[row["file"]] = file_counts.get(row["file"], 0) + 1
        if row["expression"]:
            expression_counts[row["expression"]] = expression_counts.get(row["expression"], 0) + 1

    payload = {
        "schemaVersion": 2,
        "kind": "assert-site-export",
        "producedBy": "tools/ida/export_asserts.py",
        "image": gw2re_ida.image_identity(),
        "siteCount": len(rows),
        "functionCount": len({row["function"] for row in rows}),
        "fileCount": len(file_counts),
        "fileCounts": file_counts,
        "expressionCounts": expression_counts,
        "rows": rows,
    }

    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(
        output_path, "assert-site-export",
        siteCount=len(rows), functionCount=payload["functionCount"], fileCount=payload["fileCount"])

    print("[export_asserts] wrote %s" % summary["artifact"], flush=True)
    print("[export_asserts] sites: %d, functions: %d, files: %d"
          % (summary["siteCount"], summary["functionCount"], summary["fileCount"]), flush=True)
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
