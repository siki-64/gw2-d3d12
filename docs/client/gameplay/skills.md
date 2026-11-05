# Skills: new client

How the new client implements [Skills](../../../gw2-research/docs/gameplay/skills.md). The retail behaviour and its evidence are in that note.

## Override weapon skills

The remake equips a bundle when its heal, utility or elite skill is used, replacing weapon 1–5 until the same skill is
used again or the weapon swap key is pressed.

`Gw2SkillBarState.userSuppliedEliteBuffs` holds the grant with `replacesWeaponSkills`, so the forge's weapon skills
replace weapon 1–5 until the exit.

## Skill flags

The remake lets a skill without a cast time run during a cast without interrupting it (attunement and legend swaps,
signets, shouts); a skill with a cast time waits until the cast ends.
