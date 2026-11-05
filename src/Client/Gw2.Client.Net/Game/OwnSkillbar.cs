using System.Collections.Generic;
using System.Linq;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class OwnSkillbar
{
    public const int OwnedCharacter = 0x1bb;
    public const int ProfessionState = 0x1f1;
    public const int SlotPalette = 0x264;
    public const int SpecializationBuild = 0x282;
    public const int SpecializationSelection = 0x280;
    public const int TraitSelection = 0x281;
    public const int CompanionRoster = 0x153;
    public const int SkillOverride = 0x263;
    public const int EquippedLegends = 0x1ed;
    public const int LegendSwap = 0x1ef;
    public const int SlotCount = 5;
    public const uint LandSlotSet = 0;

    private const int PlayerIdField = 1;
    private const int OwnedAgentField = 2;
    private const int StateAgentField = 1;
    private const int StateValueField = 2;
    private const int SlotPaletteIdField = 1;
    private const int SlotIndexField = 2;
    private const int SlotSetField = 3;
    private const int SlotPlayerField = 4;
    private const int BuildSpecializationsField = 2;
    private const int BuildTraitsField = 3;
    private const int SelectedSpecializationField = 2;
    private const int SelectedSpecializationLineField = 3;
    private const int SpecializationLineCount = 3;
    private const int SelectedTraitField = 2;
    private const int SelectedLineField = 3;
    private const int SelectedTierField = 4;
    private const int TiersPerLine = 3;
    private const int TraitSlotCount = 9;
    private const int CompanionAgentField = 1;
    private const int CompanionSlotField = 2;
    private const int CompanionDataIdField = 4;
    private const uint ActiveCompanionSlot = 0;
    private const uint NoCompanion = 0;
    private const int OverriddenSkillField = 1;
    private const int OverridingSkillField = 2;
    private const int OverridePlayerField = 3;
    private const int LegendAgentField = 1;
    private const int EquippedTerrestrialLegendsField = 2;
    private const int SwappedLegendsField = 4;

    private readonly Dictionary<uint, uint> _agentByPlayer = [];
    private readonly Dictionary<uint, uint> _professionStateByAgent = [];
    private readonly Dictionary<uint, uint[]> _specializationsByPlayer = [];
    private readonly Dictionary<(uint Player, uint Slot), uint> _paletteByPlayerSlot = [];
    private readonly Dictionary<uint, uint[]> _traitsByPlayer = [];
    private readonly Dictionary<uint, uint[]> _legendsByAgent = [];
    private readonly Dictionary<(uint Agent, uint Slot), uint> _companionByAgentSlot = [];
    private readonly Dictionary<(uint Player, uint Skill), uint> _overrideByPlayerSkill = [];

    public uint OverriddenSkillOf(uint agentId, uint skill) =>
        PlayerOf(agentId) is { } player && _overrideByPlayerSkill.TryGetValue((player, skill), out var overriding) ? overriding : skill;

    public uint? ActiveCompanionOf(uint agentId) =>
        _companionByAgentSlot.TryGetValue((agentId, ActiveCompanionSlot), out var companion) && companion != NoCompanion ? companion : null;

    public uint? ProfessionStateOf(uint agentId) => _professionStateByAgent.TryGetValue(agentId, out var state) ? state : null;

    public IReadOnlyList<uint> SpecializationsOf(uint agentId) =>
        PlayerOf(agentId) is { } player && _specializationsByPlayer.TryGetValue(player, out var specializations) ? specializations : [];

    public uint? SlotPaletteOf(uint agentId, int slot) =>
        PlayerOf(agentId) is { } player && _paletteByPlayerSlot.TryGetValue((player, (uint)slot), out var palette) ? palette : null;

    public IReadOnlyList<uint> TraitsOf(uint agentId) =>
        PlayerOf(agentId) is { } player && _traitsByPlayer.TryGetValue(player, out var traits) ? traits : [];

    public uint? InactiveLegendOf(uint agentId)
    {
        if (!_legendsByAgent.TryGetValue(agentId, out var legends)) return null;
        uint? active = ProfessionStateOf(agentId);
        foreach (uint legend in legends)
            if (legend != active) return legend;
        return null;
    }

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case OwnedCharacter:
                _agentByPlayer[U32(message, PlayerIdField)] = U32(message, OwnedAgentField);
                break;
            case ProfessionState:
                _professionStateByAgent[U32(message, StateAgentField)] = U32(message, StateValueField);
                break;
            case SlotPalette when U32(message, SlotSetField) == LandSlotSet:
                _paletteByPlayerSlot[(U32(message, SlotPlayerField), U32(message, SlotIndexField))] = U32(message, SlotPaletteIdField);
                break;
            case SpecializationBuild:
                _specializationsByPlayer[U32(message, PlayerIdField)] = Ids(message, BuildSpecializationsField);
                _traitsByPlayer[U32(message, PlayerIdField)] = Ids(message, BuildTraitsField);
                break;
            case SpecializationSelection:
                SelectSpecialization(U32(message, PlayerIdField), U32(message, SelectedSpecializationLineField), U32(message, SelectedSpecializationField));
                break;
            case TraitSelection:
                SelectTrait(U32(message, PlayerIdField), U32(message, SelectedLineField) * TiersPerLine + U32(message, SelectedTierField), U32(message, SelectedTraitField));
                break;
            case SkillOverride:
                _overrideByPlayerSkill[(U32(message, OverridePlayerField), U32(message, OverriddenSkillField))] = U32(message, OverridingSkillField);
                break;
            case CompanionRoster:
                _companionByAgentSlot[(U32(message, CompanionAgentField), U32(message, CompanionSlotField))] = U32(message, CompanionDataIdField);
                break;
            case EquippedLegends:
                _legendsByAgent[U32(message, LegendAgentField)] = Ids(message, EquippedTerrestrialLegendsField);
                break;
            case LegendSwap:
                _legendsByAgent[U32(message, LegendAgentField)] = Ids(message, SwappedLegendsField);
                break;
        }
    }

    private void SelectSpecialization(uint player, uint line, uint specialization)
    {
        if (line >= SpecializationLineCount) return;
        var specializations = _specializationsByPlayer.TryGetValue(player, out var known) && known.Length == SpecializationLineCount ? (uint[])known.Clone() : new uint[SpecializationLineCount];
        specializations[line] = specialization;
        _specializationsByPlayer[player] = specializations;
        for (uint tier = 0; tier < TiersPerLine; tier++)
            SelectTrait(player, line * TiersPerLine + tier, 0);
    }

    private void SelectTrait(uint player, uint slot, uint trait)
    {
        if (slot >= TraitSlotCount) return;
        if (!_traitsByPlayer.TryGetValue(player, out var traits) || traits.Length < TraitSlotCount)
            _traitsByPlayer[player] = traits = [.. traits ?? [], .. new uint[TraitSlotCount - (traits?.Length ?? 0)]];
        else
            _traitsByPlayer[player] = traits = (uint[])traits.Clone();
        traits[slot] = trait;
    }

    private static uint[] Ids(DecodedMessage message, int field) =>
        ((IReadOnlyList<DecodedField[]>)message.Fields[field].Value!).Select(entry => (uint)(ulong)entry.First(value => value.Value is ulong).Value!).ToArray();

    private uint? PlayerOf(uint agentId)
    {
        foreach (var (player, agent) in _agentByPlayer)
            if (agent == agentId) return player;
        return null;
    }

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
