using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests
{
    [TestClass]
    public class Gw2WaypointTests
    {
        [TestMethod]
        public void LionsArchWaypointNinetyTwoSitsWhereTheLiveTravelLanded()
        {
            var datPath = Gw2Installation.DatPath();
            var exePath = Gw2Installation.ExecutablePath();
            if (datPath is null || !File.Exists(datPath) || exePath is null || !File.Exists(exePath)) Assert.Inconclusive("Guild Wars 2 is not installed.");
            using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
            var layouts = new Gw2MapLayouts(reader.ContentIndex, MapLayoutContentLayout.FromExecutable(exePath));

            var waypoints = layouts.FindWaypoints(54, PointOfInterestContentLayout.FromExecutable(exePath));

            Assert.AreEqual(13, waypoints.Count);
            var waypoint = waypoints.Single(candidate => candidate.PointOfInterestDataId == 92);
            Assert.AreEqual(36327f, waypoint.X, 1f);
            Assert.AreEqual(-10092f, waypoint.Y, 1f);
            Assert.AreEqual(-1254f, waypoint.Z, 1f);
        }
    }
}
