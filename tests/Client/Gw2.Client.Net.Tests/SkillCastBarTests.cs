using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class SkillCastBarTests
{
    private const uint GuardianGreatswordStrike = 9137;
    private const uint RevenantHammerRendTheMists = 27976;
    private const uint WarriorGreatswordRush = 14355;
    private static readonly Gw2SkillLoopStepCounts NoLoop = new(0, 0);

    [TestMethod]
    public void QuicknessShortensEveryCastPhase()
    {
        var timing = new Gw2SkillCastTiming(Gw2SkillProgressWidget.Charge, 0.75, 1.5, [new Gw2SkillChargeStage(1.5, 1)]);
        var quickened = timing.AtActivationSpeed(1.5f);
        Assert.AreEqual(0.5, quickened.FirstExecuteSeconds, 1e-6);
        Assert.AreEqual(1.0, quickened.LastExecuteSeconds, 1e-6);
        Assert.AreEqual(1.0, quickened.ChargeStages[0].Seconds, 1e-6);
        Assert.AreSame(timing, timing.AtActivationSpeed(1f));
    }

    [TestMethod]
    public void ReadsTheWarmupCastTimeFromTheAbilitySequence()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var castBar = new Gw2SkillCastBar(reader.ContentIndex, SkillPaletteContentLayout.Current, SkillCastBarLayout.Current, reader.ReadAnimSequences()!);

        AssertWarmup(castBar.TimingOf(GuardianGreatswordStrike), 0.55);
        AssertWarmup(castBar.TimingOf(RevenantHammerRendTheMists), 1.26);
        AssertWarmup(castBar.TimingOf(WarriorGreatswordRush), 0.25);
    }

    [TestMethod]
    public void PicksTheWidgetLikeTheSkillProgressRoot()
    {
        Assert.AreEqual(Gw2SkillProgressWidget.None, Gw2SkillCastBar.TimingOf(SequenceWithExecutesAt(), NoLoop).Widget);
        Assert.AreEqual(Gw2SkillProgressWidget.None, Gw2SkillCastBar.TimingOf(SequenceWithExecutesAt(240), NoLoop).Widget);
        Assert.AreEqual(Gw2SkillProgressWidget.Warmup, Gw2SkillCastBar.TimingOf(SequenceWithExecutesAt(250, 1240), NoLoop).Widget);
        Assert.AreEqual(Gw2SkillProgressWidget.Channel, Gw2SkillCastBar.TimingOf(SequenceWithExecutesAt(100, 1100), NoLoop).Widget);
    }

    [TestMethod]
    public void LongerLoopsMoveTheExecutesAfterTheLoop()
    {
        var looped = SequenceWithExecutesAt(300, 900) with { Steps = [Step(200, 0), Step(100, Gw2SkillCastBar.LoopFirstStepFlag), Step(100, Gw2SkillCastBar.LoopLastStepFlag), Step(500, 0)] };

        var timing = Gw2SkillCastBar.TimingOf(looped, new Gw2SkillLoopStepCounts(4, 10));

        Assert.AreEqual(Gw2SkillProgressWidget.Channel, timing.Widget);
        Assert.AreEqual(0.3, timing.FirstExecuteSeconds, 1e-9);
        Assert.AreEqual(1.7, timing.LastExecuteSeconds, 1e-9);
    }

    [TestMethod]
    public void ChargesWhenTheLoopCanRunLongerThanItsShortestLength()
    {
        var charged = SequenceWithExecutesAt(500) with
        {
            Steps = [Step(200, Gw2SkillCastBar.LoopFirstStepFlag | Gw2SkillCastBar.LoopLastStepFlag)],
            ChargeStages = [new(400, 1), new(800, 2)],
        };

        var timing = Gw2SkillCastBar.TimingOf(charged, new Gw2SkillLoopStepCounts(0, 5));

        Assert.AreEqual(Gw2SkillProgressWidget.Charge, timing.Widget);
        CollectionAssert.AreEqual(new[] { new Gw2SkillChargeStage(0.4, 1), new Gw2SkillChargeStage(0.8, 2) }, timing.ChargeStages.ToArray());
    }

    private static void AssertWarmup(Gw2SkillCastTiming timing, double firstExecuteSeconds)
    {
        Assert.AreEqual(Gw2SkillProgressWidget.Warmup, timing.Widget);
        Assert.AreEqual(firstExecuteSeconds, timing.FirstExecuteSeconds, 1e-9);
    }

    private static Gw2AnimSequenceStep Step(uint milliseconds, uint flags) => new(0, 1f, flags, milliseconds);

    private static Gw2AnimSequenceData SequenceWithExecutesAt(params uint[] milliseconds) =>
        new(Gw2AnimSequences.DefaultAnimationDataToken, 0, 0f, 0f, [], [.. milliseconds.Select(time => new Gw2AnimSequenceTrigger(Gw2AnimSequences.ExecuteTrigger, time, 0))]);
}
