# Havok: new client

How the new client implements [Havok](../../../gw2-research/docs/engine/havok.md). The retail behaviour and its evidence are in that note.

## Placements

`Gw2MapCollision.TryResolveAnimation` mirrors the key lookup; `Gw2CollisionScene` (`Gw2.Client.Scene`) builds the
terrain and placement shapes and answers ray casts in world units.
