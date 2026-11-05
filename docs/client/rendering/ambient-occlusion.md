# Ambient occlusion: new client

How the new client implements [Ambient occlusion](../../../gw2-research/docs/rendering/ambient-occlusion.md). The retail behaviour and its evidence are in that note.

## Remake

`GrAmbientOcclusion` runs the five passes between the linear depth copy and `GrLightBuffer.Accumulate`, with the same
targets, formats and samplers (`Shaders/GrAmbientOcclusion.fx`). `GrAccumLightSunSh.fx` has an `AccumLightSunShOcclusion`
technique. `Gw2EnvironmentAmbientOcclusion` blends and clamps the environment values. It is on by default, as
in live; `--no-ambient-occlusion` turns it off and `--debug-view ambient-occlusion` shows the occlusion target.
