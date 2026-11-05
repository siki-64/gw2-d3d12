using Gw2.Client.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Ui;
using Gw2.Client.Hud.Professions;
using Gw2.Client.State;
using Gw2.Client.Net.Game;
using Gw2.Formats;

namespace Gw2.Client.Hud;

public sealed class HudStateModel
{
    private const int SpecializationLineCount = 3;
    private const int TraitsPerSpecializationLine = 3;
    private const float MillisecondsPerSecond = 1000f;
    private const int DefaultEnduranceMaximum = 100;
    private const int TomeCount = 3;
    private const float BreakBarPercent = 100f;

    private readonly WorldState _world;
    private readonly Func<OwnSkillBar?> _skillBar;
    private readonly Func<Hotkeys?> _hotkeys;
    private readonly ContentNames _names;
    private readonly HudFloaters _floaters = new();

    public HudStateModel(WorldState world, Func<OwnSkillBar?> skillBar, Func<Hotkeys?> hotkeys, ContentNames names)
    {
        _world = world;
        _skillBar = skillBar;
        _hotkeys = hotkeys;
        _names = names;
    }

    public HudState StateOf(uint ownAgentId, long nowMilliseconds)
    {
        var profession = (Profession?)_world.Equipment.ProfessionOf(ownAgentId);
        var health = _world.Vitals.HealthOf(ownAgentId);
        var endurance = _world.Endurance.PointsOf(ownAgentId);
        return new HudState(
            SlotsOf(ownAgentId, nowMilliseconds),
            health is { } own ? new HudBar(own.Current, own.Maximum) : null,
            endurance is { } points ? new HudBar(points, _world.Endurance.MaximumOf(ownAgentId) ?? DefaultEnduranceMaximum) : null,
            profession,
            EliteOf(ownAgentId),
            BarOf(ownAgentId, PrimaryResourceKindOf(profession), nowMilliseconds),
            _world.Skillbar.ProfessionStateOf(ownAgentId),
            _world.ProfessionResources.ResourceOf(ownAgentId, ProfessionResourceKind.ElementalistValue) is { } secondary ? (uint)secondary.ValueAt(nowMilliseconds) : null,
            BarOf(ownAgentId, CounterKindOf(profession), nowMilliseconds),
            BarOf(ownAgentId, profession == Profession.Thief ? ProfessionResourceKind.ThiefSecondaryMeter : null, nowMilliseconds),
            PetHealthOf(ownAgentId),
            PetBarrierFractionOf(ownAgentId));
    }

    public UiDebugHudState DebugStateOf(uint ownAgentId, string? ownLifeStateLabel, uint? targetAgentId, uint? hoveredAgentId, long nowMilliseconds)
    {
        var bars = new List<UiHudBar>();
        if (_world.Vitals.HealthOf(ownAgentId) is { } health)
        {
            bars.Add(new UiHudBar("Health", health.Current, health.Maximum));
            if (health.Barrier > 0f) bars.Add(new UiHudBar("Barrier", health.Barrier, health.Maximum));
        }
        if (_world.Endurance.PointsOf(ownAgentId) is { } endurance)
            bars.Add(new UiHudBar("Endurance", endurance, _world.Endurance.MaximumOf(ownAgentId) ?? DefaultEnduranceMaximum));
        foreach (var resource in _world.ProfessionResources.ResourcesOf(ownAgentId).Values) bars.Add(new UiHudBar(resource.Name, resource.ValueAt(nowMilliseconds), resource.Maximum));
        for (var kind = 0u; kind < AgentEnergies.KindCount; kind++)
            if (_world.Energies.MeterOf(ownAgentId, kind) is { Maximum: > 0 } energy) bars.Add(new UiHudBar($"Energy {kind}", energy.ValueAt(nowMilliseconds), energy.Maximum));
        return new UiDebugHudState(
            bars,
            ownLifeStateLabel,
            _world.Skillbar.ProfessionStateOf(ownAgentId) is { } professionState ? $"Profession state {professionState}" : null,
            targetAgentId is { } targetId ? _names.AgentLabelOf(targetId) : null,
            targetAgentId is { } lifeStateTargetId ? _world.LifeState.StateOf(lifeStateTargetId)?.ToString() : null,
            targetAgentId is { } healthTargetId && _world.Vitals.HealthOf(healthTargetId) is { } targetHealth ? new UiHudBar("Health", targetHealth.Current, targetHealth.Maximum) : null,
            targetAgentId is { } breakBarTargetId ? BreakBarOf(breakBarTargetId) : null,
            hoveredAgentId is { } hoveredId && hoveredId != targetAgentId ? _names.AgentLabelOf(hoveredId) : null,
            SpecializationLinesOf(ownAgentId),
            _floaters.Update(_world, ownAgentId, nowMilliseconds));
    }

    private UiHudBar? BreakBarOf(uint agentId) =>
        _world.BreakBars.BreakBarOf(agentId) is { } bar ? new UiHudBar(bar.State == BreakBarState.Active ? "Defiance" : bar.State.ToString(), bar.FractionFull * BreakBarPercent, BreakBarPercent) : null;

    private EliteSpecialization? EliteOf(uint ownAgentId)
    {
        var specializations = _world.Skillbar.SpecializationsOf(ownAgentId);
        if (specializations.Count < SpecializationLineCount || specializations[SpecializationLineCount - 1] is not (> 0 and var elite)) return null;
        return (EliteSpecialization?)_skillBar()?.Palettes.SpecializationKindOf(elite);
    }

    private HudBar? PetHealthOf(uint ownAgentId) =>
        _world.Kennels.ActivePetOf(ownAgentId) is { } pet && _world.Vitals.HealthOf(pet) is { Maximum: > 0f } health ? new HudBar(health.Current, health.Maximum) : null;

