using System;
using System.Collections.Generic;

namespace Gw2.Client.Frames;

public sealed class FrameTree
{
    private readonly List<Frame?> framesById = new() { null };
    private readonly Dictionary<(Frame Parent, int ChildIndex), Frame> childByIndex = new();
    private readonly List<Frame> windows = new();

    public FrameTree(float screenWidth, float screenHeight)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Desktop = Register(new Frame());
    }

    public float ScreenWidth { get; private set; }
    public float ScreenHeight { get; private set; }
    public Frame Desktop { get; }
    public Frame? TopFrame { get; private set; }
    public IReadOnlyList<Frame> Windows => windows;

    public FrameRect ScreenBounds => new(0, 0, ScreenWidth, ScreenHeight);

    public void Resize(float screenWidth, float screenHeight)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        foreach (var frame in framesById)
            if (frame != null)
                frame.CachedNativeSize = null;
    }

    public Frame? FromId(int id) => id > 0 && id < framesById.Count ? framesById[id] : null;

    public Frame? GetChild(Frame? parent, int childIndex) =>
        childByIndex.TryGetValue((parent ?? Desktop, childIndex), out var child) ? child : null;

    public T CreateChild<T>(Frame? parent, FrameCreationFlags flags, int childIndex, T frame, int siblingLayer = 0) where T : Frame
    {
        parent ??= Desktop;
        Destroy(GetChild(parent, childIndex));
        frame.Parent = parent;
        frame.ChildIndex = childIndex;
        frame.SiblingLayer = siblingLayer;
        frame.CreationFlags = flags;
        Register(frame);
        childByIndex[(parent, childIndex)] = frame;
        InsertByLayer(parent.ChildrenByLayer, frame);
        if ((flags & FrameCreationFlags.TopFrame) != 0)
            TopFrame = frame;
        return frame;
    }

    public Frame CreateChild(Frame? parent, FrameCreationFlags flags, int childIndex, int siblingLayer = 0) =>
        CreateChild(parent, flags, childIndex, new Frame(), siblingLayer);

    public void AddWindow(Frame window) => windows.Add(window);

    public void Destroy(Frame? frame)
    {
        if (frame == null || frame == Desktop || frame.Id == 0)
            return;
        for (var i = frame.ChildrenByLayer.Count - 1; i >= 0; i--)
            Destroy(frame.ChildrenByLayer[i]);
        frame.Parent!.ChildrenByLayer.Remove(frame);
        childByIndex.Remove((frame.Parent, frame.ChildIndex));
        windows.Remove(frame);
        if (TopFrame == frame)
            TopFrame = null;
        framesById[frame.Id] = null;
        frame.Id = 0;
    }

    public FrameRect LayoutRect(Frame frame)
    {
        if (frame.Parent == null)
            return ScreenBounds;
        var parentRect = LayoutRect(frame.Parent);
        if ((frame.CreationFlags & FrameCreationFlags.FillsParent) != 0)
            return parentRect;

        var margins = frame.Margins;
        var (left, right) = Span(frame.Anchor, FrameAnchor.Left, FrameAnchor.Right, margins.Left, margins.Right, parentRect.Width);
        var (top, bottom) = Span(frame.Anchor, FrameAnchor.Top, FrameAnchor.Bottom, margins.Top, margins.Bottom, parentRect.Height);
        return new FrameRect(left, top, MathF.Max(left, right), MathF.Max(top, bottom));
    }

    public FrameRect ComputeScreenRect(Frame frame)
    {
        var parent = frame.Parent;
        if (parent == null)
            return ScreenBounds;
        if ((frame.CreationFlags & FrameCreationFlags.FillsParent) != 0)
        {
            var parentRect = parent.ScreenRect;
            return new FrameRect(
                parentRect.Left + parent.ScrollOffsetX, parentRect.Top + parent.ScrollOffsetY,
                parentRect.Right + parent.ScrollOffsetX, parentRect.Bottom + parent.ScrollOffsetY);
        }

        var layout = LayoutRect(frame);
        var width = layout.Width;
        var height = layout.Height;
        if (width == 0 || height == 0)
        {
            var native = frame.CachedNativeSize ??= frame.MeasureNativeSize(new FrameSize(
                ScreenWidth > 0 ? ScreenWidth : 100000f, ScreenHeight > 0 ? ScreenHeight : 100000f));
            if (width == 0)
                width = native.Width;
            if (height == 0)
                height = native.Height;
        }

        var x = layout.Left;
        var y = layout.Top;
        if ((frame.Anchor & FrameAnchor.Right) != 0 && layout.Left == layout.Right)
            x -= width;
        if ((frame.Anchor & FrameAnchor.Bottom) != 0 && layout.Top == layout.Bottom)
            y -= height;

        if ((frame.CreationFlags & FrameCreationFlags.CenteredOnScreen) != 0)
        {
            x = (ScreenWidth - width) / 2f;
            y = (ScreenHeight - height) / 2f;
        }
        else
        {
            x += parent.ScreenRect.Left + parent.ScrollOffsetX;
            y += parent.ScreenRect.Top + parent.ScrollOffsetY;
        }

        return new FrameRect(
            FrameRect.SnapTo128th(x), FrameRect.SnapTo128th(y),
            FrameRect.SnapTo128th(x + width), FrameRect.SnapTo128th(y + height));
    }

    public void UpdateScreenRects() => UpdateScreenRects(Desktop);

    private void UpdateScreenRects(Frame frame)
    {
        frame.ScreenRect = ComputeScreenRect(frame);
        foreach (var child in frame.ChildrenByLayer)
            UpdateScreenRects(child);
    }

    private static (float Low, float High) Span(FrameAnchor anchor, FrameAnchor lowEdge, FrameAnchor highEdge, float lowMargin, float highMargin, float extent)
    {
        var anchoredLow = (anchor & lowEdge) != 0;
        var anchoredHigh = (anchor & highEdge) != 0;
        if (anchoredLow && anchoredHigh)
            return (lowMargin, extent - highMargin);
        if (anchoredLow)
            return (lowMargin, highMargin);
        if (anchoredHigh)
            return (extent - lowMargin, extent - highMargin);
        return (extent / 2f - lowMargin, extent / 2f + highMargin);
    }

    private Frame Register(Frame frame)
    {
        frame.Id = framesById.Count;
        framesById.Add(frame);
        return frame;
    }

    private static void InsertByLayer(List<Frame> siblings, Frame frame)
    {
        var index = siblings.FindIndex(sibling => sibling.SiblingLayer > frame.SiblingLayer);
        siblings.Insert(index < 0 ? siblings.Count : index, frame);
    }
}
