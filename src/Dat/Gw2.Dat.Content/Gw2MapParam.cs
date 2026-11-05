using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed class Gw2MapParam
{
    public Gw2MapParam(Vector4 rect, uint flags, ulong guid)
    {
        Rect = rect;
        Flags = flags;
        Guid = guid;
    }

    public Vector4 Rect { get; }
    public uint Flags { get; }
    public ulong Guid { get; }

    public static Gw2MapParam? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapParam");
        if (root is null) return null;

        var rect = root.Fields.GetValueOrDefault("rect") is float[] values && values.Length >= 4
            ? new Vector4(values[0], values[1], values[2], values[3])
            : Vector4.Zero;

        return new(rect, PackValue.AsU32(root.Fields.GetValueOrDefault("flags")), PackValue.AsU64(root.Fields.GetValueOrDefault("guid")));
    }
}
