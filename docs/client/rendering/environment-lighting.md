# Environment lighting (sun and ambient): new client

How the new client implements [Environment lighting (sun and ambient)](../../../gw2-research/docs/rendering/environment-lighting.md). The retail behaviour and its evidence are in that note.

### Remake

`Gw2Client` owns a `Gw2TimeOfDayClock`. It starts at `--time-of-day HH:mm[:ss]` (default 12:00) through
`Gw2DayNightCycle.ClockFromTimeOfDay`, runs at `--time-rate` (default 1, the live rate) and is frozen by
`--still-scene`, or by the map flag when every map definition that uses the map file has it. Offline there is no map id,
and map files are shared: 1595's file is also used by the instances 1605, 1608 and 1642, which freeze the clock.
Each update advances it by the frame time in whole milliseconds, starting from the second frame, and recomputes the day weight. Each frame the lighting, haze and above-water constants are re-blended at the camera from the
zone weights and that day weight; the sky, clouds, sky cards, particle fields, map lights and post-processing already
read it per frame. The water material stays the one chosen at load. When the blended record switches between day and night water, its
textures replace the above-water texture slots. A screenshot logs the clock time and day weight it was taken at. Until the server clock (`0x4DC`)
feeds the offline client, the time comes from the command line.

## Sky cards (`EnvSkyCards_Submit`, 1248-byte runtime cards built by `EnvSkyCards_Build`)

`GrSkyCards.IsShown` applies both rules with the global water surface z (no water: +∞, so underwater cards stay
hidden).

## Card quad (`EnvSkyCards_BuildQuad`)

The remake passes `Gw2MapTerrain.ViewDistance`.

## Lens flares (`GrLensFlare.cpp`, `EnvSkyCards.cpp`)

`Gw2SkyCardLensFlare` ports the fill and the visibility, and `GrSkyCards.DrawLensFlares` draws after the world with
the flare AMAT (default effect).

## Map flag that freezes the clock

`Gw2TimeOfDayClock.EnterMap` mirrors the one-way set.
