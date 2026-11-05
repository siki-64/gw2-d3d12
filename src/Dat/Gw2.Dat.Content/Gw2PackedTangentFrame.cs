using System.Numerics;

namespace Gw2.Dat.Content;

public static class Gw2PackedTangentFrame
{
    public const int Size = 12;

    private const float Scale = 2f / 255f;

    public static Vector3 DecodeVector(uint packed)
    {
        var x = ((packed >> 16) & 0xFF) * Scale - 1f;
        var y = ((packed >> 8) & 0xFF) * Scale - 1f;
        var z = (packed & 0xFF) * Scale - 1f;
        var vector = new Vector3(x, y, z);
        var lengthSquared = vector.LengthSquared();
        return lengthSquared >= 1e-12f ? vector / MathF.Sqrt(lengthSquared) : vector;
    }

    public static (Vector3 Normal, Vector3 Tangent, Vector3 Bitangent) Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Size) throw new ArgumentException($"a tangent frame needs {Size} bytes", nameof(frame));
        return (
            DecodeVector(BitConverter.ToUInt32(frame)),
            DecodeVector(BitConverter.ToUInt32(frame[4..])),
            DecodeVector(BitConverter.ToUInt32(frame[8..])));
    }
}
