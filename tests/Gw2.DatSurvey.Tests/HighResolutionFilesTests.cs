using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class HighResolutionFilesTests
{
    private const uint HumanFemaleChestTexture = 128675;
    private const uint HumanFemaleChestHighResolutionTexture = 128676;

    [TestMethod]
    public void ChestTextureRemapsToItsFullResolutionFileWhenConfigured()
    {
        var local = Gw2Installation.LocalDatPath();
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(local) || !File.Exists(local) || string.IsNullOrEmpty(dat) || !File.Exists(dat))
            Assert.Inconclusive("Gw2.dat or Local.dat is not installed.");

        using var archive = FileArchive.Open(local, verifyChecksums: false);
        var files = Gw2HighResolutionFiles.Read(archive);
        Assert.AreEqual(HumanFemaleChestHighResolutionTexture, files.HighResolutionOf(HumanFemaleChestTexture));
        Assert.IsNull(files.HighResolutionOf(HumanFemaleChestHighResolutionTexture));

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var half = reader.ReadTexture(HumanFemaleChestTexture)!.Texture!;
        var full = reader.ReadTexture(HumanFemaleChestHighResolutionTexture)!.Texture!;
        Assert.AreEqual((half.Width * 2, half.Height * 2), (full.Width, full.Height));
    }

    [TestMethod]
    public void SavedMappingLoadsBackOnlyForTheSameSourceVersion()
    {
        var files = new Gw2HighResolutionFiles(new Dictionary<uint, uint> { [HumanFemaleChestTexture] = HumanFemaleChestHighResolutionTexture, [3680069] = 3680070 });
        var path = Path.Combine(Path.GetTempPath(), $"highres-{Guid.NewGuid():N}.bin");
        try
        {
            files.Save(path, 0x6AD7ACF4);
            Assert.IsNull(Gw2HighResolutionFiles.TryLoad(path, 0x6AD14455));
            var loaded = Gw2HighResolutionFiles.TryLoad(path, 0x6AD7ACF4)!;
            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual(HumanFemaleChestHighResolutionTexture, loaded.HighResolutionOf(HumanFemaleChestTexture));
            Assert.AreEqual(3680070u, loaded.HighResolutionOf(3680069));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
