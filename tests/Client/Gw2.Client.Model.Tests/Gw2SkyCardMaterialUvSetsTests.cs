using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkyCardMaterialUvSetsTests
{
    private const uint SingleTextureCardMapFileId = 191000;
    private const uint SingleTextureCardMaterialFileId = 1019343;

    [TestMethod]
    public void MissingSetsTakeTheFullTextureRect()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMap(SingleTextureCardMapFileId)!.Environment!.SkyCards
            .Select(card => card.Material)
            .First(candidate => candidate is not null && candidate.MaterialFile.TryGetFileId(out var fileId) && fileId == SingleTextureCardMaterialFileId)!;
        Assert.AreEqual(1, material.Textures.Count);

        var sets = Gw2SkyCardMaterialUvSets.Of(material.Textures.Select(texture => texture.TextureUV).ToArray());

        Assert.AreEqual(Gw2SkyCardMaterialUvSets.SetCount, sets.Length);
        Assert.AreSame(material.Textures[0].TextureUV, sets[0]);
        foreach (var set in sets.Skip(1)) CollectionAssert.AreEqual(new[] { 0f, 1f, 0f, 1f }, set);
    }
}
