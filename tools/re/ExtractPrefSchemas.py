import argparse
import hashlib
import json
import re
import struct
import sys

from ExtractPackSchemas import PortableExecutable, collect_types, read_type

LOCATIONS = {0: "acct", 1: "char", 2: "locl"}
VERSION_ENTRY_SIZE = 24
REGISTRATION_PATTERN = re.compile(
    rb"\x41\xB9(?P<versions>.{4})\x4C\x8D\x05(?P<table>.{4})"
    rb"(?P<between>(?:\xBA.{4}|\xB9.{4}|\x44\x89\xC9|\x41\x8B\xC9|\x33\xC9|\x31\xC9){2})"
    rb"(?:\x48\x83\xC4\x28)?(?P<branch>[\xE8\xE9])(?P<target>.{4})",
    re.DOTALL)
CHUNK_PATTERN = re.compile(rb"\xBA(.{4})", re.DOTALL)


def location_of(between, versions):
    if b"\x33\xC9" in between or b"\x31\xC9" in between:
        return 0
    if b"\x44\x89\xC9" in between or b"\x41\x8B\xC9" in between:
        return versions
    match = re.search(rb"\xB9(.{4})", between, re.DOTALL)
    return struct.unpack("<I", match.group(1))[0] if match else None


def find_registrations(executable, registrar):
    virtual_address, size, raw_address = executable.sections[".text"]
    text = executable.image[raw_address:raw_address + size]
    registrations = []
    for match in REGISTRATION_PATTERN.finditer(text):
        start = executable.image_base + virtual_address + match.start()
        branch_end = start + match.end() - match.start()
        if branch_end + struct.unpack("<i", match.group("target"))[0] != registrar:
            continue
        table_end = start + match.start("table") - match.start() + 4
        table = table_end + struct.unpack("<i", match.group("table"))[0]
        versions = struct.unpack("<I", match.group("versions"))[0]
        chunk = CHUNK_PATTERN.search(match.group("between")).group(1)
        location = location_of(match.group("between"), versions)
        roots = {}
        for version in range(versions):
            root = executable.qword(table + VERSION_ENTRY_SIZE * version)
            if root and read_type(executable, root) is not None:
                roots[str(version)] = f"{root:X}"
        registrations.append({
            "location": LOCATIONS.get(location, str(location)),
            "chunk": chunk.rstrip(bytes(1)).decode("ascii"),
            "address": f"{start:X}",
            "versions": roots,
        })
    return registrations


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("executable")
    parser.add_argument("registrar", type=lambda value: int(value, 16))
    parser.add_argument("output")
    arguments = parser.parse_args()
    with open(arguments.executable, "rb") as handle:
        image = handle.read()
    executable = PortableExecutable(image)
    registrations = find_registrations(executable, arguments.registrar)
    catalog = {
        "format": "gw2-native-pref-schemas-v1",
        "executableSha256": hashlib.sha256(image).hexdigest().upper(),
        "registrations": registrations,
        "types": collect_types(executable, registrations),
    }
    with open(arguments.output, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(catalog, handle, separators=(",", ":"))
    print(f"{len(registrations)} registrations, {len(catalog['types'])} types", file=sys.stderr)


if __name__ == "__main__":
    main()
