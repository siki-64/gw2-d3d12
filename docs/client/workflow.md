# Contract-to-client workflow

## Architecture

Inbound: transport bytes -> session framing/decryption/decompression -> build-specific wire decoder -> semantic event -> client-state reducer -> presentation on the game thread.

Outbound: user intent -> session/state validation -> semantic command -> build-specific encoder -> session framing -> transport.

Keep transport, session, protocol, state, assets and presentation as distinct responsibilities. Add assemblies when each has implementation, rather than create empty projects. Runtime code must not reference native-process concerns or reverse-engineering tooling. The `src/Dat/` libraries are the parser stage; the client and the tools reference the same projects.

The transport codecs live in `src/Protocol/Gw2.Protocol`. Because the cipher is shared across directions (the handshake copies the inbound state to the outbound slot), both codecs must be given the same `TransportCipherState`.

## Rendering boundary and engine-agnostic data

The archive content the official client renders (terrain geometry, PIMG page images, ATEX surface textures, material and constant data) is engine-agnostic data, not engine code, and is decoded faithfully: byte-exact where the payload survives the transfer (DDS records, mesh indices, winding order), transformed or reconstructed where it cannot.

GW2 shader bytecode (AMAT `AmatMaterialV1.shaders`) is the ground truth for how the data is meant to be interpreted: texture-slot binding order, weight-channel order, per-page atlas orientation. When a decoded asset disagrees with the client's appearance, the fix belongs in the decode, not in a hand-tuned shader; render-side flips or fudge factors signal that a data convention (mesh UV orientation, PIMG page row order, surface-slot mapping) is still wrong. The terrain shader still leaves the layer-1 modulation, the cubemap/SSAO terms and the `t6..t9` normal set unmodelled; those are decode-side gaps. The decode stages are in [`asset-pipeline.md`](asset-pipeline.md).

## Evidence progression

1. Select one scenario and build. Record executable hash, capture point, connection role, direction, timestamps and triggering action. Store private captures under captures/local.
2. Trace both sides of the boundary: wire bytes and native schema-decoder/serializer output. Record framing, bit packing, sizes, state prerequisites and transforms separately. A handler record is not a wire fixture.
3. Document the message in `gw2-research/docs/protocol/` with its field tree and unresolved list. Mark each claim as unknown, static, observed or independently reproduced. Keep contradictory evidence explicit. The offline tooling in [`../../tools/gw2re.ps1`](../../tools/gw2re.ps1) records the build stamp and hashes private inputs.
4. Write a sanitized fixture with its provenance and expected semantic result. Synthetic fixtures test implementation only; they cannot confirm the protocol. Preserve unknown fields and avoid assigning unproven names.
5. Implement one bounded codec and deterministic state update. Validate truncation at every boundary, bad lengths/counts, unknown IDs, allocation limits and no partial state mutation. Round trips supplement independent fixtures; they do not replace them.
6. Replay offline, then show the resulting state in the client. Bound inbound work per frame.
7. For outbound messages, recover the actual serializer and session state machine, sequence/acknowledgement rules and required prior messages. Compare encoded bytes to captured output before a controlled interoperability run.
8. Record validation separately: static analysis, synthetic tests, captured replay, client and live interoperability. On build changes, revalidate the affected catalog rather than changing one global number and assuming compatibility.

## Scope of the findings

The protocol research is on build 208784 and is an evidence scope, not a declaration of current server support. Do not vendor live captures, caches, dumped fonts or game assets.

Outbound sequencing is recovered as a **negative**: there is no transport-layer sequence, acknowledgement, length or compression container, and one flush is `[u16 msgid][fields]...` encrypted as a single RC4 block ([`session-state.md`](../../gw2-research/docs/protocol/session-state.md)).

## Updating findings

Findings are reference material under `gw2-research/docs/` and `src/Dat/`, kept separate from runtime code. Do not add dumps, generated atlases or private traces by globbing a whole repository.
