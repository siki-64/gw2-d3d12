using System;

namespace Gw2.Client.Frames;

[Flags]
public enum FrameCreationFlags
{
    None = 0,
    StopsHitTest = 0x2,
    SkippedByDrawWalk = 0x4,
    FillsParent = 0x8,
    TopFrame = 0x40,
    CenteredOnScreen = 0x80,
    DrawnAfterTopFrame = 0x100,
    DefaultButton = 0x800,
}
