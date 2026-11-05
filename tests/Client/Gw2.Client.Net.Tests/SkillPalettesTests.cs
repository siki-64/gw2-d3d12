using System;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class SkillPalettesTests
{
    [TestMethod]
    public void ResolvesTheWeaponSkillsOfTheRecordedRevenant()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);

        var swordAndShield = palettes.WeaponSkills(new SkillSelection(Profession.Revenant, WeaponType.Sword, WeaponType.Shield));
        var hammer = palettes.WeaponSkills(new SkillSelection(Profession.Revenant, WeaponType.Hammer, WeaponType.Any));
        var greatsword = palettes.WeaponSkills(new SkillSelection(Profession.Guardian, WeaponType.Greatsword, WeaponType.Any));

        CollectionAssert.AreEqual(new uint?[] { 29057, 29233, 26699, 29386, 28262 }, swordAndShield);
        CollectionAssert.AreEqual(new uint?[] { 28549, 28253, 27976, 27665, 28110 }, hammer);
        var gleamingHerald = new SkillSelection(Profession.Revenant, WeaponType.Hammer, WeaponType.Any, ProfessionState: 15, EliteSpecializationDataId: 74);
        CollectionAssert.AreEqual(new uint?[] { 27220, 28379, 27014, 26644, 27760 }, new uint[] { 4572, 4614, 4651, 4564, 4554 }.Select(palette => palettes.PaletteSkill(palette, gleamingHerald)).ToArray());
        var legends = LegendSkillPaletteLayout.Current;
        Assert.AreEqual(4610u, palettes.LegendMechanicPaletteDataId(legends, 15));
        Assert.AreEqual(4592u, palettes.LegendMechanicPaletteDataId(legends, 16));
        var shiroHerald = gleamingHerald with { ProfessionState = 16, ActiveBuffDataIds = [29275] };
        Assert.AreEqual(29371u, palettes.PaletteSkill(4610, gleamingHerald, 10));
        Assert.AreEqual(28134u, palettes.PaletteSkill(4592, gleamingHerald, 9));
        Assert.AreEqual(51667u, palettes.PaletteSkill(4592, shiroHerald, 10));
        Assert.IsTrue(palettes.IsGroundTargeted(27976));
        Assert.IsFalse(palettes.IsGroundTargeted(27665));
        Assert.IsTrue(palettes.IsGroundTargeted(28110));
        CollectionAssert.AreEqual(new uint?[] { 9137, 9081, 9080, 9146, 9147 }, greatsword);
    }

    [TestMethod]
    public void ResolvesTheDownedSkillsFromTheCommonDownedPalette()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        var common = CommonSkillPaletteLayout.Current;

        Assert.AreEqual(new Guid("c363b7df-4b73-4f78-a43f-fbba936af8f1"), common.DownedPaletteKey);
        Assert.AreEqual(111u, palettes.DownedPaletteDataId(common));
        var warriorDowned = palettes.DownedWeaponSkills(common, new SkillSelection(Profession.Warrior, WeaponType.Greatsword, WeaponType.Any));
        CollectionAssert.AreEqual(new uint?[] { 14390, 14515, 14391, 1175, 37873 }, warriorDowned);
    }

    [TestMethod]
    public void ResolvesTheLiveBundlesFromTheirTransformations()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        var transformations = new Gw2Transformations(reader.ContentIndex, TransformationContentLayout.Current, ItemContentLayout.Current, SkillPaletteContentLayout.Current);
        var warrior = new SkillSelection(Profession.Warrior, WeaponType.Greatsword, WeaponType.Any);

        var recordedBundle = transformations.BundleOf(2728)!;
        Assert.AreEqual(new Gw2Bundle(82883, null, 5640, null, 5801, null), recordedBundle);
        Assert.AreEqual(41253u, palettes.ItemWeaponSkills(transformations.WeaponPaletteDataIdOfItem(82883)!.Value, null, warrior with { ActiveBuffDataIds = [79255] })[0]);
        CollectionAssert.AreEqual(new uint?[] { 73766, 73836, 73984, 74331, null }, palettes.ItemWeaponSkills(transformations.BundleOf(844)!.MainHandPaletteDataId!.Value, null, warrior));
        var photonForge = palettes.ItemWeaponSkills(transformations.WeaponPaletteDataIdOfItem(84579)!.Value, null, new SkillSelection(Profession.Engineer, WeaponType.Rifle, WeaponType.Any, ProfessionState: 5, EliteSpecializationDataId: 57));
        CollectionAssert.AreEqual(new uint?[] { 44588, 42965, 44530, 45783, 42521 }, photonForge);
        Assert.AreEqual(84579u, transformations.BundleOf(759)!.MainHandItemDataId);
        var actionBar = transformations.BundleOf(2659)!;
        Assert.IsNull(actionBar.MainHandPaletteDataId);
        CollectionAssert.AreEqual(new uint?[] { 78218, 79198, 77839, 21699, 10586 }, Enumerable.Range(0, 5).Select(slot => palettes.BarSlotSkill(actionBar.PaletteDataId!.Value, slot, warrior)).ToArray());
        Assert.IsTrue(Gw2SkillPalettes.TransformationReplacesBarSlot(4));
        Assert.IsFalse(Gw2SkillPalettes.TransformationReplacesBarSlot(5));
    }

    [TestMethod]
    public void ResolvesTheShroudBarFromTheMechanicPalette()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        var ownSkills = new Gw2OwnSkills(palettes, new Gw2Items(reader.ContentIndex, ItemContentLayout.Current), LegendSkillPaletteLayout.Current, ProfessionMechanicPaletteLayout.Current);
        var shroud = new OwnSkillState(Profession.Necromancer, null, null, Gw2OwnSkills.MechanicPaletteFillsWholeBarState, null, [], [], [], _ => null);
        var selection = ownSkills.SelectionOf(shroud);

        var mechanicPalette = ownSkills.WholeBarMechanicPaletteOf(shroud)!.Value;
        Assert.IsNull(ownSkills.WholeBarMechanicPaletteOf(shroud with { ProfessionState = null }));
        var bar = Enumerable.Range(0, 10).Select(slot => palettes.BarSlotSkill(mechanicPalette, slot, selection)).ToArray();
        Assert.AreEqual(234u, mechanicPalette);
        CollectionAssert.AreEqual(new uint?[] { null, null, null, null, null, 10554, 10604, 10588, 10594, 19504 }, bar);
    }

    [TestMethod]
    public void ResolvesTheCoreAndEliteProfessionMechanicSkills()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        uint?[] MechanicSkills(Profession profession, WeaponType weapon, uint state = SkillSelection.NoProfessionState, uint? elite = null)
        {
            var palette = palettes.ProfessionMechanicPaletteDataId(LegendSkillPaletteLayout.Current, ProfessionMechanicPaletteLayout.Current, profession)!.Value;
            var selection = new SkillSelection(profession, weapon, WeaponType.Any, ProfessionState: state, EliteSpecializationDataId: elite);
            return Enumerable.Range(9, 5).Select(kind => palettes.PaletteSkill(palette, selection, (uint)kind)).ToArray();
        }

        CollectionAssert.AreEqual(new uint?[] { 9115, 9120, 9118, null, null }, MechanicSkills(Profession.Guardian, WeaponType.Greatsword));
        CollectionAssert.AreEqual(new uint?[] { 29887, 30083, 30029, null, null }, MechanicSkills(Profession.Guardian, WeaponType.Greatsword, elite: 36));
        CollectionAssert.AreEqual(new uint?[] { 44364, 41780, 42259, null, null }, MechanicSkills(Profession.Guardian, WeaponType.Greatsword, elite: 76));
        CollectionAssert.AreEqual(new uint?[] { 14375, null, null, null, null }, MechanicSkills(Profession.Warrior, WeaponType.Greatsword));
        CollectionAssert.AreEqual(new uint?[] { 13014, null, null, null, null }, MechanicSkills(Profession.Thief, WeaponType.Dagger));
        CollectionAssert.AreEqual(new uint?[] { 43390, null, null, null, null }, MechanicSkills(Profession.Thief, WeaponType.Dagger, elite: 48));
        CollectionAssert.AreEqual(new uint?[] { 5492, 5493, 5494, 5495, null }, MechanicSkills(Profession.Elementalist, WeaponType.Staff, 1));
        CollectionAssert.AreEqual(new uint?[] { 5492, 5493, 5494, 5495, 63396 }, MechanicSkills(Profession.Elementalist, WeaponType.Staff, 1, elite: 9));
        CollectionAssert.AreEqual(new uint?[] { 5492, 5493, 5494, 5495, 63472 }, MechanicSkills(Profession.Elementalist, WeaponType.Staff, 4, elite: 9));
        CollectionAssert.AreEqual(new uint?[] { 5492, 5493, 5494, 5495, null }, MechanicSkills(Profession.Elementalist, WeaponType.Staff, 1, elite: 30));
        CollectionAssert.AreEqual(new uint?[] { 5492, 5493, 5494, 5495, 76643 }, MechanicSkills(Profession.Elementalist, WeaponType.Staff, 1, elite: 68));
        CollectionAssert.AreEqual(new uint?[] { 10191, 10190, 10287, 10192, null }, MechanicSkills(Profession.Mesmer, WeaponType.Greatsword));
        CollectionAssert.AreEqual(new uint?[] { 10574, null, null, null, null }, MechanicSkills(Profession.Necromancer, WeaponType.Staff));
        CollectionAssert.AreEqual(new uint?[] { 30792, null, null, null, null }, MechanicSkills(Profession.Necromancer, WeaponType.Staff, elite: 45));
        CollectionAssert.AreEqual(new uint?[] { 44946, 40813, 43448, 44428, 44663 }, MechanicSkills(Profession.Necromancer, WeaponType.Staff, elite: 70));
    }

    [TestMethod]
    public void ResolvesTheEngineerToolbeltFromTheBarSkills()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        var engineer = new SkillSelection(Profession.Engineer, WeaponType.Rifle, WeaponType.Any);
        Assert.AreEqual(6176u, palettes.ToolbeltSkill(5857, engineer));
        Assert.AreEqual(6172u, palettes.ToolbeltSkill(5805, engineer));
        Assert.AreEqual(5824u, palettes.ToolbeltSkill(5812, engineer));
        Assert.AreEqual(29505u, palettes.ToolbeltSkill(30357, engineer));
        Assert.AreEqual(29716u, palettes.ToolbeltSkill(5868, engineer));
        Assert.IsNull(palettes.ToolbeltSkill(9137, engineer));
    }

    [TestMethod]
    public void ResolvesTheAllianceStanceFromItsBuffs()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        SkillSelection Alliance(params uint[] buffs) => new(Profession.Revenant, WeaponType.Sword, WeaponType.Any, ProfessionState: 21, ActiveBuffDataIds: buffs, SelectedSpecializationDataIds: [18, 11, 24]);

        Assert.AreEqual(62942u, palettes.PaletteSkill(4554, Alliance()));
        Assert.AreEqual(62687u, palettes.PaletteSkill(4554, Alliance(62935)));
        Assert.AreEqual(62738u, palettes.PaletteSkill(4554, Alliance(62864)));
        Assert.AreEqual(0x64, TraitContentLayout.Current.NameTextOffset);
        var traitName = new Gw2Traits(reader.ContentIndex, SkillPaletteContentLayout.Current, TraitContentLayout.Current).NameTextIdOf(1660);
        Assert.IsNotNull(traitName);
    }

    [TestMethod]
    public void ResolvesEquippedItemPalettesThatRequireACoreSpecialization()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        var warrior = new SkillSelection(Profession.Warrior, WeaponType.Any, WeaponType.Any);

        CollectionAssert.AreEqual(new uint?[] { null, null, null, null, null }, palettes.ItemWeaponSkills(6899, null, warrior));
        CollectionAssert.AreEqual(new uint?[] { 62966, 62930, 62732, 62789, 62885 }, palettes.ItemWeaponSkills(6899, null, warrior with { SelectedSpecializationDataIds = [4, 22, 18] }));
    }

    [TestMethod]
    public void ReadsTheSpecializationKindsFromTheSpecializationRecords()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);

        Assert.AreEqual(22u, palettes.SpecializationKindOf(7));
        Assert.AreEqual(6u, palettes.SpecializationKindOf(4));
        Assert.AreEqual(41u, palettes.SpecializationKindOf(3));
        Assert.AreEqual(18u, palettes.SpecializationKindOf(25));
        Assert.AreEqual(14u, palettes.SpecializationKindOf(41));
        Assert.IsNull(palettes.SpecializationKindOf(0));
    }

    [TestMethod]
    public void ReadsTheFirebrandPageCostFromTheAbility()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);

        Assert.AreEqual(1, palettes.PageCostOf(45022));
        Assert.AreEqual(3, palettes.PageCostOf(55549));
        Assert.AreEqual(0, palettes.PageCostOf(29057));
    }
}
