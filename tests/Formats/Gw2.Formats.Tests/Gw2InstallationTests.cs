using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class Gw2InstallationTests
{
    [TestMethod]
    public void LooksBesideTheRunningClientFirst()
    {
        Assert.AreEqual(Path.GetDirectoryName(Environment.ProcessPath), Gw2Installation.CandidateDirectories().First());
    }

    [TestMethod]
    public void LooksInTheProgramFilesFolderTheInstallerDefaultsTo()
    {
        var programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Guild Wars 2");

        CollectionAssert.Contains(Gw2Installation.CandidateDirectories().ToList(), programFiles);
    }

    [TestMethod]
    public void FindsTheDatBesideTheExecutable()
    {
        var executable = Gw2Installation.ExecutablePath();
        if (executable is null) Assert.Inconclusive("Guild Wars 2 is not installed.");

        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(executable)!, Gw2Installation.DatFileName), Gw2Installation.DatPath(), ignoreCase: true);
    }
}
