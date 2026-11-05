# Skill bar: new client

How the new client implements [Skill bar](../../../gw2-research/docs/ui/skill-bar.md). The retail behaviour and its evidence are in that note.

## Root layout

The placement of the two roots relative to the health orb is set by the HUD, not by `SbRoot`, and is not decoded; the
remake puts the weapon row against the orb's left edge and the utility row against its right.

## Warrior adrenaline

The remake draws the widget part; its F-controls are the skill bar's.

The remake draws the widget (`Gw2AdrenalineBar`) centred above the orb, since its placement is not decoded; shaders
`0x10` and `0x20` are drawn as plain colour and colour × mask.

## Necromancer life force

The remake draws all three layouts (`Gw2LifeForceBar`) centred above the orb; the Scourge's count is server state, so
it shows `maximumCharges` of `userSuppliedEliteBuffs` (1) full.

## Revenant energy

Shaders `0x20` and `0x23` are not decoded: the remake draws both fills as colour × bar mask (`DrawMaskedRegion`), the
second at half alpha, and leaves out the icon and the percentage text, whose frame position is not decoded. Offline,
the rate is the resource's rate per second and the legend is `Gw2SkillBarState.professionState`.

## Mesmer clones

The remake draws the widget (`Gw2CloneOrbs`) centred above the orb.

## Thief initiative

The remake draws all three layouts.

The remake draws the core layout (`Gw2InitiativePips`) centred above the orb, like the adrenaline bar.

## Remake

