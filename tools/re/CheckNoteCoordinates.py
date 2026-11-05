import bisect
import glob
import json
import re
import sys
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[2]
NOTES_PATTERN = "gw2-research/docs/**/*.md"
SYMBOLS_PATTERN = "gw2-research/research/*/symbols.json"
FUNCTION_CITATION = re.compile(r"\b(?:sub|loc|nullsub|j_sub|FUN|LAB|thunk_FUN)_([0-9A-Fa-f]{6,16})\b(?:\+0x([0-9A-Fa-f]+))?")
DATA_CITATION = re.compile(r"\b(?:dword|qword|word|byte|unk|off|stru|xmmword|flt|dbl|DAT|_DAT|PTR_FUN|PTR_DAT|PTR_LAB|PTR)_([0-9A-Fa-f]{6,16})\b")
ADDRESS_CITATION = re.compile(r"(?<![\w+])(?:0x)?(14[0-9A-Fa-f]{7}|7[Ff]{2}[0-9A-Fa-f]{9})(?![\w])")
DEFAULT_NAME = re.compile(r"^(?:sub|loc|nullsub|j_sub|unk|off|dword|qword|byte|word)_[0-9A-Fa-f]+$")


class Symbols:
    def __init__(self, path):
        symbols = json.load(open(path, encoding="utf-8"))
        self.build = str(symbols["image"]["build"])
        self.starts = sorted(int(address, 16) for address in symbols["functions"])
        self.start_set = set(self.starts)
        self.named = {int(address, 16) for address in symbols["names"].values()}
        self.chosen_names = {int(address, 16): name for name, address in symbols["names"].items() if not DEFAULT_NAME.match(name)}
        self.minimum = int(symbols["image"]["minAddress"], 16)
        self.maximum = int(symbols["image"]["maxAddress"], 16)

    def inside_function(self, address):
        index = bisect.bisect_right(self.starts, address) - 1
        return index >= 0 and index + 1 < len(self.starts) and address < self.starts[index + 1]


def reference_symbols():
    paths = glob.glob(str(REPOSITORY / SYMBOLS_PATTERN))
    if len(paths) != 1:
        sys.exit(f"{SYMBOLS_PATTERN}: expected the reference image's symbols only, found {len(paths)}")
    return Symbols(paths[0])


def function_problem(symbols, start, offset):
    if start not in symbols.start_set:
        return "is not a function start"
    if offset is not None and not symbols.inside_function(start + offset):
        return "points past the function"
    if start in symbols.chosen_names:
        return "is named; cite it as %s" % symbols.chosen_names[start]
    return None


def address_problem(symbols, address):
    if address == symbols.minimum:
        return None
    if not symbols.minimum <= address < symbols.maximum:
        return "is outside the image"
    if address in symbols.start_set:
        return "is a function start; cite it as sub_%X" % address
    if address in symbols.named:
        return "is named; cite the name"
    if symbols.inside_function(address):
        return "is inside a function; cite it as sub_<start>+0x<offset>"
    return "is unnamed data; name it in the database and cite the name"


def line_problems(symbols, line):
    covered = []
    for match in FUNCTION_CITATION.finditer(line):
        covered.append(match.span())
        offset = int(match.group(2), 16) if match.group(2) else None
        problem = function_problem(symbols, int(match.group(1), 16), offset)
        if problem:
            yield match.group(0), problem
    for match in DATA_CITATION.finditer(line):
        covered.append(match.span())
        problem = address_problem(symbols, int(match.group(1), 16))
        if problem:
            yield match.group(0), problem
    for match in ADDRESS_CITATION.finditer(line):
        if any(low <= match.start() < high for low, high in covered):
            continue
        problem = address_problem(symbols, int(match.group(1), 16))
        if problem:
            yield match.group(0), problem


def main():
    symbols = reference_symbols()
    problems = []
    for path in sorted(glob.glob(str(REPOSITORY / NOTES_PATTERN), recursive=True)):
        relative = Path(path).relative_to(REPOSITORY).as_posix()
        for number, line in enumerate(open(path, encoding="utf-8"), 1):
            for citation, problem in line_problems(symbols, line):
                problems.append(f"{relative}:{number}: {citation} {problem} in build {symbols.build}")
    print("\n".join(problems) if problems else "ok")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
