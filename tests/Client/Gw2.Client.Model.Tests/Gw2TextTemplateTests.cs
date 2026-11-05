using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TextTemplateTests
{
    private const uint CopyrightTextId = 1115532;
    private const uint CopyrightYearTextId = 49802;

    [TestMethod]
    public void FillsNamedPlaceholdersAndDropsTheNoSeparatorTag()
    {
        Assert.AreEqual("2026", Gw2TextTemplate.Fill("[nosep]%num1%", Gw2TextParameter.Number(1, 2026, groupDigits: false)));
        Assert.AreEqual("a b %str2%", Gw2TextTemplate.Fill("a %str1% %str2%", Gw2TextParameter.Text(1, "b")));
        Assert.IsTrue(Gw2TextTemplate.DisablesDigitGrouping("[nosep]%num1%"));
    }

    [TestMethod]
    public void BuildsTheCharacterSelectCopyrightWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var yearTemplate = reader.ReadKnownText(CopyrightYearTextId, Gw2Language.English).Text;
        var year = Gw2TextTemplate.Fill(yearTemplate, Gw2TextParameter.Number(1, 2026, !Gw2TextTemplate.DisablesDigitGrouping(yearTemplate)));
        var copyright = Gw2TextTemplate.Fill(reader.ReadKnownText(CopyrightTextId, Gw2Language.English).Text, Gw2TextParameter.Text(1, year));

        Assert.AreEqual("© 2012–2026 ArenaNet, LLC", copyright);
    }

    [TestMethod]
    public void WithoutTagsDropsGrammarTagsAndKeepsEscapedBrackets()
    {
        Assert.AreEqual("Sleeping Jotun", Gw2TextTemplate.WithoutTags("[f]Sleeping Jotun"));
        Assert.AreEqual("[Elite] Golem", Gw2TextTemplate.WithoutTags("[lbracket]Elite[rbracket] Golem[s]"));
        Assert.AreEqual(string.Empty, Gw2TextTemplate.WithoutTags("[null]"));
    }
}
