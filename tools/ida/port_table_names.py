import difflib
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ida_name

import gw2re_ida
import verify_layouts as verify

TABLE_NAME_SUFFIXES = (verify.VIRTUAL_TABLE_SUFFIX, verify.VIRTUAL_BASE_TABLE_SUFFIX)


def table_key(address, kind):
    if kind == "vftable":
        return ["vftable", verify.slot_count(address)]
    return ["vbtable"] + verify.virtual_base_entries(address)


def table_sequence():
    tables = verify.referenced_tables()
    return [{"address": gw2re_ida.address_text(address), "key": table_key(address, tables[address]),
             "name": ida_name.get_name(address)} for address in sorted(tables)]


def write_sequence(output_path):
    with open(output_path, "w", encoding="utf-8") as output:
        json.dump(table_sequence(), output)


def aligned_rows(reference, current):
    matcher = difflib.SequenceMatcher(None, [tuple(row["key"]) for row in reference],
                                      [tuple(row["key"]) for row in current], autojunk=False)
    mapping = {}
    for tag, reference_start, reference_end, current_start, current_end in matcher.get_opcodes():
        if tag == "equal" or (tag == "replace" and reference_end - reference_start == current_end - current_start):
            for offset in range(reference_end - reference_start):
                mapping[reference_start + offset] = current_start + offset
    return mapping


def ported_names(reference_path):
    with open(reference_path, encoding="utf-8") as source:
        reference = json.load(source)
    current = table_sequence()
    mapping = aligned_rows(reference, current)
    ported, unplaced = {}, []
    for index, row in enumerate(reference):
        if not row["name"].endswith(TABLE_NAME_SUFFIXES):
            continue
        if index in mapping:
            ported[row["name"]] = int(current[mapping[index]]["address"], 16)
        else:
            unplaced.append(row["name"])
    return ported, unplaced


def port(reference_path):
    ported, unplaced = ported_names(reference_path)
    refused = [name for name, address in sorted(ported.items())
               if ida_name.get_name(address) != name
               and not ida_name.set_name(address, name, ida_name.SN_CHECK | ida_name.SN_NOWARN)]
    problems = ["%s: no table at its place in this build" % name for name in unplaced]
    problems += ["%s: the database refused the name" % name for name in refused]
    return problems + verify.verify()


if __name__ == "__main__":
    mode, path = gw2re_ida.script_arguments(2)[:2]
    if mode == "sequence":
        write_sequence(path)
    else:
        print("\n".join(port(path)) or "ok")
