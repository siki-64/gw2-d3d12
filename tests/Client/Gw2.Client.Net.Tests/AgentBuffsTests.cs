using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentBuffsTests
{
    [TestMethod]
    public void TheLiveApplyStartsAnActiveBuffAtArrival()
    {
        var buffs = new AgentBuffs();
        buffs.Handle(Message(AgentBuffs.BuffApply, 602, 602, 62939, 5427, 10000, 80, 1), 1000);
        Assert.AreEqual(new AgentBuff(5427, 62939, 602, 10000, 1000, 80, true), buffs.BuffOf(602, 5427));
        Assert.AreEqual(7000, buffs.BuffOf(602, 5427)!.Value.RemainingAt(4000));
    }

    [TestMethod]
    public void TheLiveDurationUpdatesKeepTheStartTime()
    {
        var buffs = new AgentBuffs();
        buffs.Handle(Message(AgentBuffs.BuffApply, 602, 602, 62939, 5427, 10000, 80, 1), 102817);
        buffs.Handle(Message(AgentBuffs.BuffDuration, 602, 5427, 13280), 106101);
        buffs.Handle(Message(AgentBuffs.BuffDuration, 602, 5427, 15640), 108461);
        Assert.AreEqual(102817, buffs.BuffOf(602, 5427)!.Value.StartMilliseconds);
        Assert.AreEqual(15640 - (108461 - 102817), buffs.BuffOf(602, 5427)!.Value.RemainingAt(108461));
        buffs.Handle(Message(AgentBuffs.BuffRemoveByInstance, 602, 0, 5427), 118457);
        Assert.IsNull(buffs.BuffOf(602, 5427));
    }

    [TestMethod]
    public void TheLiveActivateShowsTheRemainingTimeOfAQueuedStack()
    {
        var buffs = new AgentBuffs();
        buffs.Handle(Message(AgentBuffs.BuffApply, 5143, 3596, 726, 1073, 3231, 53, 0), 20934);
        Assert.AreEqual(3231, buffs.BuffOf(5143, 1073)!.Value.RemainingAt(21351));
        buffs.Handle(Message(AgentBuffs.BuffActivate, 5143, 1073, 3222), 21351);
        Assert.IsTrue(buffs.BuffOf(5143, 1073)!.Value.IsActive);
        Assert.AreEqual(3222, buffs.BuffOf(5143, 1073)!.Value.RemainingAt(21351));
        Assert.AreEqual(2222, buffs.BuffOf(5143, 1073)!.Value.RemainingAt(22351));
    }

    [TestMethod]
    public void DeactivatePausesTheRemainingTime()
    {
        var buffs = new AgentBuffs();
        buffs.Handle(Message(AgentBuffs.BuffApply, 5143, 5143, 736, 8775, 5000, 80, 1), 20000);
        buffs.Handle(Message(AgentBuffs.BuffDeactivate, 5143, 8775, 231), 21150);
        Assert.IsFalse(buffs.BuffOf(5143, 8775)!.Value.IsActive);
        Assert.AreEqual(231, buffs.BuffOf(5143, 8775)!.Value.RemainingAt(90000));
    }

    [TestMethod]
    public void RemoveBySkillDropsEveryStackOfThatSkill()
    {
        var buffs = new AgentBuffs();
        buffs.Handle(Message(AgentBuffs.BuffApply, 7356, 7356, 33640, 1, AgentBuff.UnlimitedDuration, 80, 1), 0);
        buffs.Handle(Message(AgentBuffs.BuffApply, 7356, 7356, 33640, 2, AgentBuff.UnlimitedDuration, 80, 1), 0);
        buffs.Handle(Message(AgentBuffs.BuffApply, 7356, 7356, 15750, 3, AgentBuff.UnlimitedDuration, 1, 1), 0);
        Assert.IsTrue(buffs.BuffOf(7356, 3)!.Value.IsUnlimited);
        buffs.Handle(Message(AgentBuffs.BuffRemoveBySkill, 7356, 7356, 33640), 0);
        CollectionAssert.AreEqual(new uint[] { 15750 }, buffs.SkillIdsOf(7356).ToArray());
    }

    private static DecodedMessage Message(int id, params ulong[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)id);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(id, 0, 0, fields);
    }
}
