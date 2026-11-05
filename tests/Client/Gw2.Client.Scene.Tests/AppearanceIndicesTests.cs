using System;
using System.IO;
using System.Linq;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class AppearanceIndicesTests
{
    [TestMethod]
    public void ReadsTheIndicesAndTakesSylvariHairColorFromTheSkinColorByte()
    {
        var block = new byte[Gw2AppearanceIndices.Length];
        block[Gw2AppearanceIndices.RaceAt] = 4;
        block[Gw2AppearanceIndices.SexAt] = 1;
        block[Gw2AppearanceIndices.FaceAt] = 3;
        block[Gw2AppearanceIndices.HairColorAt] = 7;
        block[Gw2AppearanceIndices.SkinColorAt] = 9;
        var appearance = Gw2AppearanceIndices.Of(block)!;

        Assert.AreEqual("sylvarifemal", appearance.CompositeRaceName);
        Assert.AreEqual(3, appearance.FaceIndex);
        Assert.AreEqual(9, appearance.HairColorIndex);
        Assert.IsNull(Gw2AppearanceIndices.Of(new byte[10]));
    }

    [TestMethod]
    public void BothSecondHairDyeSetsExistAndHoldColors()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2ColorPalettes(reader.ContentIndex, ColorPaletteContentLayout.Current);
        foreach (var dyeSet in new[] { CharacterScene.SecondHairDyeSet, CharacterScene.SylvariSecondHairDyeSet })
        {
            Assert.IsTrue(palettes.ColorCount(dyeSet) > 0, dyeSet);
            Assert.IsNotNull(palettes.FindColor(dyeSet, 0, 1), dyeSet);
        }
    }

    [TestMethod]
    public void EveryFaceAndHairStyleNamedInTheCompositeResolvesToItsPart()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
        var namesInComposite = composite.Races.SelectMany(race => race.FileData).Select(part => Token64.Encode(part.Name)).ToHashSet();
        for (var race = 0; race < Gw2CharacterRaces.RaceCount; race++)
        for (var sex = 0; sex < Gw2CharacterRaces.SexCount; sex++)
        {
            var name = Gw2CharacterRaces.CompositeRaceNameOf(race, sex)!;
            var bodies = Gw2CharacterBodyParts.FindRace(composite, name)!;
            for (var face = 0; face < bodies.Faces.Count; face++)
                if (namesInComposite.Contains(bodies.Faces[face]))
                    Assert.IsTrue(Gw2CharacterBodyParts.PartsOf(bodies, face, null, 0).Any(part => part.Type == Gw2CharacterBodyParts.FacePartType && Token64.Encode(part.Name) == bodies.Faces[face]), $"{name} face {face}");
            for (var hair = 0; hair < bodies.HairStyles.Count; hair++)
                if (namesInComposite.Contains(bodies.HairStyles[hair]))
                    Assert.IsTrue(Gw2CharacterBodyParts.PartsOf(bodies, null, hair, 0).Any(part => part.Type == Gw2CharacterBodyParts.HairPartType), $"{name} hair {hair}");
        }
    }
}
