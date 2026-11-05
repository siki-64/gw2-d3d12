using System.Linq;
using Gw2.Client.Frames;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Frames.Tests;

[TestClass]
public sealed class FrameTreeTests
{
    [TestMethod]
    public void CharacterSelectSceneFillsTheScreenThroughUiRoot()
    {
        var tree = new FrameTree(1024, 768);
        var uiRoot = tree.CreateChild(null, FrameCreationFlags.FillsParent, 0);
        var scene = tree.CreateChild(uiRoot, FrameCreationFlags.FillsParent, 11);
        var bottomBar = tree.CreateChild(scene, FrameCreationFlags.None, 22);
        bottomBar.Anchor = FrameAnchor.LeftRight | FrameAnchor.Top;
        bottomBar.Margins = new FrameMargins(0, 0, 0, 64);
        tree.UpdateScreenRects();

        Assert.AreEqual(1, tree.Desktop.Id);
        Assert.AreEqual(2, uiRoot.Id);
        Assert.AreEqual(new FrameRect(0, 0, 1024, 768), scene.ScreenRect);
        Assert.AreEqual(new FrameRect(0, 0, 1024, 64), bottomBar.ScreenRect);
        Assert.AreSame(scene, tree.GetChild(uiRoot, 11));
    }

    [TestMethod]
    [DataRow(FrameAnchor.LeftRight, 10f, 20f, 10f, 380f)]
    [DataRow(FrameAnchor.Left, 10f, 20f, 10f, 20f)]
    [DataRow(FrameAnchor.Right, 30f, 10f, 370f, 390f)]
    [DataRow(FrameAnchor.None, 25f, 25f, 175f, 225f)]
    public void HorizontalAnchorsPlaceTheEdgesFromTheMargins(FrameAnchor anchor, float marginLeft, float marginRight, float expectedLeft, float expectedRight)
    {
        var tree = new FrameTree(400, 300);
        var frame = tree.CreateChild(null, FrameCreationFlags.None, 0);
        frame.Anchor = anchor | FrameAnchor.TopBottom;
        frame.Margins = new FrameMargins(marginLeft, 0, marginRight, 0);

        var layout = tree.LayoutRect(frame);

        Assert.AreEqual(expectedLeft, layout.Left);
        Assert.AreEqual(expectedRight, layout.Right);
    }

    [TestMethod]
    public void ZeroSizedRightAnchoredFrameTakesItsNativeSizeLeftOfTheEdge()
    {
        var tree = new FrameTree(400, 300);
        var frame = tree.CreateChild(null, FrameCreationFlags.None, 0, new FixedNativeSizeFrame(50, 20));
        frame.Anchor = FrameAnchor.Right | FrameAnchor.Top;
        frame.Margins = new FrameMargins(10, 5, 10, 5);
        tree.UpdateScreenRects();

        Assert.AreEqual(new FrameRect(340, 5, 390, 25), frame.ScreenRect);
    }

    [TestMethod]
    public void ScreenEdgesSnapTo128ths()
    {
        Assert.AreEqual(1f / 128f, FrameRect.SnapTo128th(0.006f));
        Assert.AreEqual(0f, FrameRect.SnapTo128th(0.003f));
    }

    [TestMethod]
    public void SiblingsStayAscendingByLayerAndInCreationOrderWithinALayer()
    {
        var tree = new FrameTree(100, 100);
        var a = tree.CreateChild(null, FrameCreationFlags.None, 0, siblingLayer: 1);
        var b = tree.CreateChild(null, FrameCreationFlags.None, 1, siblingLayer: 0);
        var c = tree.CreateChild(null, FrameCreationFlags.None, 2, siblingLayer: 1);
        var d = tree.CreateChild(null, FrameCreationFlags.None, 3, siblingLayer: 0);

        CollectionAssert.AreEqual(new[] { b, d, a, c }, tree.Desktop.Children.ToArray());
    }

    [TestMethod]
    public void ALayerChangeStartsTheNextLevelBelowTheDeepestSiblingSubtree()
    {
        var tree = new FrameTree(100, 100);
        var first = tree.CreateChild(null, FrameCreationFlags.None, 0, siblingLayer: 0);
        var grandchild = tree.CreateChild(tree.CreateChild(first, FrameCreationFlags.None, 0), FrameCreationFlags.None, 0);
        var sameLayer = tree.CreateChild(null, FrameCreationFlags.None, 1, siblingLayer: 0);
        var nextLayer = tree.CreateChild(null, FrameCreationFlags.None, 2, siblingLayer: 1);

        FrameDrawList.Build(tree);

        Assert.AreEqual(3, grandchild.DrawLevel);
        Assert.AreEqual(1, sameLayer.DrawLevel);
        Assert.AreEqual(4, nextLayer.DrawLevel);
    }

    [TestMethod]
    public void WindowsAndTheTopFrameDrawDeeperThanTheRootTree()
    {
        var tree = new FrameTree(100, 100);
        tree.CreateChild(null, FrameCreationFlags.None, 0);
        var top = tree.CreateChild(null, FrameCreationFlags.TopFrame, 1);
        var window = tree.CreateChild(null, FrameCreationFlags.None, 2);
        tree.AddWindow(window);

        var list = FrameDrawList.Build(tree);

        Assert.AreEqual(2, window.DrawLevel);
        Assert.AreEqual(3, top.DrawLevel);
        Assert.AreSame(top, list.DrawOrder[^1]);
    }

    [TestMethod]
    public void ModelsOnOneLevelInterleaveByContentLayer()
    {
        var tree = new FrameTree(100, 100);
        var left = tree.CreateChild(null, FrameCreationFlags.None, 0);
        var right = tree.CreateChild(null, FrameCreationFlags.None, 1);
        left.SetContent(0, new object[] { "left background" });
        left.SetContent(2, new object[] { "left text" });
        right.SetContent(1, new object[] { "right background" });
        right.SetContent(2, new object[] { "right text" });

        var ops = FrameOpBuilder.Build(tree, FrameDrawList.Build(tree));

        Assert.IsInstanceOfType(ops[0], typeof(FrameSetClipOp));
        var models = ((FrameDrawModelsOp)ops.Single(op => op is FrameDrawModelsOp)).Models.Select(entry => entry.Model).ToArray();
        CollectionAssert.AreEqual(new object[] { "left background", "right background", "left text", "right text" }, models);
    }

    [TestMethod]
    public void HitTestPicksTheLastDrawnFrameUnderThePoint()
    {
        var tree = new FrameTree(100, 100);
        var below = tree.CreateChild(null, FrameCreationFlags.None, 0);
        var above = tree.CreateChild(null, FrameCreationFlags.None, 1);
        foreach (var frame in new[] { below, above })
        {
            frame.Anchor = FrameAnchor.All;
            frame.ReceivesMouse = true;
        }
        tree.UpdateScreenRects();

        Assert.AreSame(above, FrameDrawList.Build(tree).HitTest(50, 50));
    }

    private sealed class FixedNativeSizeFrame(float width, float height) : Frame
    {
        public override FrameSize MeasureNativeSize(FrameSize available) => new(width, height);
    }
}
