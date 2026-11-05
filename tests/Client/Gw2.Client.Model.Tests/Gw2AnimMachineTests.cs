using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AnimMachineTests
{
    private const ulong GroundRunForwardToken = 0x1800001D5934E0;

    [TestMethod]
    public void ReadsStateActionPayloadsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var machines = reader.ReadAnimMachines()!;
        Assert.AreEqual(270, machines.Machines.Count);
        Assert.AreEqual(1481, machines.Models.Count);

        var states = machines.Machines[0].States;
        var run = states.Single(state => state.Name == "Ground Run Forward");
        Assert.AreEqual(GroundRunForwardToken, run.Actions.Single(action => action.Kind == Gw2AnimActionKind.PlayAnimation).Token);

        var turn = states.Single(state => state.Name == "Ground Run Forward Left").Actions.Single(action => action.Kind == Gw2AnimActionKind.SetRotation);
        Assert.AreEqual(4, turn.Rotation.Count);
        Assert.AreEqual(1f, MathF.Sqrt(turn.Rotation.Sum(component => component * component)), 1e-4f);
    }
}
