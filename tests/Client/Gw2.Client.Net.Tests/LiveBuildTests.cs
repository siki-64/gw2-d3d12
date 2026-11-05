using System;
using Gw2.Client.Net.Login;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class LiveBuildTests
{
    private const string CapturedLatestReply = "209026 3831457 43065736 3831448 20508";

    [TestMethod]
    public void TheCapturedReplyNamesTheBuildTheExecutableAndTheManifest()
    {
        Assert.AreEqual(new LiveBuild(209026, 3831457, 43065736, 3831448, 20508), LiveBuild.Parse(CapturedLatestReply));
    }

    [TestMethod]
    public void TheLatestRequestGoesToTheProgramsAssetHost()
    {
        Assert.AreEqual("http://assetcdn.101.arenanetworks.com/latest64/101", LiveBuild.LatestUri(101).ToString());
    }

    [TestMethod]
    public void AReplyWithoutABuildIsRejected()
    {
        Assert.ThrowsExactly<FormatException>(() => LiveBuild.Parse("<html>maintenance</html>"));
    }
}
