# Zone scatter (vegetation and ground cover): new client

How the new client implements [Zone scatter (vegetation and ground cover)](../../../gw2-research/docs/maps/zone-scatter.md). The retail behaviour and its evidence are in that note.

## Consumer

`Gw2PropLoader.ScatterAround` uploads one `GrStaticMeshInstances` per group and LOD-0 mesh. `GrCharacterPass` draws
them with the model material's variant-3 shaders and the 64-byte `GrVertexInstance` stream.

`GrCharacterPass` draws a fading batch with the fading effect key and `fxclr = (1, 1, 1, fade)`.

## Streaming

`Gw2.Client` follows this with `Gw2ScatterActivation`: blocks are keyed from the map origin, and `Gw2PropLoader`
uploads a block's instance groups when it enters and disposes them when it leaves.
