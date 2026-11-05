#!/usr/bin/env python3

import argparse
import bisect
import collections
import difflib
import hashlib
import json
import os
import struct
import sys

from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP

REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
RELATIVE_BRANCH_GROUPS = {"call", "jump", "branch_relative"}
MAX_FUNCTION_SIZE = 0x20000
ORDER_SIZE_TOLERANCE = 0.25
ORDER_REFERENCE_SIMILARITY = 0.6
DATA_POINTER_WINDOW = 0x100

class PE:
    def __init__(self, path):
        with open(path, "rb") as f:
            self.blob = f.read()
        dos = struct.unpack_from("<I", self.blob, 0x3C)[0]
        assert self.blob[dos:dos + 4] == b"PE\x00\x00", "not a PE"
        nsec = struct.unpack_from("<H", self.blob, dos + 6)[0]
        opt_size = struct.unpack_from("<H", self.blob, dos + 20)[0]
        opt = dos + 24
        magic = struct.unpack_from("<H", self.blob, opt)[0]
        self.image_base = (struct.unpack_from("<I", self.blob, opt + 28)[0]
                           if magic == 0x10B
                           else struct.unpack_from("<Q", self.blob, opt + 24)[0])
        sec_base = opt + opt_size
        self.sections = []
        for i in range(nsec):
            off = sec_base + i * 40
            name = self.blob[off:off + 8].rstrip(b"\x00").decode("latin1")
            vsize, vaddr, rawsize, rawptr = struct.unpack_from(
                "<IIII", self.blob, off + 8)
            self.sections.append((name, vaddr, vsize, rawptr, rawsize))

    def _rva_to_off(self, va):
        rva = va - self.image_base
        for name, vaddr, vsize, rawptr, rawsize in self.sections:
            if vaddr <= rva < vaddr + max(vsize, rawsize):
                return rawptr + (rva - vaddr), name
        return None, None

    def read_va(self, va, size):
        off, _ = self._rva_to_off(va)
        if off is None:
            raise KeyError(f"VA 0x{va:x} not mapped")
        return self.blob[off:off + size]


disassembler = Cs(CS_ARCH_X86, CS_MODE_64)
disassembler.detail = True
disassembler.skipdata = True


class Build:
    def __init__(self, symbols_path):
        with open(symbols_path, encoding="utf-8") as source:
            self.symbols = json.load(source)
        self.pe = PE(os.path.join(REPOSITORY_ROOT, self.symbols["image"]["executablePath"]))
        self.starts = sorted(int(address, 16) for address in self.symbols["functions"])
        self.start_set = set(self.starts)
        self.names = {int(address, 16): name for name, address in self.symbols["names"].items()}
        self.functions = {}

    def function_end(self, start):
        index = bisect.bisect_right(self.starts, start)
        end = self.starts[index] if index < len(self.starts) else start + MAX_FUNCTION_SIZE
        return min(end, start + MAX_FUNCTION_SIZE)

    def containing_start(self, address):
        index = bisect.bisect_right(self.starts, address) - 1
        return self.starts[index] if index >= 0 else None

    def shape(self, start):
        if start not in self.functions:
            self.functions[start] = function_shape(self.pe, start, self.function_end(start))
        return self.functions[start]


def function_shape(pe, start, end):
    code = pe.read_va(start, end - start)
    masked = bytearray(code)
    references = []
    for insn in disassembler.disasm(code, start):
        offset = insn.address - start
        if insn.id == 0:
            continue
        for operand in insn.operands:
            if operand.type == X86_OP_MEM and operand.mem.base == X86_REG_RIP:
                for i in range(insn.disp_offset, insn.disp_offset + insn.disp_size):
                    masked[offset + i] = 0
                references.append((offset, "data", insn.address + insn.size + operand.mem.disp))
            elif operand.type == X86_OP_IMM and any(insn.group_name(g) in RELATIVE_BRANCH_GROUPS for g in insn.groups):
                for i in range(insn.imm_offset, insn.imm_offset + insn.imm_size):
                    masked[offset + i] = 0
                target = operand.imm
                if not start <= target < end:
                    references.append((offset, "code", target))
    digest = hashlib.sha1(bytes(masked)).hexdigest()
    return digest, references


