using System;
using System.IO;
using Gw2.Client.Net.Portal;
using Gw2.Formats;
using Gw2.Protocol.Portal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class SteamAuthTicketTests
{
    [TestMethod]
    public void TheRunningSteamClientIssuesAnAuthSessionTicket()
    {
        if (Environment.GetEnvironmentVariable("GW2_STEAM_TICKET") != "1") Assert.Inconclusive("GW2_STEAM_TICKET is not 1");
        var executable = Gw2Installation.ExecutablePath();
        var dat = Gw2Installation.DatPath();
        if (executable is null || dat is null) Assert.Inconclusive("Guild Wars 2 is not installed.");

        var deployment = SteamApiDeployment.FromExecutable(executable);
        byte[] library;
        using (var archive = FileArchive.Open(dat, verifyChecksums: false)) library = deployment.ReadLibrary(archive);
        var path = SteamAuthTicket.DeployLibrary(library, deployment.FolderName, deployment.LibraryName);

        var login = SteamAuthTicket.Request(path, deployment.AppId);
        var ticket = login.Ticket;
        Assert.AreNotEqual(0UL, login.SteamId);
        var credential = PortalCredential.ForSteamTicket("", ticket);

        Console.WriteLine($"ticket {ticket.Length} bytes, password text {credential.Password.Length} characters");
        Assert.IsTrue(ticket.Length > 0);
        Assert.AreEqual(ticket.Length * 2, credential.Password.Length);
    }
}
