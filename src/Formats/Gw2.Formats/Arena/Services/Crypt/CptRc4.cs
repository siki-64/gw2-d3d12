using System.Buffers.Binary;
using System.Numerics;

namespace Gw2.Formats;

internal sealed class CptRc4
{
    internal const int DerivedKeyBytes = 20;
    private const int StateBytes = 256;

    private readonly byte[] _state = new byte[StateBytes];
    private byte _i;
    private byte _j;

    private CptRc4()
    {
    }

    internal static CptRc4 KeySchedule(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty) throw new ArgumentException("keyBytes", nameof(key));
        var cipher = new CptRc4();
        byte[] state = cipher._state;
        for (int index = 0; index < StateBytes; index++) state[index] = (byte)index;
        byte j = 0;
        for (int index = 0, keyIndex = 0; index < StateBytes; index++)
        {
            j = (byte)(j + state[index] + key[keyIndex]);
            (state[index], state[j]) = (state[j], state[index]);
            if (++keyIndex == key.Length) keyIndex = 0;
        }
        return cipher;
    }

    internal static CptRc4 KeySchedule(ulong key) => KeySchedule(DeriveKey(key));

    internal void Process(Span<byte> data)
    {
        byte[] state = _state;
        byte i = _i;
        byte j = _j;
        for (int index = 0; index < data.Length; index++)
        {
            i++;
            j = (byte)(j + state[i]);
            (state[i], state[j]) = (state[j], state[i]);
            data[index] ^= state[(byte)(state[i] + state[j])];
        }
        _i = i;
        _j = j;
    }

    internal static byte[] DeriveKey(ulong key)
    {
        Span<byte> keyBytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(keyBytes, key);
        return DeriveKey(keyBytes);
    }

    internal static byte[] DeriveKey(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty) throw new ArgumentException("keyBytes", nameof(key));
        var derived = new byte[DerivedKeyBytes];
        for (int index = 0; index < DerivedKeyBytes; index++) derived[index] = key[index % key.Length];
        for (int index = DerivedKeyBytes; index < key.Length; index++) derived[index % DerivedKeyBytes] ^= key[index];

        uint w0 = BinaryPrimitives.ReadUInt32LittleEndian(derived);
        uint w1 = BinaryPrimitives.ReadUInt32LittleEndian(derived.AsSpan(4));
        uint w2 = BinaryPrimitives.ReadUInt32LittleEndian(derived.AsSpan(8));
        uint w3 = BinaryPrimitives.ReadUInt32LittleEndian(derived.AsSpan(12));
        uint w4 = BinaryPrimitives.ReadUInt32LittleEndian(derived.AsSpan(16));
        uint a = w0 + 0x9FB498B3;
        uint b = w1 + 0x66B0CD0D + BitOperations.RotateLeft(a, 5);
        uint c = BitOperations.RotateLeft(b, 5) + w2 + (~(a & 0x22222222) & 0x7BF36AE2) + 0xF33D5697;
        uint d = BitOperations.RotateLeft(c, 5) + w3 + (((BitOperations.RotateRight(a, 2) ^ 0x59D148C0) & b) ^ 0x59D148C0) + 0xD675E47B;
        w0 += (((BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(b, 2)) & c) ^ BitOperations.RotateRight(a, 2)) + 0xB453C259 + w4 + BitOperations.RotateLeft(d, 5);
        w1 += d;
        w2 += BitOperations.RotateRight(c, 2);
        w3 += BitOperations.RotateRight(b, 2);
        w4 += BitOperations.RotateRight(a, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(derived, w0);
        BinaryPrimitives.WriteUInt32LittleEndian(derived.AsSpan(4), w1);
        BinaryPrimitives.WriteUInt32LittleEndian(derived.AsSpan(8), w2);
        BinaryPrimitives.WriteUInt32LittleEndian(derived.AsSpan(12), w3);
        BinaryPrimitives.WriteUInt32LittleEndian(derived.AsSpan(16), w4);
        return derived;
    }
}
