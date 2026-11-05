using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentLifeStateTests
{
    private static readonly byte[] LiveDefeatEvent = [0x33, 0x00, 0x00, 0x05, 0xDE, 0x30, 0x00, 0x00, 0x1A, 0x1B, 0x00, 0x00, 0x00];
    private static readonly byte[] LiveDownedEvent = [0x34, 0x00, 0x00, 0x05, 0x65, 0x0B, 0x00, 0x00, 0xC1, 0x0F, 0x00, 0x00, 0x00];
    private static readonly byte[] LiveDefeatWhileDownedEvent = [0x33, 0x00, 0x00, 0x0A, 0x02, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] LiveRevivedEvent = [0x49, 0x04, 0x00];

    [TestMethod]
    public void CreateLeavesTheCharacterAlive()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        Assert.AreEqual(LifeState.Alive, lifeStates.StateOf(6939));
    }

    [TestMethod]
    public void TheLiveDefeatOnTheCommandMessageDefeats()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x11u, 0u, LiveDefeatEvent));
        Assert.AreEqual(LifeState.Defeated, lifeStates.StateOf(6939));
    }

    [TestMethod]
    public void TheLiveDownedDefeatedAndWaypointSequence()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x32u, 0u, LiveDownedEvent));
        Assert.AreEqual(LifeState.Downed, lifeStates.StateOf(6939));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x11u, 0u, LiveDefeatWhileDownedEvent));
        Assert.AreEqual(LifeState.Defeated, lifeStates.StateOf(6939));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x12u, 0u, LiveRevivedEvent));
        Assert.AreEqual(LifeState.Alive, lifeStates.StateOf(6939));
    }

    [TestMethod]
    public void EventsWithoutTheEventFlagAreIgnored()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        lifeStates.Handle(Message(AgentLifeState.AgentData, 6939u, 0u, 0x01u, 0u, LiveDefeatEvent));
        Assert.AreEqual(LifeState.Alive, lifeStates.StateOf(6939));
    }

    [TestMethod]
    public void HealthCreateCarriesTheLifeState()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        lifeStates.Handle(Message(AgentLifeState.HealthCreate, 6939u, 2u, 0f, 600f, 0f, 0f, 0f, 0f));
        Assert.AreEqual(LifeState.Downed, lifeStates.StateOf(6939));
    }

    [TestMethod]
    public void ALaterHealthCreateKeepsTheSpawnDefeat()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        lifeStates.Handle(Message(AgentLifeState.HealthCreate, 6939u, 1u, 0f, 600f, 0f, 0f, 0f, 0f));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x11u, 0u, LiveDefeatEvent));
        lifeStates.Handle(Message(AgentLifeState.HealthCreate, 6939u, 0u, 0f, 600f, 0f, 0f, 0f, 0f));
        Assert.AreEqual(LifeState.Defeated, lifeStates.StateOf(6939));
    }

    [TestMethod]
    public void OtherMessagesWithAListInFieldOneAreIgnored()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(0x2EC, new System.Collections.Generic.List<DecodedField[]>()));
        Assert.IsNull(lifeStates.StateOf(0));
    }

    [TestMethod]
    public void DefeatedNeverMovesAndDownedMovesOnlyInWater()
    {
        var lifeStates = new AgentLifeState();
        lifeStates.Handle(Message(AgentLifeState.CharacterCreate, 6939u));
        Assert.IsTrue(lifeStates.AllowsMovement(6939, isInWater: false));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x32u, 0u, LiveDownedEvent));
        Assert.IsFalse(lifeStates.AllowsMovement(6939, isInWater: false));
        Assert.IsTrue(lifeStates.AllowsMovement(6939, isInWater: true));
        lifeStates.Handle(Message(AgentLifeState.AgentCommandData, 6939u, 0u, 0x11u, 0u, LiveDefeatWhileDownedEvent));
        Assert.IsFalse(lifeStates.AllowsMovement(6939, isInWater: true));
    }

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is uint u ? (ulong)u : values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
