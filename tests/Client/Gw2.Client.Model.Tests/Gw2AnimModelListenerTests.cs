using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AnimModelListenerTests
{
    private const uint AnimMachinesFileId = 184788;

    [TestMethod]
    public void ReadsModelListenersWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var machines = Gw2AnimMachines.TryParse(reader.ReadDocument(AnimMachinesFileId))!;
        var listeners = machines.Models.Select(model => model.Listeners).ToArray();
        Assert.AreEqual(1481, listeners.Length);
        Assert.AreEqual(137, listeners.Count(listener => listener != Guid.Empty));
        Assert.AreEqual(20, listeners.Count(listener => listener == new Guid("cb725920-7e96-43ce-8985-c32640aa35f7")));
        Assert.IsTrue(listeners.Where(listener => listener != Guid.Empty).All(listener => listener.Version == 4));
    }
}
