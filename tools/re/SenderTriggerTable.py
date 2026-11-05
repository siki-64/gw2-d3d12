import json
import sys

TABLE_START = "<!-- sender table start -->"
TABLE_END = "<!-- sender table end -->"
MAXIMUM_TABLES_SHOWN = 2
MAXIMUM_INBOUND_SHOWN = 4
MAXIMUM_ARGUMENT_TEXT = 40
AGENT_COMMAND_SERIALIZE_SLOT = 6
MAXIMUM_OBSERVED_FRAMES = 3
MAXIMUM_OBSERVED_CHAINS = 2
MAXIMUM_STATIC_CALLERS = 3


def trigger_text(send):
    evidence = send["evidence"]
    parts = []
    observed = send.get("observedCallers", [])
    if observed:
        chains = ["called from " + " <- ".join(entry["chain"][:MAXIMUM_OBSERVED_FRAMES]) + " (%dx)" % entry["count"] for entry in observed[:MAXIMUM_OBSERVED_CHAINS]]
        parts.append("; ".join(chains) + (" (+%d)" % (len(observed) - MAXIMUM_OBSERVED_CHAINS) if len(observed) > MAXIMUM_OBSERVED_CHAINS else ""))
    static = send.get("staticCallers", [])
    if static and not observed:
        parts.append("slot called from " + ", ".join(static[:MAXIMUM_STATIC_CALLERS]) + (" (+%d)" % (len(static) - MAXIMUM_STATIC_CALLERS) if len(static) > MAXIMUM_STATIC_CALLERS else ""))
    if send["path"] == "agent-command":
        parts.append("agent command released")
    if send["path"] == "agent-world-report":
        parts.append("agent world tick")
    if evidence["inbound"]:
        inbound = evidence["inbound"]
        parts.append("reply to " + ", ".join(inbound[:MAXIMUM_INBOUND_SHOWN]) + (" (+%d)" % (len(inbound) - MAXIMUM_INBOUND_SHOWN) if len(inbound) > MAXIMUM_INBOUND_SHOWN else ""))
    for root in evidence["roots"]:
        parts.append(root)
    owners = evidence.get("tableOwners", {})
    tables = []
    for table in evidence["tables"]:
        name, offset = table.split("+")
        owner = owners.get(name)
        if send["path"] == "agent-command" and int(offset, 16) // 8 == AGENT_COMMAND_SERIALIZE_SLOT:
            continue
        tables.append("slot %d of %s" % (int(offset, 16) // 8, owner or name))
    if tables:
        parts.append("; ".join(tables[:MAXIMUM_TABLES_SHOWN]) + (" (+%d)" % (len(tables) - MAXIMUM_TABLES_SHOWN) if len(tables) > MAXIMUM_TABLES_SHOWN else ""))
    if evidence["functionPointers"] and not parts:
        parts.append("callback from " + ", ".join(evidence["functionPointers"][:MAXIMUM_TABLES_SHOWN]))
    return "; ".join(parts) or "unreached"


def value_text(value, arguments):
    kind = value["kind"]
    if kind == "constant":
        return "0x%x" % value["value"]
    if kind == "parameter":
        passed = arguments.get(str(value["index"]), [])
        shown = passed[0]["argument"][:MAXIMUM_ARGUMENT_TEXT] if passed else ""
        return "arg%d%s" % (value["index"], " (`%s`)" % shown if shown else "")
    return "`%s`" % value["text"][:MAXIMUM_ARGUMENT_TEXT].replace("|", "\\|")


def fields_text(send):
    return "<br>".join("+%x:%d %s" % (field["offset"], field["bytes"], value_text(field["value"], send["parameterArguments"])) for field in send["fields"])


def table(export):
    rows = ["| Channel | Id | Sender | Source | Trigger | Fields |", "| --- | --- | --- | --- | --- | --- |"]
    sends = [send for send in export["sends"] if send.get("channel")]
    for send in sorted(sends, key=lambda send: (send["channel"], send["messageId"], send["address"])):
        source = ((send.get("sourceFile") or {}).get("file") or "").rsplit("\\", 1)[-1]
        rows.append("| %s | 0x%x | `%s` | %s | %s | %s |" % (
            send["channel"], send["messageId"], send["address"], source, trigger_text(send).replace("|", "\\|"), fields_text(send)))
    for registered in export["registeredIdsWithoutSender"]:
        channel, message_id = registered.split(":")
        rows.append("| %s | 0x%s | none | | unreached: no send site in the image | |" % (channel, message_id))
    return "\n".join(rows)


def main(export_path, document_path):
    export = json.load(open(export_path, encoding="utf-8"))
    with open(document_path, encoding="utf-8", newline="") as file:
        document = file.read()
    newline = "\r\n" if "\r\n" in document else "\n"
    before, rest = document.split(TABLE_START, 1)
    _, after = rest.split(TABLE_END, 1)
    generated = table(export).replace("\n", newline)
    with open(document_path, "w", encoding="utf-8", newline="") as file:
        file.write(before + TABLE_START + newline + generated + newline + TABLE_END + after)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
