using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentKennelsTests
{
    [TestMethod]
    public void ActivatesAndStowsThePet()
    {
        var kennels = new AgentKennels();
        kennels.Handle(Message(AgentKennels.ActivePetSet, 874u, 0u, 1202u));
        Assert.AreEqual(1202u, kennels.ActivePetOf(874));
        kennels.Handle(Message(AgentKennels.ActivePetSet, 874u, AgentKennels.NoEquipSlot, 0u));
        Assert.IsNull(kennels.ActivePetOf(874));
    }

    [TestMethod]
    public void TracksThePetsOfTheRecordedSession()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.First(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound).Messages;
        var petMessages = inbound.Where(message => message.MessageId == AgentKennels.ActivePetSet).ToList();
        if (petMessages.Count == 0) Assert.Inconclusive("the capture holds no active pet message");
        var kennels = new AgentKennels();
        foreach (var message in petMessages)
        {
            Assert.IsTrue(message.Fields.Count > 3);
            kennels.Handle(message);
        }
        var last = petMessages[^1];
        var owner = (uint)MessageNumbers.Number(last, 1);
        var expected = (uint)MessageNumbers.Number(last, 2) == AgentKennels.NoEquipSlot ? (uint?)null : (uint)MessageNumbers.Number(last, 3);
        Assert.AreEqual(expected, kennels.ActivePetOf(owner));
    }

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is uint u ? (ulong)u : values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
