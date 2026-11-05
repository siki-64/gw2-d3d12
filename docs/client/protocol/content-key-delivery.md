# Content key delivery: new client

How the new client implements [Content key delivery](../../../gw2-research/docs/protocol/content-key-delivery.md). The retail behaviour and its evidence are in that note.

## Text keys

The client's `TextKeyDelivery` (Gw2.Client.Net) applies decoded `0x2EC` batches and `0x2EB` to
`Gw2ContentReader.Keys`; `TextKeyDeliveryTests` checks the definition against the exe's schema registry.
