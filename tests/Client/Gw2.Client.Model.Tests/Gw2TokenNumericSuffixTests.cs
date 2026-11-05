using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TokenNumericSuffixTests
{
    private const uint MapFileId = 187611;
    private const ulong ZeroPose04 = 0x4000002CDF07C8BAUL;

    [TestMethod]
    public void PropSequenceBit62IsTheNumericSuffixFour()
    {
        Assert.AreEqual(ZeroPose04, Token64.Encode("zeropose04"));
        Assert.AreEqual(4, Token64.NumericSuffix(ZeroPose04));
        Assert.AreEqual(Token64.Encode("zeropose"), Token64.WithoutNumericSuffix(ZeroPose04));
    }

    [TestMethod]
    public void SuffixedPropSequencesResolveThroughTheirBaseSequenceWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var sequenceKeys = reader.ReadAnimSequences()!.Sequences.Select(sequence => sequence.Sequence).ToHashSet();
        var suffixed = Gw2MapProps.TryParse(reader.ReadDocument(MapFileId))!.Placements
            .Select(placement => placement.AnimSequence)
            .OfType<ulong>()
            .Where(token => Token64.NumericSuffix(token) != 0 && !sequenceKeys.Contains(token))
            .ToArray();

        Assert.AreEqual(9, suffixed.Length);
        Assert.IsTrue(suffixed.All(token => token == ZeroPose04));
        Assert.IsTrue(suffixed.All(token => sequenceKeys.Contains(Token64.WithoutNumericSuffix(token))));
    }
}
