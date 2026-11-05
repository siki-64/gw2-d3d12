# Name labels

The retail nameplates are in [nameplates.md](../../gw2-research/docs/ui/nameplates.md). Until the client has its
own HUD, `Gw2AgentNameLabels` draws a stand-in: each agent from the game connection gets its name in its name colour
(`Gw2NameColors`, from the visible attitude), centred 32 px above the top of its capsule and not for the controlled character. Names and health bars fade
with the distance from the controlled character as in retail: full at 3200 units and gone at 3500, or full at 4700
and gone at 5000 for the target. The other emphasis cases (hostile in combat, flag `4`) are not read yet. Names have
the 3×3 halo of text effect `8`: eight black copies at half opacity one pixel around the name. It uses font 0, variant 0, the smallest size, resolves coded names in English from the
archive, and clips each name per pixel where the scene depth is nearer than its anchor (`GrNameLabel.hlsl`), matched
live. It does not read party or squad membership yet, so their names are clipped too.
