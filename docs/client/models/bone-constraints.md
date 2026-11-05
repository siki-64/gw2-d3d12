# Bone constraints (build 209026): new client

How the new client implements [Bone constraints (build 209026)](../../../gw2-research/docs/models/bone-constraints.md). The retail behaviour and its evidence are in that note.

## Wind

The client takes this vector as `Gw2BoneConstraintSimulator.Wind`.

## Client port

`Gw2BoneConstraintSimulator` (`Gw2.Client.Model`) runs the step on the local pose before the world matrices are
built. It covers the prediction, the angle cone (with the elliptical flag 8, taking [57] as `EllipseRatio` and [59] as `TwistOffset` by record order), twist (2), links (after the cone, before the spring), the bind-pose state setup, the spring with and without flag 4, the write-back with and without flag 0x40 and the
velocities with drag and clamp. The planar cone (0x10) is ported too. The wind force (step 5) is ported too, with the wind vector as an input. The model-speed blend raise is ported for models without flags 0x1000/0x2000. In that case the blend becomes
speed − 1 when the speed is over 1, unless the constraint has flag 0x100. The speed is an input (`ModelSpeed`).
Model motion is ported as well: the simulation runs in model space. When `ModelWorld` changes, the point and the
anchor are moved so that they stay put in the world. With 0x200 they follow the model fully; with 0x400 they follow
it by 1 − blend, clamped to [0, 1]. The collision hook (0x20) is ported with no world collider behind it. Not ported yet: the 0x1000/0x2000 blend paths, and
the source of the model's speed at +284.
