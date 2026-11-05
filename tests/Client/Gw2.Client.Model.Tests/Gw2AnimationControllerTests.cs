using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AnimationControllerTests
{
    [TestMethod]
    public void EasesTheWeightByAtMostFourPerSecond()
    {
        Gw2AnimationEntry[] entries = [new() { AnimationData = 1, Weight = 0f, TargetWeight = 1f }, new() { AnimationData = 2, Weight = 0.5f, TargetWeight = 0f, Flags = Gw2AnimationEntryFlags.FixedWeight }];

        Gw2AnimationController.EaseWeights(entries, 0.1f);

        Assert.AreEqual(0.4f, entries[0].Weight, 1e-6f);
        Assert.AreEqual(0.5f, entries[1].Weight);
    }

    [TestMethod]
    public void EarlierEntriesTakePriorityOverTheRemainingWeight()
    {
        Gw2AnimationEntry[] entries =
        [
            new() { AnimationData = 1, Weight = 0.7f },
            new() { AnimationData = 2, Weight = 0.6f },
            new() { AnimationData = 3, Weight = 0.2f, Flags = Gw2AnimationEntryFlags.FullWeightOutsidePriority },
            new() { AnimationData = 4, Weight = 1f },
        ];

        Gw2AnimationController.AssignSampleWeights(entries);

        Assert.AreEqual(0.7f, entries[0].SampleWeight, 1e-6f);
        Assert.AreEqual(0.3f, entries[1].SampleWeight, 1e-6f);
        Assert.AreEqual(1f, entries[2].SampleWeight);
        Assert.AreEqual(0f, entries[3].SampleWeight);
    }

    [TestMethod]
    public void ARestartedClipHandsItsWeightToItsReplacement()
    {
        Gw2AnimationEntry[] entries = [new() { AnimationData = 5, Weight = 0.6f }, new() { AnimationData = 5, Weight = 0.7f }];

        Gw2AnimationController.MergeRestartedClips(entries);

        Assert.AreEqual(0f, entries[0].Weight);
        Assert.AreEqual(1f, entries[1].Weight);
    }

    [TestMethod]
    public void AnEndlessClockWrapsByTheDuration()
    {
        var clock = new Gw2AnimationClock { LocalClock = 0.9f, Speed = 2f, LocalDuration = 1f };

        Gw2AnimationController.Advance(ref clock, 0.3f);

        Assert.AreEqual(0.5f, clock.LocalClock, 1e-5f);
        Assert.AreEqual(1, clock.LoopIndex);
    }

    [TestMethod]
    public void AFiniteClockHoldsAtTheEndOfItsLastLoop()
    {
        var clock = new Gw2AnimationClock { LocalClock = 0.9f, Speed = 1f, LocalDuration = 1f, LoopCount = 1 };

        Gw2AnimationController.Advance(ref clock, 0.5f);

        Assert.AreEqual(1f, clock.LocalClock);
        Assert.AreEqual(0, clock.LoopIndex);
    }
}
