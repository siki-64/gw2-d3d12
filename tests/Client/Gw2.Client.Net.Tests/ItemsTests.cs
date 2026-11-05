using System;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ItemsTests
{
    [TestMethod]
    public void ReadsTheWeaponTypesOfTheRecordedRevenantsWeapons()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var items = new Gw2Items(reader.ContentIndex, ItemContentLayout.Current);

        Assert.AreEqual(WeaponType.Hammer, items.WeaponTypeOf(15517));
        Assert.AreEqual(WeaponType.Sword, items.WeaponTypeOf(15314));
        Assert.AreEqual(WeaponType.Shield, items.WeaponTypeOf(15398));
    }

    [TestMethod]
    public void ReadsTheDefaultSkinsOfTheRecordedWeapons()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var items = new Gw2Items(reader.ContentIndex, ItemContentLayout.Current);
        var skinType = SkinContentLayout.Current.SkinContentType;

        Assert.AreEqual(4112u, items.DefaultSkinDataIdOf(skinType, 15314));
        Assert.AreEqual(6259u, items.DefaultSkinDataIdOf(skinType, 29167));
        Assert.AreEqual(14216u, items.DefaultSkinDataIdOf(skinType, 110142));
    }
}
