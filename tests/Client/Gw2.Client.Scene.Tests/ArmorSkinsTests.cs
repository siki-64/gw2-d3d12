using System.IO;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class ArmorSkinsTests
{
    private static readonly uint[] CapturedArmorSkins = [14141, 14179, 14131, 14148, 14249, 10243];
    private static readonly ushort[] CapturedDyes = [6, 592, 147, 1497];

    [TestMethod]
    public void CapturedArmorSkinsResolveToCompositePartsAndDyesToColors()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
        var skins = new Gw2Skins(reader.ContentIndex, SkinContentLayout.Current);
        for (var raceIndex = 0; raceIndex < Gw2CharacterRaces.RaceCount; raceIndex++)
        for (var sex = 0; sex < Gw2CharacterRaces.SexCount; sex++)
        {
        var name = Gw2CharacterRaces.CompositeRaceNameOf(raceIndex, sex)!;
        var race = Gw2CharacterBodyParts.FindRace(composite, name)!;
        foreach (var skinDataId in CapturedArmorSkins)
        {
            var token = skins.Find(skinDataId)?.ArmorPartToken;
            Assert.IsNotNull(token, $"{name} skin {skinDataId}");
            Assert.IsNotNull(Gw2CharacterBodyParts.ArmorPartOf(composite, race, token.Value), $"{name} skin {skinDataId} token {token:x}");
        }
        }
        var palettes = new Gw2ColorPalettes(reader.ContentIndex, ColorPaletteContentLayout.Current);
        foreach (var dye in CapturedDyes)
            Assert.IsNotNull(palettes.FindColorByDataId(CharacterUnderwearDyes.Current.ColorContentType, dye, 1), $"dye {dye}");
    }
}
