# Agent lifecycle (AgWorld spawn/despawn) — scope: new client

How the new client implements [Agent lifecycle (AgWorld spawn/despawn) — scope](../../../gw2-research/docs/protocol/agent-lifecycle.md). The retail behaviour and its evidence are in that note.

## How the exe applies a `0x39`

`GameAgents` mirrors this: it places the agent at the sent position, moves it by field 7 × ground speed (sent, else
previous, else run speed) plus the sent climb rate for `server step now − sent step`, capped at 1 s, and keeps walking
it along that velocity.

## Agent types

The remake mirrors type, category and attitude as `Gw2AgentType`, `Gw2AgentCategory` and `Gw2AgentAttitude` (listed in
[`layout-mirrors.md`](../../../gw2-research/docs/engine/layout-mirrors.md)), marks players with `PlayerListIndex` and keeps health and barrier
as floats in `src/Client/Gw2.Client.World/`.

## Keyframed and dynamic agents

`GameAgents` keeps one table per category, the way the create message ids split them: characters from `0x25`,
dynamic agents from `0x26` (`GameDynamicAgent`: type, position, collision radius) and keyframed agents from `0x28`
(`GameKeyframedAgent`: type, flags, position, rotation, scale, model file, collision radius). The factory index is the
`GameAgentType` (10 gadget, 11 attack target, 15 item). `0x29` removes an agent from every table.

The `0x28` position and rotation are collision space. The position becomes game units like `0x39`
(`x·32, y·32, −z·32`), and the quaternion is reflected through the same z flip to `(−x, −y, z, w)`. Scale 0 becomes 1
when no model file is sent. The model file (field 18) is a two-character file reference.

The client attaches each keyframed agent's model through `Gw2PropLoader.AttachModel` and places it at
scale · rotation · position, detaching it when the agent is removed. Every gadget stands at its wire transform, which is
the exe's static path; the keyframed path (`CoKeyframed_Advance`, field 10 with a clear sign bit in field 9) is not
ported. Attack targets carry no model and are not drawn. The `0x35a` gadget data, the create blob and gadget picking are
open.

Items (type 15) carry no model. `GameKeyframedAgent.ContentInstanceId` is field 7, the item instance;
`AgentEquipment.ItemDataIdOfInstance` maps it to the definition from `0x3cb`, and `ContentNames.ItemNameOf` reads the
name through `Gw2Items.NameTextIdOf` (`ItemContentLayout.NameTextOffset`, read from the exe). The client draws the
name in yellow (name attitude 8) when the controlled character is within 575 units, the item nameplate's zero-at distance;
the fade to 500 and the definition flag `0x400` that hides a nameplate are not ported.

Gadgets (type 10) carry a 111-byte struct in field 5, or a longer blob that starts with it. `GameAgents` reads it into
`GadgetCreate`: the two flag words at bytes 17 and 25, the name text id at 73 and the coded name at 81.
`GadgetCreate.HasNameplate` is the exe's `AgentShouldHaveInfoBar` test, and `ContentNames.GadgetNameOf` resolves the
text id, or the coded name as for NPCs. The client draws these names in yellow (name attitude 8) within the 5000-unit
nameplate range. Gadget attitude, and the guild and context templated names (flags `0x200000`, `0x400000`), are not
ported.
