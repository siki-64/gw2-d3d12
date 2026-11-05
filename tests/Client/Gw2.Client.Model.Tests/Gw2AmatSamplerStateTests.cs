using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatSamplerStateTests
{
    [TestMethod]
    public void AnisotropicFilteringUpgradesOnlyLinearMinifyAndMagnify()
    {
        Assert.AreEqual(0x280u, new Gw2AmatSamplerState(0).WithAnisotropicFiltering().Flags);
        Assert.AreEqual(0x240u, new Gw2AmatSamplerState(0x40).WithAnisotropicFiltering().Flags);
        Assert.AreEqual(0x185u, new Gw2AmatSamplerState(0x105).WithAnisotropicFiltering().Flags);
        Assert.AreEqual(0x6C0u, new Gw2AmatSamplerState(0x6C0).WithAnisotropicFiltering().Flags);
    }
}
