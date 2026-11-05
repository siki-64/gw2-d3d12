using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentEquipmentTests
{
    [TestMethod]
    public void ReadsTheRecordedRevenantsProfessionAndActiveWeapon()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var equipment = new AgentEquipment();
        foreach (var message in inbound.Messages.Take(3000))
            equipment.Handle(message);

        Assert.AreEqual(9u, equipment.ProfessionOf(4867));
        Assert.AreEqual(4u, equipment.ActiveSetOf(4867));
        Assert.AreEqual(((uint?)15517, (uint?)null), equipment.ActiveWeaponItemDataIdsOf(4867));
    }

    [TestMethod]
    public void OwnCharacterWearsSixArmorPiecesWithFourDyesEach()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var equipment = new AgentEquipment();
        foreach (var message in inbound.Messages) equipment.Handle(message);
        var ownAgent = (uint)(ulong)inbound.Messages.First(message => message.MessageId == AgentEquipment.CoreStats).Fields[1].Value!;

        var armor = equipment.ArmorOf(ownAgent);
        CollectionAssert.AreEqual(new uint[] { 2, 3, 4, 5, 6, 7 }, armor.Select(piece => piece.Slot).ToArray());
        Assert.IsTrue(armor.All(piece => piece.SkinDataId != 0 && piece.DyeDataIds.Count == 4));
    }

    [TestMethod]
    public void OwnCharacterHidesItsHelmAndBackItemWhileOthersHideNothing()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var equipment = new AgentEquipment();
        foreach (var message in inbound.Messages) equipment.Handle(message);
        var ownAgent = (uint)(ulong)inbound.Messages.First(message => message.MessageId == AgentEquipment.CoreStats).Fields[1].Value!;
        var otherAgent = (uint)(ulong)inbound.Messages.First(message => message.MessageId == AgentEquipment.HiddenEquipmentSlots && (ulong)message.Fields[1].Value! != ownAgent).Fields[1].Value!;

        const uint backSlot = 1;
        const uint helmSlot = 7;
        Assert.IsTrue(equipment.IsSlotHidden(ownAgent, backSlot));
        Assert.IsTrue(equipment.IsSlotHidden(ownAgent, helmSlot));
        CollectionAssert.AreEqual(new uint[] { 2, 3, 4, 5, 6 }, equipment.VisibleArmorOf(ownAgent).Select(piece => piece.Slot).ToArray());
        Assert.IsFalse(Enumerable.Range(0, 64).Any(slot => equipment.IsSlotHidden(otherAgent, (uint)slot)));
    }
}
