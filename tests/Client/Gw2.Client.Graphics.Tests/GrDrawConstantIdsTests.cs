using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrDrawConstantIdsTests
{
    private const int ExeTableRows = 53;
    private const uint BodyMaterialFileId = 0x8585;

    [TestMethod]
    public void TableHoldsEveryRowOfTheExeTable() => Assert.AreEqual(ExeTableRows, GrDrawConstantIds.Count);

    [TestMethod]
    public void UnknownTokensAreNamedConstants() => Assert.AreEqual(GrDrawConstantId.Named, GrDrawConstantIds.Of(GrGlobalConstantTokens.LightBuffer));

    [TestMethod]
    public void BodyMaterialTokensMatchTheIdTableAndTheGlobals()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMaterial(BodyMaterialFileId)!;
        var tokenByName = new Dictionary<string, uint>();
        foreach (var binary in material.Shaders.SelectMany(shader => shader.Binaries))
            foreach (var (name, token) in binary.ConstantTokensByName()) tokenByName.TryAdd(name, token);

        Assert.AreEqual(GrDrawConstantId.ScreenDims, GrDrawConstantIds.Of(tokenByName["ScreenDims"]));
        Assert.AreEqual(GrDrawConstantId.StencilId, GrDrawConstantIds.Of(tokenByName["StencilId"]));
        Assert.AreEqual(GrDrawConstantId.ViewProjection, GrDrawConstantIds.Of(tokenByName["ViewProjection"]));
        Assert.AreEqual(GrGlobalConstantTokens.LightBuffer, tokenByName["LightBuffer"]);
        Assert.AreEqual(GrGlobalConstantTokens.CharLightCurve, tokenByName["CharLightCurve"]);
        Assert.AreEqual(GrGlobalConstantTokens.BacklightColor, tokenByName["BacklightColor"]);
        Assert.AreEqual(GrGlobalConstantTokens.BacklightDirection, tokenByName["BacklightDirection"]);
        Assert.AreEqual(GrGlobalConstantTokens.BacklightShadowInfluence, tokenByName["BacklightShadowInfluence"]);
        Assert.AreEqual(GrGlobalConstantTokens.WorldToShadowD, tokenByName["WorldToShadowD"]);
    }
}
