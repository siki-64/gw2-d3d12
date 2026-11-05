# Animation control objects (build 209026): new client

How the new client implements [Animation control objects (build 209026)](../../../gw2-research/docs/models/animation-controls.md). The retail behaviour and its evidence are in that note.

## From an entry to its clip clock

`Gw2AnimationController` (`Gw2.Client.Model`) ports the weight step (ease, merge of restarted clips, priority weights)
and the Granny clock advance.

## Client blend

`Gw2SkeletonPose.WorldMatricesBlended` combines weighted clip samples into one local pose: per bone it sums the sampled
position and scale by weight, sums the sign-aligned orientations and normalises them, and keeps the bind pose where no
sample covers the bone.

Additive layers follow `sub_140D18400` (ModelAnimLayer sample, ModelAnimLayer.cpp:796). The additive controls are
sampled into a separate pose. Each bone's weight is the largest control weight × track mask value, times the layer
weight at +8, capped at 1. The delta's quaternion is flipped to w ≥ 0 and blended from the identity transform by that
weight with `sub_140FAA8C0` (lerp of position, sign-aligned nlerp of orientation). `sub_140D124B0` then calls
`sub_140D11610`, which composes every bone as base × delta (`sub_140FAAB70`, Granny's transform multiply): position
base + base rotation × delta position, orientation base × delta. The client applies this per additive sample.
A sample may carry a track mask: its weight for a bone is the weight times the mask value for that bone (`sub_140D4E140`, `sub_140F9C360`). ModelAnimInherit is not applied yet.

### Track masks

- Granny's mask (`granny_track_mask.cpp`) is 24 bytes: default weight (+0), bone weight count (+4), bone weights
  (+8). `sub_140FA6B90` returns the bone's weight, or the default past the count. `sub_140FA6C80` creates a mask
  with every bone at a default weight, and `sub_140FA6FB0` sets one bone.
- `sub_140CDA7F0` builds a mask at run time from a default weight and a list of bone tokens, resolved through the
  skeleton (`sub_140D385F0`).
- The model file's `trackMasks` entries (`ModelQuery.cpp`, `sub_140D101F0`) are 20-byte entries with the token at
  +12. Their `trackMask` field is a byte array of type `PackGrannyTrackMaskType`.
- The `trackMask` bytes are a serialised Granny track mask:
  - +0: the default weight;
  - +4: the bone weight count;
  - +8: the offset of the weights from the start of the bytes;
  - then one float weight per skeleton bone, in bone order, followed by Granny's own type table (`DefaultWeight`,
    `BoneWeights`, ...).

  Model 13869 has 153 bones and 9 masks. Its first mask (token 166950) has default 0, `bone:Head` at 0.5, and the
  face bones and `bone:Jaw` at 1. `Gw2ModelTrackMask.BoneWeights` and `WeightOf` decode it.
