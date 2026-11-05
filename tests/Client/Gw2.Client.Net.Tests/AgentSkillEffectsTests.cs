using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentSkillEffectsTests
{
    private static readonly byte[] LiveTrackedEffect = Convert.FromHexString("36C5020000C50200008D980000780C030004000000000000000000000000000000000000000000000000000000000000000000000000140000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
    private static readonly byte[] LiveTrackedEffectEnd = Convert.FromHexString("3514000000");

    [TestMethod]
    public void TheGadgetEffectSharesTheFirst67BytesOfTheCharacterEffect()
    {
        var gadgetEffect = LiveTrackedEffect[..AgentSkillEffects.GadgetSkillEffectLength];
        gadgetEffect[0] = AgentSkillEffects.GadgetSkillEffectEvent;
        Assert.AreEqual(AgentSkillEffects.Decode(LiveTrackedEffect), AgentSkillEffects.DecodeGadgetEffect(gadgetEffect));
        Assert.IsNull(AgentSkillEffects.DecodeGadgetEffect(LiveTrackedEffect[..AgentSkillEffects.GadgetSkillEffectLength]));
    }

    [TestMethod]
    public void TheLiveEffectNamesItsFields()
    {
        Assert.AreEqual(new AgentSkillEffect(709, 709, 39053, 199800, 4, 0, 20), AgentSkillEffects.Decode(LiveTrackedEffect));
    }

    [TestMethod]
    public void TheLiveEndRemovesTheTrackedEffect()
    {
        var effects = new AgentSkillEffects();
        effects.Handle(Message(709u, LiveTrackedEffect));
        Assert.IsTrue(effects.TrackedEffects.ContainsKey(20));
        Assert.AreEqual(1, effects.TakeStartedEffects().Count);
        effects.Handle(Message(709u, LiveTrackedEffectEnd));
        Assert.AreEqual(0, effects.TrackedEffects.Count);
    }

    private static DecodedMessage Message(uint agentId, byte[] blob)
    {
        var values = new object[] { (ulong)AgentLifeState.AgentCommandData, (ulong)agentId, 0UL, 0x10UL, 0UL, blob };
        var fields = new DecodedField[values.Length];
        for (int i = 0; i < values.Length; i++) fields[i] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(AgentLifeState.AgentCommandData, 0, 0, fields);
    }
}
