using System.Collections.Generic;

namespace Gw2.Client.Frames;

public sealed class FrameDrawList
{
    private const FrameCreationFlags SkippedChildFlags =
        FrameCreationFlags.SkippedByDrawWalk | FrameCreationFlags.TopFrame | FrameCreationFlags.DrawnAfterTopFrame;

    private readonly List<Frame> drawOrder = new();
    private readonly List<Frame> hitTestOrder = new();

    public IReadOnlyList<Frame> DrawOrder => drawOrder;
    public IReadOnlyList<Frame> HitTestOrder => hitTestOrder;

    public static FrameDrawList Build(FrameTree tree)
    {
        var list = new FrameDrawList();
        var deepest = 0;
        list.Walk(tree.Desktop, 0, ref deepest);
        Frame? drawnAfterTopFrame = null;
        foreach (var window in tree.Windows)
        {
            if ((window.CreationFlags & FrameCreationFlags.DrawnAfterTopFrame) != 0)
                drawnAfterTopFrame = window;
            else if (window.IsVisibleWindow)
                list.Walk(window, deepest + 1, ref deepest);
        }
        if (tree.TopFrame != null)
            list.Walk(tree.TopFrame, deepest + 1, ref deepest);
        if (drawnAfterTopFrame != null)
            list.Walk(drawnAfterTopFrame, deepest + 1, ref deepest);
        return list;
    }

    public Frame? HitTest(float x, float y)
    {
        for (var i = hitTestOrder.Count - 1; i >= 0; i--)
        {
            var frame = hitTestOrder[i];
            if (!frame.IsHidden && frame.ScreenRect.Contains(x, y))
                return frame;
        }
        return null;
    }

    private void Walk(Frame frame, int level, ref int callerDeepest)
    {
        drawOrder.Add(frame);
        if (frame.ReceivesMouse || (frame.CreationFlags & FrameCreationFlags.StopsHitTest) != 0)
            hitTestOrder.Add(frame);
        frame.DrawLevel = level;

        var subtreeDeepest = level;
        var childLevel = level + 1;
        int? previousLayer = null;
        foreach (var child in frame.ChildrenByLayer)
        {
            if (previousLayer.HasValue && previousLayer.Value != child.SiblingLayer)
                childLevel = subtreeDeepest + 1;
            previousLayer = child.SiblingLayer;
            if ((child.CreationFlags & SkippedChildFlags) == 0)
                Walk(child, childLevel, ref subtreeDeepest);
        }

        if (callerDeepest < subtreeDeepest)
            callerDeepest = subtreeDeepest;
    }
}
