import re
import subprocess
import sys
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[2]
CHECKED_SUFFIXES = (".md", ".json", ".yml", ".py", ".cs", ".ps1")
REPOSITORY_ROOTS = ("src/", "docs/", "tools/", "tests/")
MARKDOWN_LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
REPOSITORY_PATH = re.compile(r"(?<![\w./-])((?:src|docs|tools|tests|server)/[\w./-]+)")
PLACEHOLDER_CHARACTERS = set("<>*%{}$")
PINNED_EVIDENCE = ("tests/fixtures/", "gw2-research/tests/fixtures/")
DOTTED_BUILD =re.compile(r"\b(20[0-9]\.[0-9]{3})\b")
INLINE_CODE = re.compile(r"`[^`\n]*`")


def tracked_files():
    output = subprocess.run(["git", "ls-files"], cwd=REPOSITORY, capture_output=True, text=True, check=True).stdout
    return [path for path in output.splitlines() if path.endswith(CHECKED_SUFFIXES) and "/Generated/" not in path]


def heading_anchor(heading):
    text = re.sub(r"[^\w\- ]", "", heading.strip().lower())
    return text.replace(" ", "-")


def anchors(path, cache):
    if path not in cache:
        found = set()
        counts = {}
        in_code = False
        for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
            if line.startswith("```"):
                in_code = not in_code
            if in_code or not line.startswith("#"):
                continue
            anchor = heading_anchor(line.lstrip("#"))
            count = counts.get(anchor, 0)
            counts[anchor] = count + 1
            found.add(anchor if count == 0 else f"{anchor}-{count}")
        cache[path] = found
    return cache[path]


def link_problems(source, text, anchor_cache):
    for target in MARKDOWN_LINK.findall(INLINE_CODE.sub("``", text)):
        if re.match(r"^[a-z]+:", target) or set(target) & PLACEHOLDER_CHARACTERS:
            continue
        file_part, _, anchor = target.partition("#")
        resolved = (source.parent / file_part).resolve() if file_part else source
        if not resolved.exists():
            yield f"link to missing {target}"
        elif anchor and resolved.suffix == ".md" and anchor.lower() not in anchors(resolved, anchor_cache):
            yield f"link to missing anchor {target}"


def is_branch_name(path):
    return path.count("/") == 1 and "." not in path


def path_problems(source, text):
    for path in REPOSITORY_PATH.findall(text):
        path = path.rstrip(".")
        if set(path) & PLACEHOLDER_CHARACTERS or is_branch_name(path):
            continue
        if not (REPOSITORY / path).exists() and not (source.parent / path).exists():
            yield f"path to missing {path}"


def dotted_build_problems(text):
    for build in DOTTED_BUILD.findall(text):
        yield f"build {build} written with a dot; write {build.replace('.', '')}"


def main():
    anchor_cache = {}
    problems = []
    for relative in tracked_files():
        source = REPOSITORY / relative
        if not source.exists():
            continue
        text = source.read_text(encoding="utf-8", errors="ignore")
        found = list(path_problems(source, text))
        if not relative.startswith(PINNED_EVIDENCE):
            found += dotted_build_problems(text)
        if source.suffix == ".md":
            found += link_problems(source, text, anchor_cache)
        problems += [f"{relative}: {problem}" for problem in sorted(set(found))]
    print("\n".join(problems) or "ok")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
