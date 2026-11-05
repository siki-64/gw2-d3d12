import argparse
import hashlib
import json
import struct
import sys

FIELD_SIZE = 32
VERSION_ENTRY_SIZE = 24
KIND_MASK = 0xFF
VARIANT_KIND = 28
STRUCTURE_KINDS = {1, 2, 3, 16, 20, 29}
MAX_FIELDS = 512
MAX_VERSIONS = 256


class PortableExecutable:
    def __init__(self, image):
        self.image = image
        header = struct.unpack_from("<I", image, 0x3C)[0]
        section_count = struct.unpack_from("<H", image, header + 6)[0]
        optional_size = struct.unpack_from("<H", image, header + 20)[0]
        self.image_base = struct.unpack_from("<Q", image, header + 24 + 24)[0]
        self.sections = {}
        for index in range(section_count):
            entry = header + 24 + optional_size + 40 * index
            name = image[entry:entry + 8].rstrip(b"\0").decode("ascii")
            virtual_size, virtual_address, raw_size, raw_address = struct.unpack_from("<IIII", image, entry + 8)
            self.sections[name] = (virtual_address, min(virtual_size, raw_size), raw_address)

    def offset(self, address):
        relative = address - self.image_base
        for virtual_address, size, raw_address in self.sections.values():
            if virtual_address <= relative < virtual_address + size:
                return raw_address + relative - virtual_address
        return None

    def address(self, section, offset_in_section):
        virtual_address, _, _ = self.sections[section]
        return self.image_base + virtual_address + offset_in_section

    def in_section(self, address, section):
        virtual_address, size, _ = self.sections[section]
        relative = address - self.image_base
        return virtual_address <= relative < virtual_address + size

    def qword(self, address):
        return struct.unpack_from("<Q", self.image, self.offset(address))[0]

    def name(self, address):
        start = self.offset(address)
        if start is None:
            return None
        end = self.image.find(b"\0", start, start + 256)
        if end < start:
            return None
        raw = self.image[start:end]
        if not all(0x20 <= byte < 0x7F for byte in raw):
            return None
        return raw.decode("ascii")


def variant_members(executable, address, count):
    members = []
    for index in range(count):
        member = executable.qword(address + 8 * index)
        if member:
            members.append(f"{member:X}")
    return members


def read_type(executable, address):
    if not executable.in_section(address, ".rdata") and not executable.in_section(address, ".data"):
        return None
    fields = []
    cursor = address
    for _ in range(MAX_FIELDS):
        offset = executable.offset(cursor)
        if offset is None:
            return None
        kind, name_address, type_address, count = struct.unpack_from("<QQQQ", executable.image, offset)
        name = executable.name(name_address) if name_address else None
        if name is None:
            return None
        if kind & KIND_MASK == 0:
            return {"fields": fields, "name": name}
        field = {"kind": kind & KIND_MASK, "name": name, "count": count}
        if field["kind"] == VARIANT_KIND:
            field["variants"] = variant_members(executable, type_address, count)
        elif type_address and field["kind"] in STRUCTURE_KINDS:
            field["type"] = f"{type_address:X}"
        fields.append(field)
        cursor += FIELD_SIZE
    return None


def read_registration(executable, address):
    offset = executable.offset(address)
    chunk_raw, version_count, table = struct.unpack_from("<4sIQ", executable.image, offset)
    chunk = chunk_raw.rstrip(bytes(1))
    if len(chunk) < 3 or not all(0x30 <= byte < 0x7B and chr(byte).isalnum() for byte in chunk):
        return None
    if not 0 < version_count <= MAX_VERSIONS or not executable.in_section(table, ".rdata"):
        return None
    versions = {}
    upgrade_routines = {}
    skipped_versions = []
    for version in range(version_count):
        entry = table + VERSION_ENTRY_SIZE * version
        root = executable.qword(entry)
        if root == 0:
            continue
        if read_type(executable, root) is None:
            return None
        versions[str(version)] = f"{root:X}"
        routine = executable.qword(entry + 8)
        if routine != 0:
            upgrade_routines[str(version)] = f"{routine:X}"
        if executable.qword(entry + 16) & 0xFFFFFFFF:
            skipped_versions.append(version)
    if not versions:
        return None
    return {"chunk": chunk_raw.decode("ascii"), "address": f"{address:X}", "versions": versions,
            "upgradeRoutines": upgrade_routines, "skippedVersions": skipped_versions}


def collect_types(executable, registrations):
    types = {}
    pending = [root for registration in registrations for root in registration["versions"].values()]
    while pending:
        identifier = pending.pop()
        if identifier in types:
            continue
        definition = read_type(executable, int(identifier, 16))
        if definition is None:
            continue
        types[identifier] = definition
        for field in definition["fields"]:
            pending.extend(field.get("variants", []))
            if "type" in field:
                pending.append(field["type"])
    return types


def extract(image):
    executable = PortableExecutable(image)
    _, size, _ = executable.sections[".rdata"]
    registrations = []
    for offset_in_section in range(0, size - 16, 8):
        registration = read_registration(executable, executable.address(".rdata", offset_in_section))
        if registration is not None:
            registrations.append(registration)
    return {
        "format": "gw2-native-pack-schemas-v1",
        "executableSha256": hashlib.sha256(image).hexdigest().upper(),
        "registrations": registrations,
        "types": collect_types(executable, registrations),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("executable")
    parser.add_argument("output")
    arguments = parser.parse_args()
    with open(arguments.executable, "rb") as handle:
        catalog = extract(handle.read())
    with open(arguments.output, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(catalog, handle, separators=(",", ":"))
    print(f"{len(catalog['registrations'])} registrations, {len(catalog['types'])} types", file=sys.stderr)


if __name__ == "__main__":
    main()
