using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gw2.Client.Graphics;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrUiMaterialCombineTests
{
    private const uint LiveControlToken = 0x8D80FCFC;

    [TestMethod]
    public void ControlTokenMatchesTheTokenSeenOnLiveSubmodels()
    {
        Assert.AreEqual(LiveControlToken, GrUiMaterialCombine.ControlToken);
    }
}
