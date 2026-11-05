# Post-processing, HDR decode and screen/depth fades: new client

How the new client implements [Post-processing, HDR decode and screen/depth fades](../../../gw2-research/docs/rendering/post-processing.md). The retail behaviour and its evidence are in that note.

## Post-process combine: saturation and tint

The remake decodes these values in `Gw2EnvironmentGrade` and draws `GrPostProcessCombine.fx` over the scene.

## Enable mask

The remake keeps both off unless `--saturation-and-tint` or `--distortion` is given.

The remake follows this default and applies the grade only with `--saturation-and-tint`.

## Environment LUT

The remake decodes the files in `Gw2ColorLookup` and the weights in `Gw2EnvironmentColorLookup`, and draws the combine
above when Color Grading is on (the default, as live; `--no-color-grading` turns it off).

## Render scale (Render Sampling)

In the remake, `Gw2RenderScale` computes the factor and size.

The remake decodes these values in `Gw2EnvironmentBloom`.

### Client

`--light-rays` sets `Gw2PostProcessingOptions.LightRays`. After the lens flares, `GrSkyCards.LightRayScreenPosition`
computes the position and `DrawLightRaySources` writes the sources. The remake's scene materials do not write
`StencilId` alpha, so the source pass first clears scene alpha to 0, then draws the non-material flag-2 cards with the
stencil variant (`LightRaySourceCard`, alpha only, depth read). `GrPostProcessAdaptation` runs the mask and rays
after depth blur when `GrPostProcessChain.RunsLightRays`.

Not ported:

- post quality gating (the client has no post quality preset)
- flag-2 cards with an `AMAT` (none of the test maps have one)
- water as a source, since the alpha clear runs after water

Test environments: 3399578 and 3721199 have cards that pass the 0.6 test at noon. Card 0 of 3776288 (maps 1613–1624,
1648) peaks at 0.39, so it never becomes a source. No live comparison yet.

### Client

`--gamma <value>` sets `Gw2GraphicsOptions.Gamma`, clamped by `GrGamma.Clamp`. `GrGamma.PresentScale` rounds it
through the 16-bit metric. When gamma is not 1, `GrPostProcessCombine` combines into its intermediate target and
presents through `GrScreenCopy.PresentWithGamma`. The perf overlay draws after that, so unlike the exe it is not
scaled. Maps without an environment effect have no combine pass and ignore gamma.

### Client

`--distortion` sets `Gw2PostProcessingOptions.Distortion` (off by default, as live).

- `Gw2EnvironmentDistortion` holds the environment seconds and computes the base amount and the view offset.
- `Gw2Client` reads underwater from the global water `SurfaceZ` and the amount from the water loader's below-water
  record.
- `Gw2ScreenOverlay.ApplyDistortion` gives the live amount.
- `GrPostProcessCombine` draws the `DistortedPostProcessCombine` technique over the 40 × 40 grid when the amount is
  above 0.

Known differences:

- The exe resolves the below-water record per zone. The remake uses the map's global underwater entry, like the water
  loader.
- Underwater is tested against the global water plane only, not local water bodies.

Map 1595 at 12:00 with the eye under the water: the warp moves edges by a few pixels. Above water the frame is unchanged.

## Post chain order

The remake runs light adaptation and depth blur in `GrPostProcessAdaptation` between the scene and
`GrPostProcessCombine`.

## Light adaptation

The remake blends the record by zone weight, like the other environment values (`Gw2EnvironmentLightAdaptation`,
`--light-adaptation`).
