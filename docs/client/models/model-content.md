# Model content layout and dependency chain: new client

How the new client implements [Model content layout and dependency chain](../../../gw2-research/docs/models/model-content.md). The retail behaviour and its evidence are in that note.

## Bounding sphere

`Gw2Model.BoundingSphere` decodes it; per-mesh bounds stay in `Gw2ModelMeshBounds`.

## Cloth and lightning

`lodOverride` becomes `Gw2Model.LodOverride` (floats). `soundScript` becomes `Gw2Model.SoundScript`, but no model
populates it.

## Skeleton and mesh skinning (recovered)

`CharacterScene` builds one by id straight from the archive through `Gw2ModelBuilder`.
