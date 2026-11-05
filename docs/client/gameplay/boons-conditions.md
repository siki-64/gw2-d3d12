# Effects: boons and conditions: new client

How the new client implements [Effects: boons and conditions](../../../gw2-research/docs/gameplay/boons-conditions.md). The retail behaviour and its evidence are in that note.

## Buff block

The remake keeps `0x08` where it is set (Stability 25, Regeneration 5, damaging conditions 1,500, duration boons 99)
and uses a user-supplied limit where it is `0` (Might and Vulnerability: 25 on the wiki).

## Effects applied by skills

The remake takes the first function of each without a required trait (`SkillEffectBlockView.TargetCount`,
`RadiusInGameUnits`).

## Remake

`BuffView` decodes the block and `EffectCatalog` picks the shared definitions. Only rows without
requirements are used. The effect bar copies the stack count rule and the expiry blink of `AsBuff_DrawIcon`
(`sinf` is `sinf`); its position, the pop-in animation (350 ms after a stack arrives), the
boon and condition frame with its remaining-duration fill and the 85 % icon inset are not copied yet.
Game state lives in `Gw2.Client.World`; see the client README.
