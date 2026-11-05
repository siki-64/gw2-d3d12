# Handler-to-subsystem traces: new client

How the new client implements [Handler-to-subsystem traces](../../../gw2-research/docs/protocol/handler-to-subsystem.md). The retail behaviour and its evidence are in that note.

## Message `0x264` - per-player configured skill (208784)

The remake writes there too (`SetSlot_ProfessionMechanicLandsInTheSpecializationAfterTheSkillObject`).

The remake mirrors this with `ConfiguredSkillHandler`, `ChCliSkill.SetSlot`, `CnContext` slots 14 and 71 and a
`CnData`/`CnPack` built over the pinned content packs (`LoadedContent`). The remake's key map hashes with its own
multiplier instead of the 256-entry `HashMapUInt32_ByteHashTable` used by `HashMapUInt32_FindSlot`, registers every
index entry instead of only the namespaces `CnData_LoadPacks` selects, and leaves the pack's pointer fixups unapplied,
so pointer fields inside a found object still hold pack offsets. The notifier walk and the slot 63 call have no
listeners in the remake and are not mirrored.

## Messages `0x100` / `0x101` / `0x103` / `0x104` / `0xFF` - character health (208784)

The remake has no listeners, so it stays 1.

## Messages `0x427` / `0x424` / `0x42A` map entry and `0x53` client control (208784)

The remake keeps that byte as `Agent.Category`.

`0x424`'s trailing u32 goes to the global `MapLoad_SpawnValue` through `CoScene_SetSpawnValue` (renamed from
`CoScene_GetSpawnValue`; it is a setter) before `MsCliPlayer_BeginLoad`; the remake keeps it as `CoScene.SpawnValue`.

## Messages `0x26A` / `0x276` / profession creates / `0x1F1` - skillbar, skill recharge and profession state (208784)

The profession subclass fields and which profession each remaining create id builds; the remake constructs the base.

## Messages `0x2DF` / `0x2E1` / `0x2E3` - buff applied, duration changed and removed (208784)

The exe's hash function; the remake probes by buff id.

## Messages `0x280` / `0x281` / `0x282` - specialization, major trait and build changed (208784)

The progress tier needs `m_rewardTrackMgr`; the remake leaves it at 0.

## `CoCharSimpleCli` path (208784)

The remake's agent world is never ticked (`+0x1D4` stays 0), so end ticks never pass and timed commands run until
replaced.
