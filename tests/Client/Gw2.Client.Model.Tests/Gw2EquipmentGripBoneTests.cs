using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EquipmentGripBoneTests
{
    [TestMethod]
    public void MapsSocketBonesToTheExesItemBoneTokens()
    {
        Assert.AreEqual(0xB184577D263ul, Gw2EquipmentGripBone.ItemBoneFor(0x58C229169363D03ul));
        Assert.AreEqual(0x825177D272ul, Gw2EquipmentGripBone.ItemBoneFor(0x41289169363D12ul));
        Assert.AreEqual(0x4705104C8F2ul, Gw2EquipmentGripBone.ItemBoneFor(0x23828A18ACul));
        Assert.AreEqual(0x4705104C8F2ul, Gw2EquipmentGripBone.ItemBoneFor(0x47051441D32ul));
        Assert.AreEqual(0x20991C8C2A513ul, Gw2EquipmentGripBone.ItemBoneFor(146973971ul));
        Assert.AreEqual(0ul, Gw2EquipmentGripBone.ItemBoneFor(671218ul));
    }
}
