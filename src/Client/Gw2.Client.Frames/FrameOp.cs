using System.Collections.Generic;

namespace Gw2.Client.Frames;

public abstract record FrameOp;

public sealed record FrameDrawSelfOp(Frame Frame) : FrameOp;

public sealed record FrameDrawModelsOp(IReadOnlyList<FrameContentEntry> Models) : FrameOp;

public sealed record FrameSetClipOp(FrameRect Clip) : FrameOp;
