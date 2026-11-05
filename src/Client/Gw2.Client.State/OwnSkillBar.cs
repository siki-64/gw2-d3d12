using System.Linq;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.State;

public sealed class OwnSkillBar
{
    public const int FirstWeaponBarSlot = 5;

    private readonly WorldState _world;
    private readonly Gw2OwnSkills _ownSkills;
    private readonly Gw2Transformations _transformations;

    public OwnSkillBar(Gw2ContentIndex index, WorldState world)
    {
        _world = world;
        Palettes = new Gw2SkillPalettes(index, SkillPaletteContentLayout.Current);
        _ownSkills = new Gw2OwnSkills(Palettes, new Gw2Items(index, ItemContentLayout.Current), LegendSkillPaletteLayout.Current, ProfessionMechanicPaletteLayout.Current);
        _transformations = new Gw2Transformations(index, TransformationContentLayout.Current, ItemContentLayout.Current, SkillPaletteContentLayout.Current);
    }

    public Gw2SkillPalettes Palettes { get; }

    public (OwnSkillState State, SkillSelection Selection)? SelectionOf(uint ownAgentId)
    {
        if (_world.Equipment.ProfessionOf(ownAgentId) is not { } profession) return null;
        var skillbar = _world.Skillbar;
        var (mainHand, offHand) = _world.Equipment.ActiveWeaponItemDataIdsOf(ownAgentId);
        var state = new OwnSkillState(
            (Profession)profession,
            mainHand,
            offHand,
            skillbar.ProfessionStateOf(ownAgentId),
            skillbar.InactiveLegendOf(ownAgentId),
            skillbar.SpecializationsOf(ownAgentId),
            skillbar.TraitsOf(ownAgentId).ToArray(),
            skillbar.BuffSkillsOf(ownAgentId),
            slot => skillbar.SlotPaletteOf(ownAgentId, slot),
            skillbar.ActiveCompanionOf(ownAgentId),
            skill => skillbar.OverriddenSkillOf(ownAgentId, skill));
        return (state, _ownSkills.SelectionOf(state));
    }

    public uint?[] WeaponSkillsOf(uint ownAgentId, OwnSkillState state, SkillSelection selection)
    {
        bool downed = _world.LifeState.StateOf(ownAgentId) == Gw2.Client.Net.Game.LifeState.Downed;
        if (!downed && _ownSkills.WholeBarMechanicPaletteOf(state) is { } wholeBarPalette)
            return Enumerable.Range(0, Gw2SkillPalettes.WeaponSkillCount).Select(slot => Palettes.BarSlotSkill(wholeBarPalette, FirstWeaponBarSlot + slot, selection)).ToArray();
        if (downed) return Palettes.DownedWeaponSkills(CommonSkillPaletteLayout.Current, selection, ActiveBundleOf(ownAgentId));
        var (mainHand, offHand) = _world.Equipment.ActiveWeaponItemDataIdsOf(ownAgentId);
        if (mainHand is { } mainHandItem && _transformations.WeaponPaletteDataIdOfItem(mainHandItem) is { } mainHandPalette)
            return Palettes.ItemWeaponSkills(mainHandPalette, offHand is { } offHandItem ? _transformations.WeaponPaletteDataIdOfItem(offHandItem) : null, selection);
        return Palettes.WeaponSkills(selection);
    }

    public uint? BarSkillOf(uint ownAgentId, int barSlot, OwnSkillState state, SkillSelection selection)
    {
        if (_ownSkills.WholeBarMechanicPaletteOf(state) is { } wholeBarPalette) return Palettes.BarSlotSkill(wholeBarPalette, barSlot, selection);
        if (ActiveBundleOf(ownAgentId)?.PaletteDataId is { } transformationPalette && Gw2SkillPalettes.TransformationReplacesBarSlot(barSlot))
            return Palettes.BarSlotSkill(transformationPalette, barSlot, selection);
        return _world.Skillbar.SlotPaletteOf(ownAgentId, barSlot) is { } palette ? Palettes.PaletteSkill(palette, selection) : null;
    }

    public uint? MechanicSkillOf(OwnSkillState state, int mechanicSlot) => _ownSkills.MechanicSkill(state, mechanicSlot);

    private Gw2Bundle? ActiveBundleOf(uint ownAgentId) =>
        _world.Transformations.ActiveTransformationOf(ownAgentId) is { } transformation ? _transformations.BundleOf(transformation) : null;
}
