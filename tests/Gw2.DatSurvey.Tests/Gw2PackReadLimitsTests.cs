using Gw2.Dat.Archive;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2PackReadLimitsTests
{
    [TestMethod]
    public void CompositePacksUseCompositeLimits()
        => Assert.AreSame(Gw2PackReadLimits.Composite, Gw2PackReadLimits.ForPackType("cmpc"));

    [TestMethod]
    public void MapPacksUseMapLimits()
        => Assert.AreSame(Gw2PackReadLimits.Map, Gw2PackReadLimits.ForPackType(Gw2FourCc.Map));

    [TestMethod]
    public void MapLimitsExceedDefaults()
    {
        var defaults = new Gw2PackReadLimits();
        Assert.IsTrue(Gw2PackReadLimits.Map.MaxValues > defaults.MaxValues);
        Assert.IsTrue(Gw2PackReadLimits.Map.MaxArrayElements > defaults.MaxArrayElements);
    }

    [TestMethod]
    public void OtherPacksUseDefaultLimits()
        => Assert.IsNull(Gw2PackReadLimits.ForPackType(Gw2FourCc.Model));
}
