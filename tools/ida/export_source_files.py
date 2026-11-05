import bisect
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gw2re_ida

SOURCE_ROOT_MARKER = "\\Code\\"
SOURCE_EXTENSION = ".cpp"


def source_path(asserted_path):
    index = asserted_path.find(SOURCE_ROOT_MARKER)
    return asserted_path[index + len(SOURCE_ROOT_MARKER):] if index >= 0 else asserted_path


def is_source_file(path):
    return path.lower().endswith(SOURCE_EXTENSION)


def asserted_files_by_function(assert_sites_path):
    rows = json.load(open(assert_sites_path, encoding="utf-8"))["rows"]
    files = collections.defaultdict(collections.Counter)
    for row in rows:
        if row["file"]:
            files[int(row["function"][4:], 16)][source_path(row["file"])] += 1
    return files


def most_asserted(file_counts):
    return max(file_counts.items(), key=lambda item: (item[1], item[0]))[0]


def split_own_files(asserted_files):
    source_files = {}
    header_files = {}
    for function, file_counts in asserted_files.items():
        sources = collections.Counter({path: count for path, count in file_counts.items() if is_source_file(path)})
        if sources:
            source_files[function] = most_asserted(sources)
        else:
            header_files[function] = most_asserted(file_counts)
    return source_files, header_files


def calling_functions(function):
    callers = set()
    for reference in gw2re_ida.database().xrefs.code_refs_to_ea(function):
        caller = gw2re_ida.containing_function(reference)
        if caller is not None and caller.start_ea != function:
            callers.add(caller.start_ea)
    return callers


class Placement:
    def __init__(self, source_files, header_files):
        self.source_files = source_files
        self.header_files = header_files
        self.placed_starts = sorted(source_files)

    def neighbor_files(self, function):
        index = bisect.bisect_left(self.placed_starts, function)
        previous_file = self.source_files[self.placed_starts[index - 1]] if index > 0 else None
        next_file = self.source_files[self.placed_starts[index]] if index < len(self.placed_starts) else None
        return previous_file, next_file

    def place(self, function):
        if function in self.source_files:
            return self.source_files[function], "assert"
        previous_file, next_file = self.neighbor_files(function)
        if previous_file is not None and previous_file == next_file:
            return previous_file, "between"
        boundary_files = {path for path in (previous_file, next_file) if path is not None}
        caller_files = {self.source_files[caller] for caller in calling_functions(function) if caller in self.source_files}
        boundary_callers = boundary_files & caller_files
        if len(boundary_callers) == 1:
            return next(iter(boundary_callers)), "boundary-caller"
        if function in self.header_files:
            return self.header_files[function], "header"
        return None, None


def export(assert_sites_path=None, output_path=None):
    assert_sites_path = assert_sites_path or gw2re_ida.default_output_path(".assert-sites.json")
    output_path = output_path or gw2re_ida.default_output_path(".source-files.json")
    source_files, header_files = split_own_files(asserted_files_by_function(assert_sites_path))
    placement = Placement(source_files, header_files)
    functions = {}
    method_counts = collections.Counter()
    for function in sorted(gw2re_ida.function_starts()):
        path, method = placement.place(function)
        method_counts[method or "unplaced"] += 1
        if path is not None:
            functions["%x" % function] = {"file": path, "method": method}
    payload = {
        "schemaVersion": 1,
        "kind": "function-source-files",
        "producedBy": "tools/ida/export_source_files.py",
        "image": gw2re_ida.image_identity(),
        "methodCounts": dict(method_counts),
        "fileCount": len({entry["file"] for entry in functions.values()}),
        "functions": functions,
    }
    gw2re_ida.write_json(output_path, payload)
    summary = gw2re_ida.artifact_summary(output_path, "function-source-files", **dict(method_counts))
    print("[export_source_files] wrote %s" % summary["artifact"], flush=True)
    print("[export_source_files] %s" % dict(method_counts), flush=True)
    return summary


if __name__ == "__main__":
    export(*gw2re_ida.script_arguments(2)[:2])
