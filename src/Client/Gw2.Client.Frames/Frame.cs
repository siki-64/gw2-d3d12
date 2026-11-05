using System;
using System.Collections.Generic;

namespace Gw2.Client.Frames;

public class Frame
{
    internal readonly List<Frame> ChildrenByLayer = new();
    private readonly List<List<FrameContentEntry>> contentLayers = new();

    public int Id { get; internal set; }
    public Frame? Parent { get; internal set; }
    public int ChildIndex { get; internal set; }
    public int SiblingLayer { get; internal set; }
    public FrameCreationFlags CreationFlags { get; internal set; }

    public FrameAnchor Anchor { get; set; }
    public FrameMargins Margins { get; set; }
    public int ScrollOffsetX { get; set; }
    public int ScrollOffsetY { get; set; }
    public FrameRect? ClipRect { get; set; }
    public bool IsHidden { get; set; }
    public bool DrawsItself { get; set; }
    public bool ReceivesMouse { get; set; }
    public bool IsVisibleWindow { get; set; } = true;
    public float Opacity { get; set; } = 1f;

    public FrameRect ScreenRect { get; internal set; }
    public int DrawLevel { get; internal set; }
    public FrameSize? CachedNativeSize { get; internal set; }

    public IReadOnlyList<Frame> Children => ChildrenByLayer;
    public IReadOnlyList<IReadOnlyList<FrameContentEntry>> ContentLayers => contentLayers;

    public virtual FrameSize MeasureNativeSize(FrameSize available) => default;

    public virtual void DrawSelf(FrameRect viewport) { }

    public void SetContent(int layer, IEnumerable<object> models)
    {
        while (contentLayers.Count <= layer)
            contentLayers.Add(new List<FrameContentEntry>());
        var entries = contentLayers[layer];
        entries.Clear();
        var opacityByte = (byte)MathF.Round(Math.Clamp(Opacity, 0f, 1f) * 255f);
        foreach (var model in models)
            entries.Add(new FrameContentEntry(model, opacityByte));
    }

    public void ClearContent() => contentLayers.Clear();
}
