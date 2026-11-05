using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentMountWardrobeTests
{
    [TestMethod]
    public void KeepsTheLiveDyesOfEachMount()
    {
        var wardrobe = new AgentMountWardrobe();
        wardrobe.Handle(Message(AgentMountWardrobe.MountDyes, 7356, 85, 1579, 1680, 1489, 220));
        wardrobe.Handle(Message(AgentMountWardrobe.MountDyes, 7356, 705, 1, 1, 1, 124));
        CollectionAssert.AreEqual(new ushort[] { 85, 1579, 1680, 1489 }, wardrobe.DyesOf(7356, 220)!.ToArray());
        CollectionAssert.AreEqual(new ushort[] { 705, 1, 1, 1 }, wardrobe.DyesOf(7356, 124)!.ToArray());
        Assert.IsNull(wardrobe.DyesOf(7356, 407));
    }

    [TestMethod]
    public void ANewSkinReplacesTheSkinOfTheSameMount()
    {
        var mountOfSkin = new Dictionary<uint, uint?> { [836] = 220, [807] = 407, [900] = 220 };
        var wardrobe = new AgentMountWardrobe(skin => mountOfSkin.GetValueOrDefault(skin));
        wardrobe.Handle(Message(AgentMountWardrobe.MountSkin, 7356, 836));
        wardrobe.Handle(Message(AgentMountWardrobe.MountSkin, 7356, 807));
        wardrobe.Handle(Message(AgentMountWardrobe.MountSkin, 7356, 900));
        Assert.AreEqual(900u, wardrobe.SkinOf(7356, 220));
        Assert.AreEqual(807u, wardrobe.SkinOf(7356, 407));
        Assert.AreEqual(2, wardrobe.SkinsOf(7356).Count);
    }

    private static DecodedMessage Message(int id, params ulong[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)id);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(id, 0, 0, fields);
    }
}