    private float PetBarrierFractionOf(uint ownAgentId) =>
        _world.Kennels.ActivePetOf(ownAgentId) is { } pet && _world.Vitals.HealthOf(pet) is { Maximum: > 0f } health ? Math.Clamp(health.Barrier / health.Maximum, 0f, 1f) : 0f;

    private HudBar? BarOf(uint ownAgentId, ProfessionResourceKind? kind, long nowMilliseconds) =>
        kind is { } barKind && _world.ProfessionResources.ResourceOf(ownAgentId, barKind) is { } bar ? new HudBar(bar.ValueAt(nowMilliseconds), bar.Maximum) : null;

    private static ProfessionResourceKind? CounterKindOf(Profession? profession) => profession switch
    {
        Profession.Necromancer => ProfessionResourceKind.NecromancerCounter,
        Profession.Thief => ProfessionResourceKind.ThiefCounter,
        Profession.Ranger => ProfessionResourceKind.RangerStacks,
        Profession.Guardian => ProfessionResourceKind.GuardianPair,
        _ => null,
    };

    private static ProfessionResourceKind? PrimaryResourceKindOf(Profession? profession) => profession switch
    {
        Profession.Warrior => ProfessionResourceKind.WarriorAdrenaline,
        Profession.Engineer => ProfessionResourceKind.EngineerMeter,
        Profession.Guardian => ProfessionResourceKind.GuardianMeter,
        Profession.Ranger => ProfessionResourceKind.RangerMeter,
        Profession.Elementalist => ProfessionResourceKind.ElementalistCounter,
        Profession.Necromancer => ProfessionResourceKind.NecromancerLifeForce,
        Profession.Revenant => ProfessionResourceKind.RevenantEnergy,
        Profession.Thief => ProfessionResourceKind.ThiefInitiative,
        Profession.Mesmer => ProfessionResourceKind.MesmerPair,
        _ => null,
    };

    private List<HudSlotState> SlotsOf(uint ownAgentId, long nowMilliseconds)
    {
        var controls = Hotkeys.WeaponSkillControls.Concat(Hotkeys.BarSkillControls).Concat(Hotkeys.MechanicControls).ToArray();
        var hotkeys = _hotkeys();
        var skillIds = new uint?[controls.Length];
        if (_skillBar() is { } bar && bar.SelectionOf(ownAgentId) is var (state, selection))
        {
            var weaponSkills = bar.WeaponSkillsOf(ownAgentId, state, selection);
            for (var index = 0; index < Hotkeys.WeaponSkillControls.Length && index < weaponSkills.Length; index++) skillIds[index] = weaponSkills[index];
            for (var index = 0; index < Hotkeys.BarSkillControls.Length; index++)
                skillIds[Hotkeys.WeaponSkillControls.Length + index] = bar.BarSkillOf(ownAgentId, index, state, selection);
            for (var index = 0; index < Hotkeys.MechanicControls.Length; index++)
                skillIds[Hotkeys.WeaponSkillControls.Length + Hotkeys.BarSkillControls.Length + index] = bar.MechanicSkillOf(state, index);
        }
        var palettes = _skillBar()?.Palettes;
        var pages = EliteOf(ownAgentId) == EliteSpecialization.Firebrand && _world.ProfessionResources.ResourceOf(ownAgentId, ProfessionResourceKind.GuardianPair) is { } pageResource ? (float?)pageResource.ValueAt(nowMilliseconds) : null;
        var slots = new List<HudSlotState>(controls.Length);
        for (var index = 0; index < controls.Length; index++)
        {
            var recharge = skillIds[index] is { } skillId ? _world.Vitals.RechargeOf(ownAgentId, skillId) : null;
            var remainingMilliseconds = recharge?.RemainingAt(nowMilliseconds) ?? 0f;
            var elapsedFraction = recharge is { DurationMilliseconds: > 0 } running ? Math.Clamp(1f - remainingMilliseconds / running.DurationMilliseconds, 0f, 1f) : 1f;
            var iconFileId = skillIds[index] is { } iconSkillId ? palettes?.IconFileIdOf(iconSkillId) : null;
            var tomeIndex = index - Hotkeys.WeaponSkillControls.Length - Hotkeys.BarSkillControls.Length;
            var lacksPages = pages is <= 0f && tomeIndex is >= 0 and < TomeCount;
            slots.Add(new HudSlotState(hotkeys?.LabelOf(controls[index]) ?? "", skillIds[index], remainingMilliseconds / MillisecondsPerSecond, iconFileId, elapsedFraction, skillIds[index] is { } chargedSkillId ? _world.Vitals.ChargesOf(ownAgentId, chargedSkillId) : null, lacksPages));
        }
        return slots;
    }

    private List<UiHudSpecializationLine> SpecializationLinesOf(uint ownAgentId)
    {
        var specializations = _world.Skillbar.SpecializationsOf(ownAgentId);
        var traits = _world.Skillbar.TraitsOf(ownAgentId);
        var lines = new List<UiHudSpecializationLine>();
        for (int line = 0; line < SpecializationLineCount && line < specializations.Count; line++)
        {
            var lineTraits = new List<string>();
            for (int tier = 0; tier < TraitsPerSpecializationLine; tier++)
            {
                int slot = line * TraitsPerSpecializationLine + tier;
                lineTraits.Add(slot < traits.Count && traits[slot] != 0 ? _names.TraitNameOf(traits[slot]) : "-");
            }
            lines.Add(new UiHudSpecializationLine(specializations[line] != 0 ? $"Specialization {specializations[line]}" : "No specialization", lineTraits));
        }
        return lines;
    }
}
