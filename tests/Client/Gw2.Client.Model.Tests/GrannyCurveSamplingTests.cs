using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class GrannyCurveSamplingTests
{
    [TestMethod]
    public void LinearCurveBlendsTheTwoControlsAroundTheSampleTime()
    {
        var curve = new Gw2GrannyCurve
        {
            Role = Gw2GrannyCurveRole.Position,
            Degree = 1,
            Dimension = 1,
            Knots = [0f, 1f, 2f],
            Controls = [0f, 10f, 0f],
        };

        Assert.AreEqual(5f, curve.Sample(0.5f)[0], 1e-5f);
        Assert.AreEqual(5f, curve.Sample(1.5f)[0], 1e-5f);
    }
}
