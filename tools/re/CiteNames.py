import glob
import json
import re
import subprocess
import sys
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[2]
SYMBOLS_PATTERN = "gw2-research/research/*/symbols.json"
CITING_FILES = ("*.md", "*.cs")
SKIPPED_DIRECTORIES = ("gw2-research/research/",)
FUNCTION_CITATION = re.compile(r"\bsub_([0-9A-Fa-f]{6,16})\b")
DEFAULT_NAME = re.compile(r"^(?:sub|loc|nullsub|j_sub|unk|off|dword|qword|byte|word)_[0-9A-Fa-f]+$")


def chosen_names():
    paths = glob.glob(str(REPOSITORY / SYMBOLS_PATTERN))
    if len(paths) != 1:
        sys.exit(f"{SYMBOLS_PATTERN}: expected the reference image's symbols only, found {len(paths)}")
    names = json.load(open(paths[0], encoding="utf-8"))["names"]
    return {int(address, 16): name for name, address in names.items() if not DEFAULT_NAME.match(name)}


def citing_paths():
    listed = subprocess.run(["git", "ls-files", "-z", "--", *CITING_FILES], cwd=REPOSITORY,
                            capture_output=True, check=True).stdout.decode("utf-8").split("\0")
    return [path for path in listed if path and not path.startswith(SKIPPED_DIRECTORIES)]


def cite_names(text, names):
    def replacement(match):
        return names.get(int(match.group(1), 16), match.group(0))
    return FUNCTION_CITATION.sub(replacement, text)


def main():
    names = chosen_names()
    changed = 0
    for path in citing_paths():
        file = REPOSITORY / path
        text = file.read_bytes().decode("utf-8")
        cited = cite_names(text, names)
        if cited != text:
            file.write_bytes(cited.encode("utf-8"))
            changed += 1
    print(f"cited names in {changed} files")


if __name__ == "__main__":
    main()
