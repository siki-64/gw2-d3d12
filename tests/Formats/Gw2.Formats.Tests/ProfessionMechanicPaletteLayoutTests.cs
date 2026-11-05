using System;
using System.IO;
using System.Linq;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class ProfessionMechanicPaletteLayoutTests
{
    [TestMethod]
    public void ReadsOneMechanicPaletteConfigPerCoreProfession()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = ProfessionMechanicPaletteLayout.FromExecutable(path);

        CollectionAssert.AreEqual(new[]
        {
            new Guid("bbe6804f-2b83-4629-847e-ee6f9e05a396"),
            new Guid("91da317a-a5a2-4b32-a17f-d9cd3bdf8848"),
            new Guid("d4439ec0-6217-4e4a-b764-a34d1c5a8d91"),
            new Guid("efe43bb6-44d2-4ff3-a065-0554300dce1d"),
            new Guid("521e08d1-8471-495b-abb3-96f637b12722"),
            new Guid("7b44d173-af0a-4900-ab36-31d649db49d7"),
            new Guid("e08b061a-8649-47ee-a0da-837e909a7d54"),
            new Guid("c33ec8b1-eeb1-4dc3-9f5d-88bf30e441eb"),
        }, Enumerable.Range(1, 8).Select(profession => layout.PaletteConfigKeysByProfession[(uint)profession]).ToArray());
        CollectionAssert.AreEqual(new uint[] { 3 }, layout.ToolbeltProfessions.ToArray());
    }
}
