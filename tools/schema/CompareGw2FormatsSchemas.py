#!/usr/bin/env python3

import argparse
import json
import os
import re
import sys

SCALAR_KINDS = {
    "byte": {5},
    "word": {21},
    "dword": {10, 36},
    "qword": {17, 37},
    "bool": {5, 10, 36, 37},
    "float": {12},
    "float2": {13},
    "float3": {14},
    "float4": {15},
    "dword2": {13, 17, 24},
    "dword4": {15, 24, 25},
    "helpers::WString": {18},
    "helpers::String": {19},
    "helpers::FileName": {11},
    "helpers::FileReference": {27},
}
ARRAY_KINDS = {1, 2, 3}
INLINE_ARRAY_KINDS = {1, 20, 24, 25, 29}
PTR_KINDS = {11, 16, 18, 19, 20, 29}
VECTOR_KINDS = {1, 20, 29}

ELEMENT_ALIASES = {"token64": "qword", "token32": "dword", "dword": "dword", "qword": "qword"}

STRUCT_RE = re.compile(r"struct\s+(\w+)\s*\{(.*?)public\s*:", re.S)
FIELD_RE = re.compile(r"^\s*([A-Za-z_][\w:]*(?:\s*<[^>]+>)?)\s+(\w+)(?:\[(\d+)\])?\s*$")


def parse_headers(root):
    structs = {}
    for dirpath, _dirs, files in os.walk(root):
        for name in files:
            if not name.endswith(".h"):
                continue
            with open(os.path.join(dirpath, name), encoding="utf-8", errors="replace") as handle:
                text = handle.read()
            for match in STRUCT_RE.finditer(text):
                struct_name = match.group(1)
                fields = []
                for raw in match.group(2).split(";"):
                    line = raw.strip()
                    if not line or line.startswith("//"):
                        continue
                    field = FIELD_RE.match(line)
                    if field:
                        fields.append((field.group(2), field.group(1).strip(),
                                       int(field.group(3)) if field.group(3) else None))
                if fields:
                    structs[struct_name] = fields
    return structs


def element_type(cpp_type):
    inner = re.search(r"<(.+)>", cpp_type)
    return inner.group(1).strip() if inner else None


def expected_kinds(cpp_type):
    if cpp_type in SCALAR_KINDS:
        return SCALAR_KINDS[cpp_type], None
    if cpp_type.startswith("helpers::Array") or cpp_type.startswith("helpers::RefList"):
        return ARRAY_KINDS, element_type(cpp_type)
    if cpp_type.startswith("helpers::Ptr"):
        return PTR_KINDS, element_type(cpp_type)
    if cpp_type.startswith("helpers::Vector"):
        return VECTOR_KINDS, element_type(cpp_type)
    return None, None


def load_catalog(path):
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    types = data["types"]
    by_name = {}
    for address, entry in types.items():
        by_name.setdefault(entry.get("name", ""), []).append((address, entry))
    return types, by_name


def resolve_name(types, address):
    entry = types.get(str(address))
    return entry.get("name") if entry else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--headers", required=True)
    parser.add_argument("--schemas", required=True)
    parser.add_argument("--json", default=None)
    args = parser.parse_args()

    structs = parse_headers(args.headers)
    types, by_name = load_catalog(args.schemas)

    compared = 0
    missing = []
    mismatches = []

    for name, fields in sorted(structs.items()):
        entries = by_name.get(name)
        if not entries:
            missing.append(name)
            continue
        address, entry = min(entries, key=lambda pair: abs(len(pair[1].get("fields", [])) - len(fields)))
        catalog_fields = entry.get("fields", [])
        compared += 1

        for index, (field_name, cpp_type, fixed_count) in enumerate(fields):
            if index >= len(catalog_fields):
                mismatches.append((name, field_name, cpp_type, "missing field"))
                continue
            catalog_field = catalog_fields[index]
            if fixed_count is not None:
                kinds, element = INLINE_ARRAY_KINDS, cpp_type
            else:
                kinds, element = expected_kinds(cpp_type)
            if kinds is None:
                continue
            kind = catalog_field.get("kind")
            if kind not in kinds:
                mismatches.append((name, field_name, cpp_type, f"kind {kind} not in {sorted(kinds)}"))
                continue
            if element and element[:1].isupper():
                referenced = resolve_name(types, catalog_field.get("type"))
                if referenced:
                    left = ELEMENT_ALIASES.get(referenced, referenced)
                    right = ELEMENT_ALIASES.get(element, element)
                    if left != right:
                        mismatches.append((name, field_name, cpp_type,
                                           f"element {referenced} != {element}"))

    print(f"gw2formats structs parsed: {len(structs)}")
    print(f"structs present in catalog: {compared}")
    print(f"structs missing from catalog: {len(missing)}")
    print(f"field mismatches: {len(mismatches)}")
    print()
    print("=== suspected field mismatches ===")
    by_type = {}
    for name, field, cpp, why in mismatches:
        by_type.setdefault(name, []).append((field, cpp, why))
    for name in sorted(by_type):
        print(f"{name}:")
        for field, cpp, why in by_type[name]:
            print(f"    {field}: {cpp} -> {why}")

    if missing:
        print()
        print("=== gw2formats structs absent from the catalog (coverage) ===")
        for name in missing:
            print(f"    {name}")

    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump({"mismatches": mismatches, "missing": missing}, handle, indent=2)

    return 1 if mismatches else 0


if __name__ == "__main__":
    sys.exit(main())
