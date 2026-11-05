using System;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentEnergiesTests
{
    private const int CaptureBuild = 208784;
    private const string CapturedGuardianCreate = "DD019B1F0000804000000000000080400000";
    private const string CapturedWarriorCreate = "F80188111E00";

    [TestMethod]
    public void NewMetersStartFullAndRegenerateAtTheDefaultRate()
    {
        var energies = new AgentEnergies { DefaultMaximum = 100, DefaultChangePerSecond = 10 };
        var meter = energies.MeterFor(6550, 3, 1000);
        Assert.AreEqual(100f, meter.Current);
        Assert.IsTrue(meter.UsesDefaultRate);
        meter.Adjust(-50);
        Assert.AreEqual(70f, energies.ValueOf(6550, 3, 3000), 1e-4f);
    }

    [TestMethod]
    public void LimitsKeepTheFractionAndSetTheCurrentEnergy()
    {
        var energies = new AgentEnergies();
        energies.Handle(Message(AgentEnergies.EnergyLimits, 6550u, 1u, 100f, 40f), 1000);
        var meter = energies.MeterOf(6550, 1)!;
        Assert.AreEqual(100f, meter.Maximum);
        Assert.AreEqual(40f, meter.ValueAt(5000));
        energies.Handle(Message(AgentEnergies.EnergyChangeScale, 6550u, 1u, 2f), 1000);
        meter.SetMaximum(200f);
        Assert.AreEqual(80f, meter.Current);
    }

    [TestMethod]
    public void StateRunsTheRateUntilPausedAndScalesTheDefaultRate()
    {
        var energies = new AgentEnergies();
        energies.Handle(Message(AgentEnergies.EnergyState, 6550u, 2u, 100f, 50f, -10f, 5f, 1f, 0u), 1000);
        Assert.AreEqual(30f, energies.ValueOf(6550, 2, 3000), 1e-4f);
        Assert.AreEqual(0f, energies.ValueOf(6550, 2, 60000), 1e-4f);
        var meter = energies.MeterFor(6550, 2, 2000);
        Assert.AreEqual(40f, meter.Current, 1e-4f);
        meter.RestoreDefaultRate();
        meter.SetChangeScale(3f);
        Assert.AreEqual(15f, meter.ChangePerSecond);
        meter.SetPaused(true);
        Assert.AreEqual(40f, meter.ValueAt(10000));
    }

    [TestMethod]
    public void IgnoresKindsOutsideTheFourMeters()
    {
        var energies = new AgentEnergies();
        energies.Handle(Message(AgentEnergies.EnergyLimits, 6550u, 4u, 100f, 40f), 0);
        Assert.IsNull(energies.MeterOf(6550, 0));
    }

    [TestMethod]
    public void CapturedGuardianCreateFillsVirtuesAndRemoveClearsThem()
    {
        var resources = new AgentProfessionResources();
        var create = Captured(CapturedGuardianCreate);
        resources.Handle(create, 0);
        var meter = resources.ResourceOf(MessageAgent(create), ProfessionResourceKind.GuardianMeter)!.Value;
        Assert.AreEqual(4f, meter.Maximum);
        Assert.AreEqual(4f, meter.Value);
        Assert.AreEqual(0f, meter.ChangePerSecond);
        resources.Handle(Message(0x1de, MessageAgent(create)), 0);
        Assert.AreEqual(0, resources.ResourcesOf(MessageAgent(create)).Count);
    }

    [TestMethod]
    public void CapturedWarriorCreateFillsAdrenaline()
    {
        var resources = new AgentProfessionResources();
        var create = Captured(CapturedWarriorCreate);
        resources.Handle(create, 0);
        var adrenaline = resources.ResourceOf(MessageAgent(create), ProfessionResourceKind.WarriorAdrenaline)!.Value;
        Assert.AreEqual(30f, adrenaline.Maximum);
        Assert.AreEqual(0f, adrenaline.Value);
    }

    [TestMethod]
    public void RevenantEnergyRegeneratesToItsMaximum()
    {
        var resources = new AgentProfessionResources();
        resources.Handle(Message(0x1f0, 6550u, 50f, 100f, 5f), 1000);
        var energy = resources.ResourceOf(6550, ProfessionResourceKind.RevenantEnergy)!.Value;
        Assert.AreEqual(60f, energy.ValueAt(3000), 1e-4f);
        Assert.AreEqual(100f, energy.ValueAt(100000), 1e-4f);
    }

    private static uint MessageAgent(DecodedMessage message) => (uint)MessageNumbers.Number(message, 1);

    private static DecodedMessage Captured(string hex)
    {
        var schemas = RecordedSession.Schemas;
        if (schemas.Build != CaptureBuild) Assert.Inconclusive($"the fixture is build {CaptureBuild}, the installed exe is build {schemas.Build}");
        return MessageStreamDecoder.Decode(schemas.Game.Recv, Convert.FromHexString(hex)).Single();
    }

    private const int VarintFieldType = 4;
    private const int Fixed32FieldType = 0x06;

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (var index = 0; index < values.Length; index++)
            fields[index + 1] = values[index] switch
            {
                float wireFloat => new DecodedField(Fixed32FieldType, true, (ulong)BitConverter.SingleToUInt32Bits(wireFloat)),
                uint varint => new DecodedField(VarintFieldType, true, (ulong)varint),
                var other => new DecodedField(0, true, other),
            };
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
