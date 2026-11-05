using System;
using System.IO;
using System.Linq;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class UnderwearDyesTests
{
    [TestMethod]
    public void EveryUnderwearDyeOfEveryRaceAndSexResolvesToAColor()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        var dyes = CharacterUnderwearDyes.Current;
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2ColorPalettes(reader.ContentIndex, ColorPaletteContentLayout.Current);
        Assert.AreEqual(CharacterUnderwearDyes.RaceCount * CharacterUnderwearDyes.SexCount * CharacterUnderwearDyes.ColorsPerCharacter, dyes.Colors.Count);
        for (var race = 0; race < CharacterUnderwearDyes.RaceCount; race++)
        for (var sex = 0; sex < CharacterUnderwearDyes.SexCount; sex++)
        {
            var colors = dyes.ColorsOf(race, sex);
            Assert.IsTrue(colors.Any(color => color != Guid.Empty), $"{race}/{sex}");
            foreach (var color in colors.Where(color => color != Guid.Empty))
                Assert.IsNotNull(palettes.FindColorByContentKey(dyes.ColorContentType, color, 1), $"{race}/{sex} {color}");
        }
    
    }

    [TestMethod]
    public void EveryRaceHasLegsUnderwearAndEveryFemaleHasChestUnderwear()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
        for (var race = 0; race < Gw2CharacterRaces.RaceCount; race++)
        for (var sex = 0; sex < Gw2CharacterRaces.SexCount; sex++)
        {
            var name = Gw2CharacterRaces.CompositeRaceNameOf(race, sex)!;
            var parts = Gw2CharacterBodyParts.PartsOf(Gw2CharacterBodyParts.FindRace(composite, name)!, null, null, sex);
            var underwear = parts.Where(part => part.Name.StartsWith("underwear", StringComparison.Ordinal)).ToList();
            Assert.AreEqual(sex == Gw2CharacterBodyParts.FemaleSex ? 2 : 1, underwear.Count, name);
        }
    }
}
