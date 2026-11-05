using System.Linq;
using Gw2.Client.Scene.Havok;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class MapLayerMaskTests
{
    private const uint BeachMapFileBaseId = 3719751;

    [TestMethod]
    public void MapBodiesCarryTheSceneLayersTheExeMasksSelect()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var layers = world.Bodies.GroupBy(body => body.Layer).ToDictionary(group => group.Key, group => group.Count());
        System.Console.WriteLine(string.Join(", ", layers.Select(pair => $"{pair.Key}:{pair.Value}")));
        Assert.IsTrue(layers.ContainsKey(Gw2HavokMapWorld.TerrainLayer));
        Assert.IsTrue(layers.Keys.All(layer => layer is >= Gw2HavokMapWorld.PropModelLayer and <= Gw2HavokMapWorld.ObsModelLayer));
        foreach (var layer in layers.Keys)
        {
            Assert.IsTrue(Gw2HavokMapWorld.LayerPasses(Gw2HavokMapWorld.CameraSweepMask, layer));
            Assert.AreEqual(layer != Gw2HavokMapWorld.ObsModelLayer, Gw2HavokMapWorld.LayerPasses(Gw2HavokMapWorld.CameraLineOfSightMask, layer));
            Assert.AreEqual(layer == Gw2HavokMapWorld.TerrainLayer, Gw2HavokMapWorld.LayerPasses(Gw2HavokMapWorld.TerrainOnlyMask, layer));
        }
    }
}
