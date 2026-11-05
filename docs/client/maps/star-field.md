# Star field: new client

How the new client implements [Star field](../../../gw2-research/docs/maps/star-field.md). The retail behaviour and its evidence are in that note.

## In the remake

`Gw2StarFieldMesh` builds the vertices and indices as above. `GrStarField` draws them with `GrStarField.fx`, a
hand port of the embedded shaders, after the sky dome and before the sky cards.
