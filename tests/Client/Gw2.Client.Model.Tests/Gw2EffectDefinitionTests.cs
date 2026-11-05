using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EffectDefinitionTests
{
    [TestMethod]
    public void EventsPlacedInTheWorldAreKnown()
    {
        Assert.IsTrue(SkillEffectEvents.IsPlacedInWorld(SkillEffectEvents.HitOnTarget));
        Assert.IsTrue(SkillEffectEvents.IsPlacedInWorld(SkillEffectEvents.ActivationPlaced));
        Assert.IsFalse(SkillEffectEvents.IsPlacedInWorld(SkillEffectEvents.Hit));
        Assert.IsFalse(SkillEffectEvents.IsPlacedInWorld(SkillEffectEvents.EffectMomentAttached));
    }

    [TestMethod]
    public void ActivationEffectsAreKeptUntilTheSkillStops()
    {
        Assert.IsTrue(SkillEffectEvents.IsKeptUntilStopped(SkillEffectEvents.ActivationAttached));
        Assert.IsTrue(SkillEffectEvents.IsKeptUntilStopped(SkillEffectEvents.ActivationPlaced));
        Assert.IsFalse(SkillEffectEvents.IsKeptUntilStopped(SkillEffectEvents.Hit));
    }
}
