# Material animation: new client

How the new client implements [Material animation](../../../gw2-research/docs/models/material-animation.md). The retail behaviour and its evidence are in that note.

## Consequence for the client

Without these, every water, waterfall, lava and portal surface is frozen. The dat side already decodes the inputs:
`TextureTransforms` on the model material, and `UvAnimations` and `MaterialConstantAnimations` in the animation
bank. The client needs the per-input TexTransform constants in the model shaders, evaluated as above at the same
point in the frame.
