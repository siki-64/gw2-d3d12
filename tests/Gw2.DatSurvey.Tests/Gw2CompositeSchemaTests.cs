using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2CompositeSchemaTests
{
    [TestMethod]
    public void RaceSkeletonFileIsAnInlineReferenceInThisBuild()
    {
        var field = Field("PackCompositeRaceDataV20", "skeletonFile");
        Assert.AreEqual(27, field.Kind);
    }

    [TestMethod]
    public void AnimOverrideFilepathIsAnInlineReferenceInThisBuild()
    {
        var field = Field("PackCompositeAnimOverrideV20", "filepath");
        Assert.AreEqual(27, field.Kind);
    }

    private static Gw2PackFieldDefinition Field(string typeName, string fieldName)
    {
        var type = Gw2PackSchemaCatalog.Default.Types.First(t => t.Name == typeName);
        return type.Fields.First(f => f.Name == fieldName);
    }
}
