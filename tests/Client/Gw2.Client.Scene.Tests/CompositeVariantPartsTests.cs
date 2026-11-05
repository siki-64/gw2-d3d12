using System.IO;
using System.Linq;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CompositeVariantPartsTests
{
    private const ulong NornMaleRaceToken = 0x2B02D749EE;
    private const ulong HuntsmanVariantToken = 0x705B3A3AA8;

    [TestMethod]
    public void TheHuntsmanVariantResolvesEveryComponentAndKeepsTheBareBody()
    {
        var composite = ReadComposite();
        var race = composite.Races.Single(candidate => candidate.NameToken == NornMaleRaceToken);
        var variant = race.Variants.Single(candidate => candidate.Token == HuntsmanVariantToken);

        var componentParts = Gw2CharacterBodyParts.VariantComponentPartsOf(composite, race, variant);
        Assert.AreEqual(variant.Components.Count, componentParts.Count);
        var parts = Gw2CharacterBodyParts.VariantPartsOf(race, componentParts.Select(entry => entry.Part).ToList());
        CollectionAssert.IsSubsetOf(new uint[] { 0, 1, 2, 3 }, parts.Select(part => part.Type).ToList());
        Assert.AreEqual(parts.Count, parts.Select(part => part.Type).Distinct().Count());
    }

    [TestMethod]
    public void VariantColorsDecodeFromBytesWithTheNeutralByteAsIdentity()
    {
        var composite = ReadComposite();
        var race = composite.Races.Single(candidate => candidate.NameToken == NornMaleRaceToken);
        var colors = race.Variants.SelectMany(variant => new[] { variant.HairColor, variant.PatternColor }).ToList();

        Assert.IsTrue(colors.Contains(Gw2CompositeColor.Identity));
        Assert.IsTrue(race.Variants.SelectMany(variant => variant.Components).Any(component => component.Color0.Contrast > 1f));
    }

    private static Gw2Composite ReadComposite()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        return reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
    }
}
