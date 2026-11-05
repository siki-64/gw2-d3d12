using Gw2.Formats;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkillEffectMomentTests
{
    [TestMethod]
    public void ActivationEventsPlayWhenTheSkillIsActivated()
    {
        Assert.AreEqual(Gw2SkillEffectMoment.Activation, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.ActivationAttached));
        Assert.AreEqual(Gw2SkillEffectMoment.Activation, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.ActivationPlaced));
        Assert.IsTrue(Gw2SkillEffectMoments.LastsUntilStopped(SkillEffectEvents.ActivationAttached));
    }

    [TestMethod]
    public void EffectMomentEventsPlayWhenTheSkillExecutes()
    {
        Assert.AreEqual(Gw2SkillEffectMoment.Execution, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.EffectMomentAttached));
        Assert.AreEqual(Gw2SkillEffectMoment.Execution, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.EffectMomentPlacedGround));
        Assert.IsFalse(Gw2SkillEffectMoments.LastsUntilStopped(SkillEffectEvents.EffectMomentAttached));
    }

    [TestMethod]
    public void HitEventsPlayOnEachEnemyHit()
    {
        Assert.AreEqual(Gw2SkillEffectMoment.Hit, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.Hit));
        Assert.AreEqual(Gw2SkillEffectMoment.Hit, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.HitOnTarget));
    }

    [TestMethod]
    public void CriticalHitEventsPlayOnlyOnCriticalHits()
    {
        Assert.AreEqual(Gw2SkillEffectMoment.CriticalHit, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.CriticalHit));
    }

    [TestMethod]
    public void BuffAndTrailEventsAreNotSkillMoments()
    {
        Assert.AreEqual(Gw2SkillEffectMoment.None, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.BuffActive));
        Assert.AreEqual(Gw2SkillEffectMoment.None, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.WeaponTrail));
    }

    [TestMethod]
    public void ProjectileImpactAndPulseEventsHaveTheirOwnMoments()
    {
        Assert.AreEqual(Gw2SkillEffectMoment.Projectile, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.Projectile));
        Assert.AreEqual(Gw2SkillEffectMoment.ProjectileImpact, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.ProjectileGroundImpact));
        Assert.AreEqual(Gw2SkillEffectMoment.Pulse, Gw2SkillEffectMoments.MomentOf(SkillEffectEvents.Pulse));
    }

    [TestMethod]
    public void PlacedEventsArePlacedAtTheTarget()
    {
        Assert.AreEqual(Gw2SkillEffectPlacement.PlacedAtTarget, Gw2SkillEffectMoments.PlacementOf(SkillEffectEvents.EffectMomentPlaced, 1));
        Assert.AreEqual(Gw2SkillEffectPlacement.PlacedAtTarget, Gw2SkillEffectMoments.PlacementOf(SkillEffectEvents.HitOnTarget, 2));
        Assert.AreEqual(Gw2SkillEffectPlacement.AttachedToCaster, Gw2SkillEffectMoments.PlacementOf(SkillEffectEvents.EffectMomentAttached, 1));
        Assert.AreEqual(Gw2SkillEffectPlacement.PlacedAtTarget, Gw2SkillEffectMoments.PlacementOf(SkillEffectEvents.Hit, SkillEffectEvents.DetachedAttachArgument));
    }
}
