using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentVitalsTests
{
    private const uint OwnAgent = 4672;
    private const int Fixed32FieldType = 0x06;

    [TestMethod]
    public void TheHealthCreateFillsCurrentMaximumBarrierAndRegen()
    {
        var vitals = new AgentVitals();
        vitals.Handle(HealthCreate(current: 595f, maximum: 600f, changePerSecond: 70f, barrier: 0f, barrierMaximum: 300f), 0);

        Assert.AreEqual(new AgentHealth(595f, 600f, 0f, 300f, 70f), vitals.HealthOf(OwnAgent));
    }

    [TestMethod]
    public void HealthChangesAddTheirDeltasWithinTheMeters()
    {
        var vitals = new AgentVitals();
        vitals.Handle(HealthCreate(current: 595f, maximum: 600f, changePerSecond: 0f, barrier: 0f, barrierMaximum: 300f), 0);

        vitals.Handle(Message(AgentVitals.HealthChange, Float(-5f), Float(0f)), 0);
        Assert.AreEqual(590f, vitals.HealthOf(OwnAgent)!.Value.Current);

        vitals.Handle(Message(AgentVitals.HealthChange, Float(50f), Float(400f)), 0);
        Assert.AreEqual(600f, vitals.HealthOf(OwnAgent)!.Value.Current);
        Assert.AreEqual(300f, vitals.HealthOf(OwnAgent)!.Value.Barrier);
    }

    [TestMethod]
    public void HealthChangesForAgentsWithoutHealthAreIgnored()
    {
        var vitals = new AgentVitals();
        vitals.Handle(Message(AgentVitals.HealthChange, Float(-5f), Float(0f)), 0);

        Assert.IsNull(vitals.HealthOf(OwnAgent));
    }

    private static DecodedMessage HealthCreate(float current, float maximum, float changePerSecond, float barrier, float barrierMaximum) =>
        Message(AgentVitals.HealthCreate, new DecodedField(0, true, 0UL), Float(current), Float(maximum), Float(changePerSecond), Float(barrier), Float(barrierMaximum), Float(maximum));

    private static DecodedField Float(float value) => new(Fixed32FieldType, true, (ulong)BitConverter.SingleToUInt32Bits(value));

    private static DecodedMessage Message(int messageId, params DecodedField[] values)
    {
        DecodedField[] fields = [new(0, true, (ulong)messageId), new(0, true, (ulong)OwnAgent), .. values];
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
