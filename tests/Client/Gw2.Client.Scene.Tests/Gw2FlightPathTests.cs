using System.Numerics;
using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class Gw2FlightPathTests
{
    [TestMethod]
    public void EyeMovesAlongWaypointsAtTheSpeed()
    {
        var flight = new Gw2FlightPath([Vector3.Zero, new Vector3(1000f, 0f, 0f), new Vector3(1000f, 500f, 0f)], 100f);

        Assert.AreEqual(1500f, flight.Length);
        Assert.AreEqual(15f, flight.DurationSeconds);
        Assert.AreEqual(new Vector3(500f, 0f, 0f), flight.Eye(5f));
        Assert.AreEqual(Vector3.UnitX, flight.Front(5f));
        Assert.AreEqual(new Vector3(1000f, 200f, 0f), flight.Eye(12f));
        Assert.AreEqual(Vector3.UnitY, flight.Front(12f));
    }

    [TestMethod]
    public void EyeStopsAtTheLastWaypoint()
    {
        var flight = new Gw2FlightPath([Vector3.Zero, new Vector3(0f, 100f, 0f)], 50f);

        Assert.AreEqual(new Vector3(0f, 100f, 0f), flight.Eye(10f));
        Assert.AreEqual(Vector3.Zero, flight.Eye(-1f));
    }
}