class Port:
    def __init__(self, reference, target):
        self.reference = reference
        self.target = target
        self.functions = {}
        self.function_methods = {}
        self.data_votes = collections.defaultdict(collections.Counter)

    def match_unique_shapes(self):
        reference_by_shape = collections.defaultdict(list)
        for start in self.reference.starts:
            reference_by_shape[self.reference.shape(start)[0]].append(start)
        target_by_shape = collections.defaultdict(list)
        for start in self.target.starts:
            target_by_shape[self.target.shape(start)[0]].append(start)
        for digest, starts in reference_by_shape.items():
            matches = target_by_shape.get(digest, [])
            if len(starts) == 1 and len(matches) == 1:
                self.functions[starts[0]] = matches[0]
                self.function_methods[starts[0]] = "shape"

    def follow_references(self):
        pending = list(self.functions.items())
        while pending:
            reference_start, target_start = pending.pop()
            reference_references = self.reference.shape(reference_start)[1]
            target_references = self.target.shape(target_start)[1]
            if [(offset, kind) for offset, kind, _ in reference_references] != \
                    [(offset, kind) for offset, kind, _ in target_references]:
                continue
            for (_, kind, reference_address), (_, _, target_address) in zip(reference_references, target_references):
                if kind == "data":
                    self.data_votes[reference_address][target_address] += 1
                elif reference_address in self.reference.start_set and target_address in self.target.start_set \
                        and reference_address not in self.functions:
                    self.functions[reference_address] = target_address
                    self.function_methods[reference_address] = "call"
                    pending.append((reference_address, target_address))

    def reference_key(self, kind, address, known):
        return (kind, address) if address in known else (kind, None)

    def follow_aligned_references(self):
        added = 0
        known_functions = set(self.functions.values())
        for reference_start, target_start in list(self.functions.items()):
            reference_references = self.reference.shape(reference_start)[1]
            target_references = self.target.shape(target_start)[1]
            if [(offset, kind) for offset, kind, _ in reference_references] == \
                    [(offset, kind) for offset, kind, _ in target_references]:
                continue
            reference_keys = [(kind, self.functions[address]) if address in self.functions else (kind, None)
                              for _, kind, address in reference_references]
            target_keys = [self.reference_key(kind, address, known_functions) for _, kind, address in target_references]
            matcher = difflib.SequenceMatcher(None, reference_keys, target_keys, autojunk=False)
            for tag, reference_low, reference_high, target_low, target_high in matcher.get_opcodes():
                if tag != "equal":
                    continue
                for offset in range(reference_high - reference_low):
                    _, kind, reference_address = reference_references[reference_low + offset]
                    _, _, target_address = target_references[target_low + offset]
                    if kind == "data":
                        self.data_votes[reference_address][target_address] += 1
                    elif reference_address in self.reference.start_set and target_address in self.target.start_set \
                            and reference_address not in self.functions:
                        self.functions[reference_address] = target_address
                        self.function_methods[reference_address] = "aligned"
                        added += 1
        return added

    def alike(self, reference_start, target_start):
        reference_size = self.reference.function_end(reference_start) - reference_start
        target_size = self.target.function_end(target_start) - target_start
        if abs(reference_size - target_size) > max(0x10, ORDER_SIZE_TOLERANCE * reference_size):
            return False
        reference_kinds = [kind for _, kind, _ in self.reference.shape(reference_start)[1]]
        target_kinds = [kind for _, kind, _ in self.target.shape(target_start)[1]]
        if not reference_kinds and not target_kinds:
            return True
        return difflib.SequenceMatcher(None, reference_kinds, target_kinds, autojunk=False).ratio() >= ORDER_REFERENCE_SIMILARITY

    def match_ordered_gaps(self):
        added = 0
        target_index = {start: index for index, start in enumerate(self.target.starts)}
        anchors = [(index, target_index[self.functions[start]]) for index, start in enumerate(self.reference.starts)
                   if start in self.functions]
        matched_targets = set(self.functions.values())
        for (reference_low, target_low), (reference_high, target_high) in zip(anchors, anchors[1:]):
            if target_high <= target_low:
                continue
            reference_gap = self.reference.starts[reference_low + 1:reference_high]
            target_gap = [start for start in self.target.starts[target_low + 1:target_high] if start not in matched_targets]
            if not reference_gap or len(reference_gap) != len(target_gap):
                continue
            for reference_start, target_start in zip(reference_gap, target_gap):
                if not self.alike(reference_start, target_start):
                    continue
                self.functions[reference_start] = target_start
                self.function_methods[reference_start] = "order"
                matched_targets.add(target_start)
                added += 1
        return added

    def image_pointer(self, build, address):
        try:
            value, = struct.unpack("<Q", build.pe.read_va(address, 8))
        except Exception:
            return None
        minimum = int(build.symbols["image"]["minAddress"], 16)
        maximum = int(build.symbols["image"]["maxAddress"], 16)
        return value if minimum <= value < maximum else None

    def follow_data_pointers(self):
        added = 0
        ported = {reference: votes.most_common(1)[0][0] for reference, votes in self.data_votes.items() if len(votes) == 1}
        for reference_address, target_address in ported.items():
            for offset in range(0, DATA_POINTER_WINDOW, 8):
                reference_pointer = self.image_pointer(self.reference, reference_address + offset)
                target_pointer = self.image_pointer(self.target, target_address + offset)
                if reference_pointer is None or target_pointer is None:
                    break
                if reference_pointer in self.reference.start_set:
                    if self.functions.get(reference_pointer) != target_pointer:
                        break
                    continue
                known = ported.get(reference_pointer)
                if known is not None and known != target_pointer:
                    break
                if reference_pointer not in self.data_votes:
                    added += 1
                self.data_votes[reference_pointer][target_pointer] += 1
        return added

    def data(self):
        ported, conflicts = {}, {}
        for reference_address, votes in self.data_votes.items():
            (target_address, count), = votes.most_common(1)
            if len(votes) == 1:
                ported[reference_address] = target_address
            else:
                conflicts[reference_address] = {"%x" % address: count for address, count in votes.items()}
        return ported, conflicts

    def site(self, reference_address):
        start = self.reference.containing_start(reference_address)
        if start in self.functions:
            return self.functions[start] + reference_address - start
        return None

    def run(self):
        self.match_unique_shapes()
        self.follow_references()
        while self.follow_aligned_references() + self.match_ordered_gaps():
            self.follow_references()
        while self.follow_data_pointers():
            pass
        data, conflicts = self.data()
        unique_targets = collections.Counter(self.functions.values())
        functions = {"%x" % reference: "%x" % target for reference, target in sorted(self.functions.items())
                     if unique_targets[target] == 1}
        return {
            "reference": self.reference.symbols["image"]["build"],
            "target": self.target.symbols["image"]["build"],
            "functions": functions,
            "functionMethods": dict(collections.Counter(self.function_methods[int(k, 16)] for k in functions)),
            "data": {"%x" % reference: "%x" % target for reference, target in sorted(data.items())},
            "dataConflicts": {"%x" % reference: votes for reference, votes in sorted(conflicts.items())},
        }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", required=True, help="symbols.json of the build the names come from")
    parser.add_argument("--target", required=True, help="symbols.json of the build they move to")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    result = Port(Build(args.reference), Build(args.target)).run()
    with open(args.output, "w", encoding="utf-8") as output:
        json.dump(result, output, indent=1)
    print("%d functions (%s), %d data, %d data conflicts" % (
        len(result["functions"]), result["functionMethods"], len(result["data"]), len(result["dataConflicts"])))


if __name__ == "__main__":
    main()
