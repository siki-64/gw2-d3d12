using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class OwnSkillbarTests
{
    [TestMethod]
    public void KeepsTheRecordedLegendSpecializationsAndSlotPalettesOfTheOwnAgent()
    {
        var skillbar = new OwnSkillbar();
        skillbar.Handle(Message(OwnSkillbar.ProfessionState, Value(6550), Value(15)));
        skillbar.Handle(Message(OwnSkillbar.OwnedCharacter, Value(16), Value(6550)));
        skillbar.Handle(Message(OwnSkillbar.SpecializationBuild, Value(16), new DecodedField(0, true, new List<DecodedField[]> { new[] { Value(18) }, new[] { Value(11) }, new[] { Value(74) } }), new DecodedField(0, true, new List<DecodedField[]> { new[] { Value(1660) }, new[] { Value(1361) } }), Value(0)));
        skillbar.Handle(Message(OwnSkillbar.EquippedLegends, Value(6550), Ids(15, 16), Ids(16, 15)));
        foreach (var (palette, slot) in new (ulong, ulong)[] { (4572, 0), (4614, 1), (4651, 2), (4564, 3), (4554, 4) })
        {
            skillbar.Handle(Message(OwnSkillbar.SlotPalette, Value(palette), Value(slot), Value(0), Value(16)));
            skillbar.Handle(Message(OwnSkillbar.SlotPalette, Value(1), Value(slot), Value(1), Value(16)));
        }
        Assert.AreEqual(15u, skillbar.ProfessionStateOf(6550));
        Assert.AreEqual(16u, skillbar.InactiveLegendOf(6550));
        CollectionAssert.AreEqual(new uint[] { 1660, 1361 }, skillbar.TraitsOf(6550).ToArray());
        skillbar.Handle(Message(OwnSkillbar.LegendSwap, Value(6550), Value(10000), Value(0), Ids(16, 15)));
        skillbar.Handle(Message(OwnSkillbar.ProfessionState, Value(6550), Value(16)));
        Assert.AreEqual(15u, skillbar.InactiveLegendOf(6550));
        CollectionAssert.AreEqual(new uint[] { 18, 11, 74 }, skillbar.SpecializationsOf(6550).ToArray());
        CollectionAssert.AreEqual(new uint?[] { 4572, 4614, 4651, 4564, 4554 }, Enumerable.Range(0, OwnSkillbar.SlotCount).Select(slot => skillbar.SlotPaletteOf(6550, slot)).ToArray());
    }

    [TestMethod]
    public void AppliesTheRecordedSpecializationAndTraitChanges()
    {
        var skillbar = new OwnSkillbar();
        skillbar.Handle(Message(OwnSkillbar.OwnedCharacter, Value(28), Value(6140)));
        skillbar.Handle(Message(OwnSkillbar.SpecializationBuild, Value(28), Ids(18, 11, 24), Ids(1660, 1361, 115, 2255, 1843, 1789, 1316, 1052, 2021), Value(0)));
        skillbar.Handle(Message(OwnSkillbar.TraitSelection, Value(28), Value(1456), Value(1), Value(2)));
        skillbar.Handle(Message(OwnSkillbar.SpecializationSelection, Value(28), Value(74), Value(2)));

        CollectionAssert.AreEqual(new uint[] { 18, 11, 74 }, skillbar.SpecializationsOf(6140).ToArray());
        CollectionAssert.AreEqual(new uint[] { 1660, 1361, 115, 2255, 1843, 1456, 0, 0, 0 }, skillbar.TraitsOf(6140).ToArray());
    }

    private static DecodedField Value(ulong value) => new(0, true, value);

    private static DecodedField Ids(params ulong[] ids) => new(0, true, ids.Select(id => new[] { Value(id) }).ToList());

    private static DecodedMessage Message(int messageId, params DecodedField[] values) =>
        new(messageId, 0, 0, [new DecodedField(0, true, (ulong)messageId), .. values]);
}
