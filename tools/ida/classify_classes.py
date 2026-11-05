import json
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

TEMPLATE_FILES = {
    r"Gw2\Engine\Controls\CtlInstance.h",
    r"Gw2\Engine\Agent\AgentInl.h",
    r"Gw2\Game\Content\CnUtil.h",
    r"Gw2\Services\ScratchCollection\ScratchCollectionInl.h",
    r"Arena\Core\Basics\MathInl.h",
    r"Arena\Core\Basics\Ref.h",
    r"Arena\Core\Basics\String.h",
    r"Arena\Core\Collections\Dictionary.h",
    r"Arena\Core\Collections\Hash.h",
    r"Arena\Core\Collections\List.h",
    r"Arena\Core\Collections\Set.h",
    r"Arena\Services\Handle\Handle.h",
    r"Arena\Engine\ArenaWeb\ArenaWebInl.h",
    r"Arena\Engine\Gr\GrInt.h",
}

CLASS_FILE_SUFFIXES = (".cpp", ".h", ".hpp", ".inl")

SOURCE_PREFIX = r"D:\Perforce\Live\NAEU\v2\Code"
HAVOK_PREFIXES = ("hk", "hknp", "hkp", "hkgp", "hka", "hkb", "hks", "hcl", "hct", "hkd", "hkr")
REPORTED_SLOT_FILES = 5
REPORTED_TOP_FILES = 40
PROGRESS_INTERVAL = 1000


def strip_prefix(path):
    if not path:
        return ""
    text = path
    if text.startswith(SOURCE_PREFIX):
        text = text[len(SOURCE_PREFIX):]
    return text.lstrip("\\/")


def source_tree(path):
    if not path:
        return "unknown"
    text = strip_prefix(path).lower()
    if text.startswith("gw2\\services\\"):
        return "services"
    if text.startswith("gw2\\engine\\"):
        return "game"
    if text.startswith("gw2\\"):
        return "game"
    if text.startswith("arena\\services\\"):
        return "services"
    if text.startswith("arena\\engine\\"):
        return "engine"
    if text.startswith("arena\\core\\"):
        return "core"
    if text.startswith("arena\\"):
        return "engine"
    return "unknown"


def is_havok_rtti(name):
    if not name:
        return False
    base = name[4:] if name.startswith(".?AV") else name
    return any(base.startswith(prefix) for prefix in HAVOK_PREFIXES)


def log(*values):
    print(*values, flush=True)


def assert_file_index(asserts):
    index = defaultdict(Counter)
    for row in asserts.get("rows", []):
        function = row.get("function")
        file_name = row.get("file")
        if function and file_name:
            index[function][file_name] += 1
    return index


def is_class_file(file_name):
    if not file_name.isascii():
        return False
    if "\\" not in file_name and "/" not in file_name:
        return False
    if not file_name.endswith(CLASS_FILE_SUFFIXES):
        return False
    return strip_prefix(file_name) not in TEMPLATE_FILES


def best_file(counter):
    candidates = [file_name for file_name, _ in counter.most_common() if is_class_file(file_name)]
    for file_name in candidates:
        if file_name.endswith(".cpp"):
            return file_name
    return candidates[0] if candidates else None


def vtable_class_files(assert_index, vtable):
    counter = Counter()
    for slot in vtable.get("slots", []):
        target = slot.get("target")
        if not target or target.startswith("_") or "purecall" in target:
            continue
        target_address = gw2re_ida.try_normalize_address(target)
        if target_address is None:
            continue
        resolved = gw2re_ida.resolved_function(target_address)
        resolved_address = resolved.start_ea if resolved is not None else target_address
        name = gw2re_ida.function_name(resolved_address) or "sub_%X" % resolved_address
        counter.update(assert_index.get(name, Counter()))
    return counter


