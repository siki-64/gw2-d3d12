using System.Linq;
using Gw2.Client.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrGpuSceneGroupsTests
{
    [TestMethod]
    public void GroupsByKeyInFirstAppearanceOrderWithPackedRanges()
    {
        var groups = GrGpuSceneGroups<string>.Build(6, [(0, "rock"), (1, "tree"), (2, "rock"), (4, "bush"), (5, "rock")]);

        CollectionAssert.AreEqual(new[] { "rock", "tree", "bush" }, groups.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { 3, 1, 1 }, groups.Capacity.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 3, 4 }, groups.First.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 0, GrGpuSceneGroups<string>.NoGroup, 2, 0 }, groups.GroupOfInstance.ToArray());
    }

    [TestMethod]
    public void EmptySceneHasNoGroups()
    {
        var groups = GrGpuSceneGroups<string>.Build(2, []);

        Assert.AreEqual(0, groups.Count);
        CollectionAssert.AreEqual(new[] { GrGpuSceneGroups<string>.NoGroup, GrGpuSceneGroups<string>.NoGroup }, groups.GroupOfInstance.ToArray());
    }
}
