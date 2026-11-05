# Nameplates (infobars): new client

How the new client implements [Nameplates (infobars)](../../../gw2-research/docs/ui/nameplates.md). The retail behaviour and its evidence are in that note.

## Species name

The remake drops grammar tags such as `[f]`, `[s]` and `[null]` and keeps `[lbracket]` and `[rbracket]` as brackets
(`Gw2TextTemplate.WithoutTags`); the executable's text formatter is not traced.

## In the remake

`Gw2AgentNameLabels` projects each character's anchor and draws the name as an overlay, depth-tested against the
scene, in white until the name style is traced. The name is centred on the anchor with its bottom raised by 32 px;
the `−27` x offset needs the pivot first.

`Gw2NameplateHealthBars` draws the health bar for every character with a health maximum, except the controlled
one, within nameplate range. The bar is 81 × 5.25 px (the 216 × 14 bar at the nameplate scale `0.375`), its top-left snapped to a whole pixel so the frame samples the same way every frame, centred
under the name, with the 256 × 32 artwork placed so its bar rect `15, 7 – 231, 21` covers it. The frame `0x262FD`
uses shader `3` tinted with the secondary colour, and the fill uses shader `0x1C` with the colour `0x262FE`, the
ramp `0x262FF` and the window `(0, health)`, tinted with the primary colour. Colours follow the visible attitude:
hostile and indifferent use the hostile pair, the rest the friendly pair, each with its downed variant. The bar
uses the same scene-depth test as the name (`GrUiQuads` with a scene depth).

Not yet mirrored: the fades and distance targets, the barrier layer, the second bar, the large boss bar, the
low-health blend of the controlled character, shroud colours and the colour-blind option.
