using System;
using System.IO;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class ColorPaletteTests
{
    private const string HumanSkinPalette = "Gw2.Common.Color.Skin Human";

    [TestMethod]
    public void FullNameManglesToTheStoredObjectName()
    {
        Assert.AreEqual("ndW7T.22saL", Gw2ContentName.Mangle(HumanSkinPalette));
    }

    [TestMethod]
    public void NameHashMatchesTheStoredTrailerHash()
    {
        Assert.AreEqual(0x1F92742Fu, Gw2ContentName.Hash("DHhln.Q9PtI"));
    }
}
