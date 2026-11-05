using System;

namespace Gw2.Client.Frames;

[Flags]
public enum FrameAnchor
{
    None = 0,
    Left = 0x1,
    Top = 0x2,
    Right = 0x4,
    Bottom = 0x8,
    LeftRight = Left | Right,
    TopBottom = Top | Bottom,
    All = Left | Top | Right | Bottom,
}
