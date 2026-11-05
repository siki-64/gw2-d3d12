using System;
using Gw2.Client.Net.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ServerEndpointTests
{
    [TestMethod]
    public void AnAddressWithoutAPortTakesTheDefaultPort()
    {
        Assert.AreEqual(new ServerEndpoint("127.0.0.1", ServerEndpoint.DefaultAuthServerPort), ServerEndpoint.Parse("127.0.0.1", ServerEndpoint.DefaultAuthServerPort));
    }

    [TestMethod]
    public void AnAddressWithAPortKeepsThePort()
    {
        Assert.AreEqual(new ServerEndpoint("10.0.0.5", 7000), ServerEndpoint.Parse("10.0.0.5:7000", ServerEndpoint.DefaultPortalPort));
    }

    [TestMethod]
    public void AHostNameIsRejectedLikeTheExecutableDoes()
    {
        Assert.ThrowsExactly<FormatException>(() => ServerEndpoint.Parse("auth.example.com", ServerEndpoint.DefaultAuthServerPort));
    }
}
