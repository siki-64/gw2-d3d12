using System.Numerics;
using Gw2.Client.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2StreakSimulatorTests
{
    [TestMethod]
    public void RowsAreAddedPerSpawnDistanceTravelled()
    {
        var simulator = new Gw2StreakSimulator(10f, 100f, 1f);
        simulator.Advance(0.01f, true, new[] { Vector3.Zero, Vector3.UnitZ });
        simulator.Advance(0.01f, true, new[] { new Vector3(35, 0, 0), new Vector3(35, 0, 1) });
        Assert.AreEqual(4, simulator.Rows.Count);
        Assert.AreEqual(10f, simulator.Rows[1].Positions[0].X, 1e-4f);
        Assert.AreEqual(0.3f, simulator.Rows[3].TextureU, 1e-4f);
    }

    [TestMethod]
    public void RowsExpireAfterTheLifetime()
    {
        var simulator = new Gw2StreakSimulator(10f, 100f, 0.2f);
        simulator.Advance(0.01f, true, new[] { Vector3.Zero });
        simulator.Advance(0.1f, false, new[] { Vector3.Zero });
        Assert.AreEqual(1, simulator.Rows.Count);
        simulator.Advance(0.1f, false, new[] { Vector3.Zero });
        Assert.AreEqual(0, simulator.Rows.Count);
    }
}
