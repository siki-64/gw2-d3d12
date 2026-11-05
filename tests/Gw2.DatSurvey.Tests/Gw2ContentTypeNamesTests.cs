using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2ContentTypeNamesTests
{
    [TestMethod]
    public void LabelReturnsTheEvidenceNameForANamedType()
    {
        Assert.AreEqual("SkillDef", Gw2ContentTypeNames.Label(0x40));
        Assert.AreEqual("MadLib", Gw2ContentTypeNames.Label(0x104));
        Assert.AreEqual("MissionOpDef", Gw2ContentTypeNames.Label(0xD7));
        Assert.AreEqual("WvwObjectiveDef", Gw2ContentTypeNames.Label(0x52));
        Assert.AreEqual("ImageListDef", Gw2ContentTypeNames.Label(0x12));
        Assert.AreEqual("CompanionDef", Gw2ContentTypeNames.Label(0x0A));
        Assert.AreEqual("Audio", Gw2ContentTypeNames.Label(0xCC));
        Assert.AreEqual("LfgDef", Gw2ContentTypeNames.Label(0xFF));
        Assert.AreEqual("LfgDef", Gw2ContentTypeNames.Label(0xFF));
        Assert.AreEqual("PermissionDef", Gw2ContentTypeNames.Label(0x19));
        Assert.AreEqual("UnlockDef", Gw2ContentTypeNames.Label(0x1A1));
    }

    [TestMethod]
    public void LabelFallsBackToThePlaceholderForAnUnnamedType()
    {
        Assert.AreEqual("UnknownContentType_0x6C", Gw2ContentTypeNames.Label(0x6C));
        Assert.AreEqual("UnknownContentType_0x8D", Gw2ContentTypeNames.Label(0x8D));
        Assert.AreEqual("UnknownContentType_0x1A5", Gw2ContentTypeNames.Label(0x1A5));
    }

    [TestMethod]
    public void IsUnnamedAgreesWithTryGet()
    {
        Assert.IsFalse(Gw2ContentTypeNames.IsUnnamed(0x40));
        Assert.IsTrue(Gw2ContentTypeNames.TryGet(0x40, out _));
        Assert.IsTrue(Gw2ContentTypeNames.IsUnnamed(0x6C));
        Assert.IsFalse(Gw2ContentTypeNames.TryGet(0x6C, out _));
    }

    [TestMethod]
    public void NoEvidenceNameLooksLikeAPlaceholder()
    {
        foreach (var pair in Gw2ContentTypeNames.All)
        {
            Assert.IsFalse(Gw2ContentTypeNames.IsPlaceholder(pair.Value), $"type {pair.Key:x} is named {pair.Value}");
        }
    }

    [TestMethod]
    public void IsPlaceholderRecognisesOnlyThePlaceholderPrefix()
    {
        Assert.IsTrue(Gw2ContentTypeNames.IsPlaceholder(Gw2ContentTypeNames.Label(0x6C)));
        Assert.IsFalse(Gw2ContentTypeNames.IsPlaceholder("SkillDef"));
        Assert.IsFalse(Gw2ContentTypeNames.IsPlaceholder("UnknownContent"));
        Assert.IsFalse(Gw2ContentTypeNames.IsPlaceholder(string.Empty));
    }
}
