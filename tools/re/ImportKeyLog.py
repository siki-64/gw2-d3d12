import json
import re
import sys
from pathlib import Path

KEY_LINE = re.compile(r"KEY\t(\w+)\t([0-9A-Fa-f]+)\t([0-9A-Fa-f]+)")
ASSET_KEY_LINE = re.compile(r"ASSETKEY ([0-9A-Fa-f]+) ([0-9A-Fa-f]+)")
TEXT_KEY_HIGH_BITS = 0xFFFFE00000000000
ASSET_KINDS = {1: "audio"}


def asset_kind(asset_type):
    return ASSET_KINDS.get(asset_type, f"asset{asset_type}")


def read_entry(line):
    line = line.replace("\\t", "\t")
    match = KEY_LINE.search(line)
    if match:
        kind = match.group(1)
        if kind.startswith("asset") and kind[5:].isdigit():
            kind = asset_kind(int(kind[5:]))
        return kind, int(match.group(2), 16), int(match.group(3), 16)
    match = ASSET_KEY_LINE.search(line)
    if match:
        type_and_id = int(match.group(1), 16)
        return asset_kind(type_and_id & 0xFFFFFFFF), type_and_id >> 32, int(match.group(2), 16)
    return None


def parse_key_log(lines):
    keys = {}
    conflicts = []
    rejected = []
    for line in lines:
        entry = read_entry(line)
        if entry is None:
            continue
        kind, entry_id, key = entry
        if key == 0:
            continue
        if key > 0xFFFFFFFFFFFFFFFF or (kind == "text" and key & TEXT_KEY_HIGH_BITS):
            rejected.append(line.strip())
            continue
        known = keys.setdefault(kind, {})
        if entry_id in known and known[entry_id] != key:
            conflicts.append({"kind": kind, "id": entry_id, "keys": [known[entry_id], key]})
            continue
        known[entry_id] = key
    return keys, conflicts, rejected


def to_document(keys, conflicts):
    return {
        "keys": {
            kind: {str(entry_id): f"{key:016X}" for entry_id, key in sorted(entries.items())}
            for kind, entries in sorted(keys.items())
        },
        "conflicts": [
            {"kind": c["kind"], "id": c["id"], "keys": [f"{k:016X}" for k in c["keys"]]} for c in conflicts
        ],
    }


def merge(existing, keys):
    for kind, entries in existing.get("keys", {}).items():
        target = keys.setdefault(kind, {})
        for entry_id, key in entries.items():
            target.setdefault(int(entry_id), int(key, 16))
    return keys


def main(argv):
    if len(argv) != 2:
        print("usage: ImportKeyLog.py <x64dbg-log>")
        return 2
    log_path = Path(argv[1])
    output = Path("gw2-research/src/Gw2.Exe.Routines/keys.json")
    keys, conflicts, rejected = parse_key_log(log_path.read_text(encoding="utf-8", errors="replace").splitlines())
    if output.exists():
        keys = merge(json.loads(output.read_text(encoding="utf-8")), keys)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(to_document(keys, conflicts), indent=2), encoding="utf-8")
    total = sum(len(entries) for entries in keys.values())
    print(f"{total} keys, {len(conflicts)} conflicts, {len(rejected)} rejected -> {output}")
    return 1 if conflicts else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
