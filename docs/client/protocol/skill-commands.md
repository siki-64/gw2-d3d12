# Skill and target commands (`0x17`): new client

How the new client implements [Skill and target commands (`0x17`)](../../../gw2-research/docs/protocol/skill-commands.md). The retail behaviour and its evidence are in that note.

## Reimplementation

`OwnCharacterMovement.TargetSelectionCommandOf` builds the `14` command; it matches every recorded one byte for byte
(capture 000717). `Gw2Client` sends it when Tab picks a target: the next agent by distance within 1200 units, cycling.
How the exe picks the Tab target is not traced, so that choice is the reimplementation's.

## Recording 20261102-002144

`OwnCharacterMovement` builds `0xe1`, the targeted `2B` and `0A` byte for byte; `Gw2Client` no longer sends `14` on
Tab.

`OwnCharacterMovement` builds the ground sequence of capture 000717 byte for byte: `2A`, `2B` (agent 0, point), `25`,
`2B`, `28` with command flags 0, sequence gaps 1, 2, 1, 1. `Gw2Client` takes the point from the cursor ray against
collision and terrain when the skill has the ground-targeted flag. The first `2B` in the recording holds the reticle
from the frame before (a slightly different point); the reimplementation repeats the cast point.

## Held ground skill (recording 20261102-002144, key 3, skill 72938 from slot 7)

`OwnCharacterMovement` builds this cadence from `OwnMovementInput.HeldGroundReticleInGameUnits`, which `Gw2Client`
fills with the cursor ground point while the casting key stays down; the builder in the exe is not traced.

## Heal, utility and elite slots (recording 20261102-002144, keys 6–0)

`Gw2SkillPalettes.PaletteSkill` resolves the five slots (test against the frozen `.dat`), `OwnSkillbar` keeps `0x1BB`,
`0x1F1`, `0x264` (land set 0) and `0x282`, and `Gw2Client` sends keys 6–0 as slots 0–4 without a target.

## Profession mechanic slots (F1–F5)

`OwnSkillbar` keeps legends, buffs (`0x2DF`, `0x2E2` by skill, `0x2E3` by instance) and traits.
