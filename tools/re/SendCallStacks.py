import collections
import struct
import sys

CAPTURE_MAGIC = b"GW2NCAP1"
HEADER = struct.Struct("<8sIq")
RECORD = struct.Struct("<BIQqi")
SEND_CALL_STACK = 7


def send_call_stacks(capture_path):
    with open(capture_path, "rb") as file:
        data = file.read()
    magic, build, _ = HEADER.unpack_from(data, 0)
    if magic != CAPTURE_MAGIC:
        raise ValueError("not a GW2NCAP1 capture: %s" % capture_path)
    stacks = collections.Counter()
    offset = HEADER.size
    while offset + RECORD.size <= len(data):
        kind, protocol, _, _, length = RECORD.unpack_from(data, offset)
        offset += RECORD.size
        payload = data[offset:offset + length]
        offset += length
        if len(payload) != length:
            break
        if kind == SEND_CALL_STACK:
            stacks[(protocol, struct.unpack("<%dI" % (length // 4), payload))] += 1
    return build, stacks


def main(output_path, capture_paths):
    stacks = collections.Counter()
    builds = set()
    for capture_path in capture_paths:
        build, captured = send_call_stacks(capture_path)
        builds.add(build)
        stacks.update(captured)
    if len(builds) > 1:
        raise ValueError("captures span builds %s" % sorted(builds))
    with open(output_path, "w", encoding="utf-8", newline="\n") as file:
        file.write("id,count,returnAddressRvas\n")
        for (message_id, return_addresses), count in sorted(stacks.items()):
            file.write("%x,%d,%s\n" % (message_id, count, " ".join("%x" % rva for rva in return_addresses)))
    print("%d distinct send call stacks over %d ids -> %s" % (len(stacks), len({message_id for message_id, _ in stacks}), output_path))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
