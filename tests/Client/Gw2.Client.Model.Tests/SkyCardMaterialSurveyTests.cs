using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class SkyCardMaterialSurveyTests
{
    [TestMethod]
    [TestCategory("Survey")]
    public void ListSkyCardMaterials()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var index = Gw2DatTypeIndex.Load(reader.Archive, dat);
        var materials = new Dictionary<string, (int Cards, SortedSet<uint> Maps, string Shape)>();
        foreach (var mapFileId in index.FileIdsOfType("mapc"))
        {
            Gw2MapBundle? map;
            try { map = reader.ReadMap(mapFileId); } catch { continue; }
            if (map?.Environment is not { } environment) continue;
            foreach (var card in environment.SkyCards)
            {
                if (card.Material is not { } material) continue;
                var key = material.MaterialFile.TryGetFileId(out var amat) ? amat.ToString() : "none";
                var shape = $"flags=0x{card.Flags:X} textures={material.Textures.Count} constants={string.Join(",", material.Constants.Select(constant => $"{constant.Token:X8}"))} anim={material.TextureAnimation is not null} flip={material.Flipbook is not null}";
                materials[key] = materials.TryGetValue(key, out var entry) ? (entry.Cards + 1, entry.Maps, entry.Shape) : (1, new SortedSet<uint>(), shape);
                materials[key].Maps.Add(mapFileId);
            }
        }
        foreach (var (amat, entry) in materials.OrderByDescending(pair => pair.Value.Cards))
            Console.WriteLine($"AMAT {amat}: {entry.Cards} cards, maps {string.Join(",", entry.Maps.Take(6))}{(entry.Maps.Count > 6 ? $" (+{entry.Maps.Count - 6})" : "")} {entry.Shape}");
    }
}
