using System.Runtime.CompilerServices;

namespace Gw2.Dat.Archive;

public static class Gw2Binary
{
    public static float ReadSingleLittleEndian(ReadOnlySpan<byte> source) =>
        BitConverter.ToSingle(BitConverter.GetBytes(ReadUInt32LittleEndian(source)), 0);

    public static double ReadDoubleLittleEndian(ReadOnlySpan<byte> source) =>
        BitConverter.ToDouble(BitConverter.GetBytes(ReadUInt64LittleEndian(source)), 0);

    public static float ReadHalfLittleEndian(ReadOnlySpan<byte> source)
    {
        var value = ReadUInt16LittleEndian(source);
        var sign = (value >> 15) & 1;
        var exponent = (value >> 10) & 0x1F;
        var mantissa = value & 0x3FF;
        float result;
        if (exponent == 0)
        {
            if (mantissa == 0) return sign == 0 ? 0f : -0f;
            result = mantissa / 1024f * MathF.Pow(2f, -14f);
        }
        else if (exponent == 31)
        {
            return mantissa == 0
                ? (sign == 0 ? float.PositiveInfinity : float.NegativeInfinity)
                : float.NaN;
        }
        else
        {
            result = (1f + mantissa / 1024f) * MathF.Pow(2f, exponent - 15f);
        }

        return sign == 0 ? result : -result;
    }

    public static int Log2(uint value)
    {
        var result = 0;
        while (value > 1)
        {
            value >>= 1;
            result++;
        }

        return result;
    }

    public static string ToHexString(ReadOnlySpan<byte> bytes)
    {
        const string digits = "0123456789ABCDEF";
        var result = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            result[i * 2] = digits[bytes[i] >> 4];
            result[i * 2 + 1] = digits[bytes[i] & 0xF];
        }

        return new string(result);
    }

    public static int ReadAt(Stream stream, Span<byte> destination, long position)
    {
        lock (stream)
        {
            stream.Seek(position, SeekOrigin.Begin);
            return stream.Read(destination);
        }
    }

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source) =>
        (uint)(source[0] | (source[1] << 8) | (source[2] << 16) | (source[3] << 24));

    private static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source) =>
        (ushort)(source[0] | (source[1] << 8));

    private static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source) =>
        ReadUInt32LittleEndian(source) | ((ulong)ReadUInt32LittleEndian(source[4..]) << 32);
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>
{
    public static ReferenceEqualityComparer Instance { get; } = new();

    private ReferenceEqualityComparer()
    {
    }

    bool IEqualityComparer<object?>.Equals(object? x, object? y) => ReferenceEquals(x, y);

    int IEqualityComparer<object?>.GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj);
}
