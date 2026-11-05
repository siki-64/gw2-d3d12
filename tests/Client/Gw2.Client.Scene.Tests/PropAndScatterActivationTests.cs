using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class PropAndScatterActivationTests
{
    private const float LiveViewDistance = 59416f;

    [TestMethod]
    public void PropRectangleRecentersOnlyAfterTheViewMovesPastTheThreshold()
    {
        var activation = new Gw2PropActivation();
        var view = new Vector3(-15449.8f, -5282.8f, -442.7f);

        Assert.IsTrue(activation.Update(view, LiveViewDistance));
        Assert.AreEqual(view.X - Gw2PropActivation.DefaultRadius, activation.Min.X, 1e-3f);
        Assert.IsTrue(activation.Contains(view + new Vector3(Gw2PropActivation.DefaultRadius - 1f, 0f, 0f)));
        Assert.IsFalse(activation.Contains(view + new Vector3(Gw2PropActivation.DefaultRadius, 0f, 0f)));

        Assert.IsFalse(activation.Update(view + new Vector3(Gw2PropActivation.RecenterDistance, 0f, 0f), LiveViewDistance));
        Assert.IsTrue(activation.Update(view + new Vector3(Gw2PropActivation.RecenterDistance + 1f, 0f, 0f), LiveViewDistance));
    }

    [TestMethod]
    public void ScatterBlocksSnapToTheBlockGridAndDiffWhenTheViewCrossesABlock()
    {
        var activation = new Gw2ScatterActivation(Vector2.Zero);
        var released = new List<(int X, int Y)>();
        var activated = new List<(int X, int Y)>();

        Assert.IsTrue(activation.Update(new Vector3(768f, 768f, 0f), LiveViewDistance, released, activated));
        Assert.AreEqual(0, released.Count);
        Assert.AreEqual(21 * 21, activated.Count);
        Assert.IsTrue(activation.IsActive(-10, -10));
        Assert.IsTrue(activation.IsActive(10, 10));
        Assert.IsFalse(activation.IsActive(11, 0));

        released.Clear();
        activated.Clear();
        Assert.IsFalse(activation.Update(new Vector3(800f, 768f, 0f), LiveViewDistance, released, activated));
        Assert.IsTrue(activation.Update(new Vector3(768f + 1536f, 768f, 0f), LiveViewDistance, released, activated));
        Assert.AreEqual(21, released.Count);
        Assert.AreEqual(21, activated.Count);
        Assert.IsTrue(released.TrueForAll(block => block.X == -10));
        Assert.IsTrue(activated.TrueForAll(block => block.X == 11));
    }

    [TestMethod]
    public void PlacementGridReturnsEveryPlacementThatCrossesTheRectangleEdge()
    {
        var random = new System.Random(1595);
        var placements = new List<Vector2>();
        for (var index = 0; index < 20000; index++) placements.Add(new Vector2(random.NextSingle() * 80000f - 40000f, random.NextSingle() * 80000f - 40000f));
        var grid = new Gw2PlacementGrid<Vector2>(placements, placement => placement);
        var activation = new Gw2PropActivation();
        Gw2PlacementRect? previous = null;
        var candidates = new List<Vector2>();

        foreach (var view in new[] { new Vector3(-15449.8f, -5282.8f, 0f), new Vector3(-15200f, -5282.8f, 0f), new Vector3(-15200f, -4900f, 0f), new Vector3(2000f, 3000f, 0f) })
        {
            var before = previous;
            var wasInside = new HashSet<Vector2>(placements.FindAll(placement => before is { } rect && Inside(rect, placement)));
            Assert.IsTrue(activation.Update(view, LiveViewDistance));
            var current = new Gw2PlacementRect(activation.Min, activation.Max);
            candidates.Clear();
            grid.CollectChanged(previous, current, candidates);
            var candidateSet = new HashSet<Vector2>(candidates);
            foreach (var placement in placements)
                if (wasInside.Contains(placement) != Inside(current, placement)) Assert.IsTrue(candidateSet.Contains(placement));
            if (previous is { } prior && Vector2.Distance(prior.Min, current.Min) < 1000f) Assert.IsTrue(candidates.Count < wasInside.Count / 4);
            previous = current;
        }
    }

    private static bool Inside(Gw2PlacementRect rect, Vector2 position) =>
        position.X >= rect.Min.X && position.Y >= rect.Min.Y && position.X < rect.Max.X && position.Y < rect.Max.Y;
}
