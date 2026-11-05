# Character composite: new client

How the new client implements [Character composite](../../../gw2-research/docs/models/composite.md). The retail behaviour and its evidence are in that note.

## High-resolution texture files

The remake decodes the table in `Gw2HighResolutionFiles`, from a frozen copy of `Local.dat`
(`research/208784/Local-208784.dat`, or the installed `%APPDATA%\Guild Wars 2\Local.dat`).

## NPC composite variants

`AgentCompositeIdentities` (`Gw2.Client.Net`) keeps the race token from `0x179` field 4 and the appearance token from
`0x183` per agent. Agents with a race token and without a `0xB7` block are drawn with `CharacterScene.OfCompositeIdentity`. Without an appearance token (no `0x183`, as for Risen Corpses) they get the race's default body with their equipped armour, like the exe's `sub_1412D58D0` when `CpsComposite +2632` is 0.

- The variant's components replace the parts of their types.
- The bare body (types 0-3) fills the types the variant leaves out.
- Armour component colours are the part dyes, and the variant colours are the skin, hair, eye and pattern shifts.

These agents get no species model.
