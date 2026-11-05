# Model pose buffers (build 209026): new client

How the new client implements [Model pose buffers (build 209026)](../../../gw2-research/docs/models/model-pose-buffers.md). The retail behaviour and its evidence are in that note.

## Bind pose difference

`Gw2PoseDifference` (`Gw2.Client.Scene`) computes it per bone: the largest offset of four probe points (origin and the
unit axes) and the rotation angle.