`GW2Import > Import Skill Bars` resolves each profession's default bar with `SkillCatalog` (see
[`skills.md`](../../../gw2-research/docs/gameplay/skills.md)), imports the skill icons as UI textures and writes `Gw2SkillBarSet`
per profession: weapon 1–5 for every weapon the profession can hold (see
[weapon hands](../../../gw2-research/docs/gameplay/skills.md#weapon-hands)), and heal, utilities and elite. `Gw2SkillBarState.profession`,
`mainHandWeapon` and `offHandWeapon` pick the bar. Weapon 1–5 are stored per main and off hand pair
(off hand `25` unarmed for none, `24` any after a two-handed weapon), because slot 3 can depend on both
hands (thief dual skills). `Gw2Hud.skillKeys` (1–0 by default) start a
slot's recharge sweep with the recharge for `Gw2SkillBarState.gameMode`; recharges run per skill, so they survive weapon
swaps. Charges (ammo) are imported by the decoder but not used yet.

The import stores bars per profession state (`SkillCatalog.ProfessionStates`: attunements, legends,
warrior and ranger states, plus "none" when that state resolves a heal and weapon 1). `Gw2SkillBarState.professionState`
picks one; the picker steps through them. The profession row (F1–F7, keys F1–F7) sits above the weapon row
and hides slots without a skill; its placement relative to the orb is not decoded. The weapon swap key
(`` ` ``) swaps the main and off hand with `alternateMainHandWeapon`/`alternateOffHandWeapon`. Engineer F1–F5 are the toolbelt
skills of heal, utilities and elite (`SkillView.ToolbeltSkillDataId`, ability `+0xB8`), used when a
profession resolves no F-skills of its own; kits have none there yet. Professions with several mechanic palettes that hold F1 entries (revenant legends, ranger pets) store an
F-row per palette; `Gw2SkillBarState.mechanicPaletteDataId` picks one (the picker's Mechanic row), and selecting a state selects the
palette that alone uses it (revenant legends). `alternateProfessionState` is the second equipped legend: F1
swaps the two after `professionStateSwapRechargeSeconds` (9 s, user-supplied), with the sweep on F1. F-slots beyond the widget's own count
(`Gw2ProfessionWidgetSlots`: guardian, warrior, thief, revenant 4; engineer, mesmer 5) are dropped. The weapon
swap waits `weaponSwapRechargeSeconds` (9 s, a user-supplied value: the swap cooldown widget reads it from
runtime skill bar state, vtable `+0x138`/`+0x130`). Profession resources are `Gw2TimedResource` (the server's `{value, maximum, rate, timestamp}`, see
[profession resources](../../../gw2-research/docs/gameplay/skills.md#profession-resources)); skills spend their ability `+0x4C` cost on
activation. Until captured resource messages are decoded, `Gw2SkillBarState.userSuppliedProfessionResources` holds
user-supplied values: warrior adrenaline 30 (gained per hit; with no combat yet, each weapon skill use counts as one hit worth 1, `gainPerWeaponSkillHit`), revenant energy 100 at +10/s starting at 50, thief
initiative 12 at +1/s, necromancer life force 69 % of health starting at 0, mesmer 3 clones; elementalist,
engineer, ranger and guardian have none. `Gw2Hud.ApplyProfessionResource` takes a resource from a server
message in their place. Not yet in the remake: forge bars and kits, gaining resources from hits and upkeep. The necromancer's shroud replaces weapon 1–5 while in state 7 (see [override weapon skills](../../../gw2-research/docs/gameplay/skills.md#override-weapon-skills)); a state with its own weapon palette is baked as one weapon set with main and off hand `24`.

The import also stores every state per elite specialization (`EliteSpecializationBar`, resolved with the
elite's traits and required specialization) and, per state, profession-slot variants by main-hand weapon and
by required buff (`ProfessionSlotVariant`) where they differ from the default. `Gw2SkillBarState.eliteSpecializationDataId`
selects the elite (the picker's Elite row). The warrior's state follows adrenaline: full bars 1–3 select
states 9–11 (`firstResourceLevelState`, `resourceLevelCount`), and a cost-free F1 spends all of it
(`costFreeProfessionSkillSpendsAll`); both stand in for the server. Buffs are server state too:
`Gw2SkillBarState.userSuppliedEliteBuffs` gives the Berserker the hidden buff 24266 while idle, and Berserk (58801 or
45193) grants 4737 for 20 s in PvE and WvW and 15 s in PvP until End Berserk (5508). The durations are
user-supplied; WvW follows PvE. The Bladesworn (22) uses flow instead of adrenaline (100, gained 1 per weapon
hit; an elite entry in `userSuppliedProfessionResources`), Gunsaber (15469) grants buff 21595 until Sheathe
(44345) with no duration, and the charge maximum is 0 until known, so no pips show; charges and buff 38423 are
not modelled. Dragon Trigger (F2, buff 81934) and the gunsaber's weapon palette stay server state. Each profession
starts with a weapon checked against the API: guardian, warrior and mesmer greatsword, engineer rifle,
ranger longbow, thief dagger/dagger, elementalist staff in fire, necromancer and revenant staff. Revenant heal,
utility and elite slots stay empty until the starting legend is decoded.

The skill bar rules live in `Gw2SkillBarState` (`src/Client/Gw2.Client.Model`, no engine references): selection,
recharges, weapon and legend swaps, bundles, profession state toggles, elite buffs and the profession resource.
`Gw2Hud.skillBar` holds one; the HUD reads keys, passes the time and `Gw2SkillBarSet` (as `IGw2SkillBarLayout`)
and draws what the state resolves. The user-supplied server stand-ins are `Gw2SkillBarUserSuppliedValues`.
`Gw2SkillBarStateTests` (`tests/Client/Gw2.Client.Model.Tests`) cover these rules outside the editor.

## Cast bar

`HudCastBar` draws the warmup bar of the exe's skill progress root (research: `gw2-research/docs/ui/skill-bar.md`,
"Segment quads" and "Driving the bar"). It is a 200×50 control. Each layer is one quad, using the same rects, texture
transforms and per-mode alphas as the exe.

- **Timing**: `Gw2SkillCastBar` reads the ability's sequence and variant tokens, its hide-bar flag and its loop step
  counts; the offsets come from `SkillCastBarLayout`, which reads them from the exe. `TimingOf` sums the steps like
  `SkpSkillTiming` and picks the warmup, channel or charge widget; interact bars are not drawn.
- **Segments**: `Gw2CastProgress` holds the exe's segments (one for the warmup, two for a channel, one per charge
  stage), each with its layout span, style and mode. `HudCastBar` draws each segment in its span with the style's
  masks; style 0 fills with `0x26601` and keeps its fill when interrupted.
- **Driving**: the client calls `Begin` when it sends a new `OwnSkillUse`. `AgentSkillOrders` follows the server's
  skill order (`0x23`) and its agent events: `0x44` charge levels advance the charge stages, and `0x45` stops the bar,
  interrupting a warmup that is still casting. The `0x340` cancel and the `0x3B0` charge release are not wired, and
  skill rate (quickness, slow) is not applied.
- **Opacity**: the bar fades in over 0.25 s. Charges fade in over 0.15 s. 0.15 s after the bar ends, it fades out over 0.4 s. The exe's curve types
  are not decoded, so all fades are linear.
- **Placement**: a stand-in. The SkpRoot layout (id 200020) is not decoded, so the bar is centred 4 px above the
  profession panel.
- **Blending**: `GrUiQuads` uses the exe's modulate-twice blend for UI shaders 43, 47 and 48 (the glow), and keeps
  non-premultiplied alpha for every other shader.
