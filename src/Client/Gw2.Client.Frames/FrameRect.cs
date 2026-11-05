using System;

namespace Gw2.Client.Frames;

public readonly record struct FrameRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public bool Contains(float x, float y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public static float SnapTo128th(float value) => MathF.Floor(value * 128f + 0.5f) / 128f;
}

public readonly record struct FrameSize(float Width, float Height);

public readonly record struct FrameMargins(float Left, float Top, float Right, float Bottom);

public readonly record struct FrameContentEntry(object Model, byte Opacity);
