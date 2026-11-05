using Gw2.Client.Net.Login;
using Gw2.Client.Net.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class CustomServerSettingsTests
{
    private static readonly LoginServerSettings Saved = new("10.0.0.5:6112", "10.0.0.5", @"C:\servers\portal.cer", "AB01");

    [TestMethod]
    public void TheFieldsAndTheSelectionSurviveARoundTrip()
    {
        var state = new CustomServerState(Saved, IsSelected: true);

        Assert.AreEqual(state, CustomServerSettings.Parse(CustomServerSettings.ToXml(state)));
    }

    [TestMethod]
    public void CommandLineFieldsOverrideTheSavedOnesAndSelectCustom()
    {
        var merged = CustomServerSettings.WithCommandLine(new CustomServerState(Saved, IsSelected: false), LoginServerSettings.Retail with { AuthServer = "127.0.0.1" });

        Assert.AreEqual(Saved with { AuthServer = "127.0.0.1" }, merged.Servers);
        Assert.IsTrue(merged.IsSelected);
    }

    [TestMethod]
    public void NoCommandLineServersKeepTheSavedState()
    {
        var saved = new CustomServerState(Saved, IsSelected: false);

        Assert.AreEqual(saved, CustomServerSettings.WithCommandLine(saved, LoginServerSettings.Retail));
    }
}