def export(output_path=None, asserts_path=None, vtables_path=None, report_path=None):
    output_path = output_path or gw2re_ida.default_output_path(".class-model.json")
    asserts_path = asserts_path or gw2re_ida.default_output_path(".assert-sites.json")
    vtables_path = vtables_path or gw2re_ida.default_output_path(".vtables.json")
    report_path = report_path or gw2re_ida.default_output_path(".class-model.txt")

    log("[classify-classes] loading %s" % asserts_path)
    with open(asserts_path, "r", encoding="utf-8") as handle:
        asserts = json.load(handle)
    log("[classify-classes] loading %s" % vtables_path)
    with open(vtables_path, "r", encoding="utf-8") as handle:
        vtables = json.load(handle)

    log("[classify-classes] assert rows: %d" % len(asserts.get("rows", [])))
    log("[classify-classes] vtables: %s, constructors: %s"
        % (vtables.get("vtableCount"), vtables.get("constructorCount")))

    assert_index = assert_file_index(asserts)
    log("[classify-classes] functions with assert files: %d" % len(assert_index))

    vtable_by_address = {}
    for vtable in vtables.get("vtables", []):
        address = gw2re_ida.try_normalize_address(vtable["address"])
        if address is not None:
            vtable_by_address[address] = vtable

    log("[classify-classes] rolling up vtable slot files...")
    vtable_files = {}
    for index, (address, vtable) in enumerate(vtable_by_address.items()):
        if index % PROGRESS_INTERVAL == 0:
            log("[classify-classes]   %d/%d" % (index, len(vtable_by_address)))
        vtable_files[address] = vtable_class_files(assert_index, vtable)

    log("[classify-classes] classifying constructors...")
    classes = []
    vtable_placements = defaultdict(list)
    stats = {
        "ctors": 0,
        "ctorsWithRtti": 0,
        "ctorsHavok": 0,
        "ctorsArenanet": 0,
        "ctorsOther": 0,
        "placements": 0,
        "vtablesWithRtti": 0,
        "vtablesWithAssert": 0,
    }

    for index, constructor in enumerate(vtables.get("constructors", [])):
        if index % PROGRESS_INTERVAL == 0:
            log("[classify-classes]   ctor %d/%s" % (index, vtables.get("constructorCount")))

        constructor_name = constructor["function"]
        stores = constructor.get("stores", [])

        primary_rtti = None
        for store in stores:
            address = gw2re_ida.try_normalize_address(store["vtable"])
            if address is None:
                continue
            name = gw2re_ida.read_rtti_name_cached(address)
            if name:
                primary_rtti = name
                break

        if primary_rtti:
            stats["ctorsWithRtti"] += 1
            if is_havok_rtti(primary_rtti):
                kind = "havok"
                stats["ctorsHavok"] += 1
            else:
                kind = "other"
                stats["ctorsOther"] += 1
        else:
            kind = "arenanet"
            stats["ctorsArenanet"] += 1

        placements = []
        constructor_files = Counter()
        for store in stores:
            address = gw2re_ida.try_normalize_address(store["vtable"])
            vtable = vtable_by_address.get(address, {}) if address is not None else {}
            rtti = gw2re_ida.read_rtti_name_cached(address) if address is not None else None
            slot_files = vtable_files.get(address, Counter()) if address else Counter()

            placements.append({
                "vptrOffset": store["vptrOffset"],
                "vtable": store["vtable"],
                "slotCount": vtable.get("slotCount", 0),
                "rtti": rtti,
                "rttiInheritedFrom": primary_rtti,
                "slotFiles": [file_name for file_name, _ in slot_files.most_common(REPORTED_SLOT_FILES)],
            })

            stats["placements"] += 1
            if rtti:
                stats["vtablesWithRtti"] += 1
            if slot_files:
                stats["vtablesWithAssert"] += 1

            if address is not None:
                vtable_placements[address].append({"ctor": constructor_name, "vptrOffset": store["vptrOffset"]})

            for file_name in placements[-1]["slotFiles"]:
                constructor_files[file_name] += 1

        top_file = best_file(constructor_files)
        classes.append({
            "ctor": constructor_name,
            "rttiClass": primary_rtti,
            "classKind": kind,
            "ctorTopFile": top_file,
            "ctorTree": source_tree(top_file),
            "placements": placements,
        })
        stats["ctors"] += 1

    vtable_rows = []
    for address in sorted(vtable_by_address):
        vtable = vtable_by_address[address]
        file_name = best_file(vtable_files[address])
        vtable_rows.append({
            "vtable": "%x" % address,
            "slotCount": vtable.get("slotCount", 0),
            "rttiClass": gw2re_ida.read_rtti_name_cached(address),
            "assertPath": file_name,
            "tree": source_tree(file_name),
            "placements": vtable_placements.get(address, []),
        })

    gw2re_ida.write_json(output_path, {"stats": stats, "classes": classes, "vtables": vtable_rows})
    log("[classify-classes] wrote %s" % os.path.abspath(output_path))

    havok_classes = set()
    arenanet_files = set()
    for entry in classes:
        if entry["classKind"] == "havok" and entry["rttiClass"]:
            havok_classes.add(entry["rttiClass"])
        if entry["classKind"] == "arenanet" and entry["ctorTopFile"]:
            arenanet_files.add(entry["ctorTopFile"])

    constructor_tree_counts = Counter(entry["ctorTree"] for entry in classes)
    file_tree_map = {entry["ctorTopFile"]: entry["ctorTree"] for entry in classes if entry["ctorTopFile"]}
    file_tree_counts = Counter(file_tree_map.values())
    arenanet_tree_counts = Counter(entry["ctorTree"] for entry in classes if entry["classKind"] == "arenanet")
    vtable_tree_counts = Counter(row["tree"] for row in vtable_rows)

    lines = []
    lines.append("=" * 60)
    lines.append("CLASS MODEL REPORT")
    lines.append("=" * 60)
    lines.append("ctors processed      : %d" % stats["ctors"])
    lines.append("ctors with RTTI      : %d" % stats["ctorsWithRtti"])
    lines.append("  havok ctors        : %d" % stats["ctorsHavok"])
    lines.append("  other-RTTI ctors   : %d" % stats["ctorsOther"])
    lines.append("  arenanet ctors     : %d" % stats["ctorsArenanet"])
    lines.append("placements           : %d" % stats["placements"])
    lines.append("vtables w/ RTTI      : %d" % stats["vtablesWithRtti"])
    lines.append("vtables w/ assert    : %d" % stats["vtablesWithAssert"])
    lines.append("")
    lines.append("Havok classes        : %d" % len(havok_classes))
    lines.append("ArenaNet source files: %d" % len(arenanet_files))
    lines.append("")

    def distribution(title, counts):
        lines.append("-" * 60)
        lines.append(title)
        lines.append("-" * 60)
        total = sum(counts.values())
        for tree, count in counts.most_common():
            percentage = 100.0 * count / total if total else 0
            lines.append("  %-12s %6d  (%5.1f%%)" % (tree, count, percentage))
        lines.append("")

    distribution("TREE DISTRIBUTION (ctors)", constructor_tree_counts)
    distribution("TREE DISTRIBUTION (ArenaNet ctors only)", arenanet_tree_counts)
    distribution("TREE DISTRIBUTION (distinct source files)", file_tree_counts)
    distribution("TREE DISTRIBUTION (vtables)", vtable_tree_counts)

    lines.append("-" * 60)
    lines.append("Top ArenaNet source files (by ctor count)")
    lines.append("-" * 60)
    file_counts = Counter(entry["ctorTopFile"] for entry in classes
                          if entry["classKind"] == "arenanet" and entry["ctorTopFile"])
    for file_name, count in file_counts.most_common(REPORTED_TOP_FILES):
        lines.append("  %5d  [%-10s]  %s" % (count, source_tree(file_name), file_name))

    report = "\n".join(lines)
    with open(report_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(report + "\n")

    log("")
    log(report)
    log("")
    summary = gw2re_ida.artifact_summary(
        output_path, "class-model",
        report=os.path.abspath(report_path),
        ctors=stats["ctors"], placements=stats["placements"], vtables=len(vtable_rows),
        ctorsWithRtti=stats["ctorsWithRtti"], havokCtors=stats["ctorsHavok"],
        otherRttiCtors=stats["ctorsOther"], arenanetCtors=stats["ctorsArenanet"],
        havokClasses=len(havok_classes), arenanetSourceFiles=len(arenanet_files))
    log("[classify-classes] wrote %s" % summary["artifact"])
    log("[classify-classes] report %s" % summary["report"])
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(1)[:1])
