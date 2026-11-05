using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AnimSequenceMovesTests
{
    private const uint AnimSequencesFileId = 184788;
    private const ulong ChargedSequenceToken = 143253359351;
    private const ulong MovingSequenceToken = 281634;
    private const ulong MovingVariantToken = 10391123015720;

    [TestMethod]
    public void ReadsChargeStagesAndStepMovesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var sequences = Gw2AnimSequences.TryParse(reader.ReadDocument(AnimSequencesFileId))!;

        var stages = sequences.DataFor(ChargedSequenceToken, Gw2AnimSequences.DefaultAnimationDataToken)!.ChargeStages;
        Assert.AreEqual(4, stages.Count);
        Assert.AreEqual(new Gw2AnimSequenceChargeStage(560, 1), stages[0]);
        Assert.AreEqual(new Gw2AnimSequenceChargeStage(200, 4), stages[3]);

        var step = sequences.DataFor(MovingSequenceToken, MovingVariantToken)!.Steps.First(candidate => candidate.Move is not null);
        Assert.AreEqual(1400u, step.DurationMilliseconds);
        Assert.AreEqual(new Vector2(300, 300), step.Move!.Velocity);
        Assert.AreEqual(Quaternion.Identity, step.Move.MoveRotation);
        Assert.AreEqual(Quaternion.Identity, step.Move.FacingRotation);
    }
}
