using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2PropMaterialConstantTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;
    private const int SampledModels = 200;

    [TestMethod]
    public void ModelConstantTokensAreNameHashesOfTheMaterialShaderConstants()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;

        var bound = 0;
        var unbound = new HashSet<uint>();
        foreach (var modelFileId in map.Props.Placements.Select(placement => placement.ModelFileId).Distinct().Take(SampledModels))
        {
            if (reader.ReadModel(modelFileId) is not { } model) continue;
            foreach (var material in model.Materials)
            {
                if (!material.TryGetFileId(out var materialFileId) || reader.ReadMaterial(materialFileId) is not { } amat) continue;
                var shaderConstantTokens = amat.Shaders
                    .SelectMany(shader => shader.Binaries)
                    .Where(binary => binary.Backend == Gw2AmatShaderBackend.Dx11)
                    .Select(binary => Gw2BgfxShaderBinary.TryParse(binary.Data))
                    .Where(parsed => parsed is not null)
                    .SelectMany(parsed => parsed!.Uniforms)
                    .Where(uniform => uniform.Kind == Gw2ShaderReflectionKind.Constant)
                    .Select(uniform => Token.Encode(uniform.Name))
                    .ToHashSet();
                foreach (var constant in material.Constants ?? [])
                    if (shaderConstantTokens.Contains(constant.Token)) bound++;
                    else unbound.Add(constant.Token);
            }
        }

        Console.WriteLine($"bound {bound} unbound tokens {string.Join(",", unbound.Select(token => $"0x{token:X8}"))}");
        Assert.IsTrue(bound > 100);
        Assert.IsTrue(Token.Encode("speccp") == 0x3567E669u && Token.Encode("modxcol") == 0x8D45B133u);
    }
}
