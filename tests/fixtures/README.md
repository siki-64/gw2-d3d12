# Protocol fixtures

Byte fixtures live under `<build>/` here, where the offline decoder in `tools/re/` and the `Gw2.Exe.Protocol.Tests` harnesses consume them. The protocol fixtures are on 208.784 only and decode with its schemas.

Captured fixtures (`"kind": "captured"`) come from one live 208.784 session that `Gw2.LiveLogin --play --record-transport` recorded to `captures/local/live-transport.json`. `tools/Gw2.TransportFixtures` replays the session's transport key over the recorded bytes and writes `handshake-live.json`, `keystream-reuse.json`, `captured-inbound.json` (one whole packet and the cipher state before it), `captured-inbound-0x403.json` and `captured-inbound-0x424.json` (decrypted frames), and `captured-outbound-0x11.json`, `-0x199.json` and `-0x1a4.json` (flushes and the state before each):

```
dotnet run --project tools/Gw2.TransportFixtures -- captures/local/live-transport.json gw2-research/tests/fixtures/208784 --inbound-ids 23 --frame-ids 424,403 --outbound-ids 11,1a4,199
```

`--list` prints every packet's message ids instead, and `--describe-ids 1ac` prints the field shapes (types and lengths, no values) of every message with those ids across the whole inbound stream, so a message carrying names or account ids can be checked against its schema without writing it out. Pick only messages without account names or tokens. `captured-inbound-0x39.json` comes from `--frame-ids 39`. Synthetic fixtures (`"kind": "synthetic"`) are `0x264-wire.json` and `outbound-0x11f.json` from `tools/re/MakeSyntheticFixtures.py`, `transport-cipher.json` from `tools/re/Gw2TransportCipher.py --emit-fixture` and `handshake-kdf.json` from `tools/re/Gw2HandshakeKdf.py --emit-fixture`, whose captured vector is the recorded session's seed.

For each fixture record build, direction, connection role, capture layer, provenance, sanitization, expected decode and unknown fields. Label synthetic bytes explicitly. Keep original private captures in `captures/local`, and never call a native memory record a network packet fixture.

`208784/0x2e-wire.json` is the first live `0x2e` agent move of a 208.784 session, the controlled character's own, recorded by `Gw2.LiveLogin --dump`. `Gw2.Exe.Handlers.Tests` consumes it.

`208784/dxt5-encode/` holds a BGRA input (`.bgra`) and the DXT5 blocks (`.dxt5`) that the exe blitter `ImgBlt_EncodeDxt5` produced from it, called from x64dbg in the live 208039 client. Its encode routines are byte-identical in 208784 apart from moved table addresses, and the tables match. `noise-13x7` is LCG noise (seed 12345). `Gw2.Client.Scene.Tests` consumes them.
