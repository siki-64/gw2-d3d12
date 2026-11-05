using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2LocomotionSpeedsTests
{
    [TestMethod]
    public void ConvertsRecoveredGroundSpeedsToMetresPerSecond()
    {
        Assert.AreEqual(2.032004f, Gw2LocomotionSpeeds.MetersPerSecond(Gw2LocomotionSpeeds.Walk), 1e-5f);
        Assert.AreEqual(7.467615f, Gw2LocomotionSpeeds.MetersPerSecond(Gw2LocomotionSpeeds.Run), 1e-5f);
        Assert.AreEqual(3.048006f, Gw2LocomotionSpeeds.MetersPerSecond(Gw2LocomotionSpeeds.AuthoredWalk), 1e-5f);
        Assert.AreEqual(6.096012f, Gw2LocomotionSpeeds.MetersPerSecond(Gw2LocomotionSpeeds.AuthoredRun), 1e-5f);
    }

    [TestMethod]
    public void FallsBackToTheSharedBankAuthoringSpeedWhenMoveSpeedIsMissing()
    {
        Assert.AreEqual(3.048006f, Gw2LocomotionSpeeds.AuthoredMetersPerSecond(0f, Gw2LocomotionSpeeds.AuthoredWalk), 1e-5f);
        Assert.AreEqual(6.096012f, Gw2LocomotionSpeeds.AuthoredMetersPerSecond(0f, Gw2LocomotionSpeeds.AuthoredRun), 1e-5f);
    }

    [TestMethod]
    public void PrefersTheAuthoredMoveSpeedOverTheFallback()
    {
        Assert.AreEqual(3.251207f, Gw2LocomotionSpeeds.AuthoredMetersPerSecond(128f, Gw2LocomotionSpeeds.AuthoredWalk), 1e-5f);
    }

    [TestMethod]
    public void PlaybackRateMatchesGroundTravel()
    {
        var walkAuthored = Gw2LocomotionSpeeds.AuthoredMetersPerSecond(Gw2LocomotionSpeeds.AuthoredWalk, Gw2LocomotionSpeeds.AuthoredWalk);
        var timeScale = Gw2LocomotionSpeeds.ClipTimeScale(walkAuthored);

        var groundSpeed = Gw2LocomotionSpeeds.MetersPerSecond(Gw2LocomotionSpeeds.Walk);
        Assert.AreEqual(groundSpeed / walkAuthored, groundSpeed * timeScale, 1e-5f);
        Assert.AreEqual(80f / 120f, groundSpeed * timeScale, 1e-5f);
    }

    [TestMethod]
    public void ZeroAuthoredSpeedKeepsTheClipAtItsNaturalRate()
    {
        Assert.AreEqual(1f, Gw2LocomotionSpeeds.ClipTimeScale(0f));
    }
}
