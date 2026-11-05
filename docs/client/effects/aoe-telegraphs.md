# AoE telegraphs: new client

How the new client implements [AoE telegraphs](../../../gw2-research/docs/effects/aoe-telegraphs.md). The retail behaviour and its evidence are in that note.

## Remake

`GW2Import > Import Skill Effects` also imports model `0xD226` (`Gw2TelegraphIntegrator.TelegraphModelFileId`, the model the
telegraph effects reference) and writes `Gw2TelegraphSet`: the mesh bound to `AreaSA` (ally) and to `AreaSE`
(enemy) with their UV0 pattern and UV1 perturbation textures, the mesh radius (120 u), `modxcol`, `difptrb`,
`depdist` and `depptrb` from the material, and the start and end clip lengths from the compound animation.
`GW2/AoE Telegraph` reproduces the pixel shader above; `Gw2TelegraphCustomPass` draws it after post-processing with
the camera depth bound as a texture. `Gw2TelegraphTimeline` stands in for the clips: `diffade` rises linearly from 0
to 1 over the start clip (the track is not decoded), stays 1, and the telegraph hides after the end clip.
`Gw2Telegraphs` scales the sphere to the skill's radius / 120. `Gw2PlayerAttacks` shows the ally telegraph when a
skill with a radius is activated, at the ground target, the selected target (skills with a range) or the player,
and ends it when the cast executes. `fxclr` is the model's per-mesh draw colour: the effect system only changes it through kind 5 material effects ([`effects.md`](../../../gw2-research/docs/effects/effects.md)), so a telegraph keeps the mesh's stored colour under the default effect key `183330`. Where that stored colour is first written is not found yet. Not yet: the character mask (`t14`), and enemy telegraphs for NPC
attacks.

`GW2Import > Import Skill Effects` marks model effects whose model is `0xD226` as telegraphs instead of importing
the model as a prop. `Gw2SkillEffects` sends them to `Gw2Telegraphs` with the skill's radius, and
`Gw2PlayerAttacks` shows its own telegraph only for skills without a telegraph among their activation effects.
