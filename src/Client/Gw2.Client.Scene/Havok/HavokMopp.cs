using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public sealed class HavokMoppCode(Vector128<float> offsetAndScale, byte[] code)
{
    public Vector128<float> OffsetAndScale { get; } = offsetAndScale;
    public byte[] Code { get; } = code;

    public static HavokMoppCode FromBlob(byte[] blob)
    {
        var info = Vector128.Create(BitConverter.ToSingle(blob, 0), BitConverter.ToSingle(blob, 4), BitConverter.ToSingle(blob, 8), BitConverter.ToSingle(blob, 12));
        var length = BitConverter.ToInt32(blob, 16);
        return new HavokMoppCode(info, blob.AsSpan(20, length).ToArray());
    }

    public HavokMoppCode Scaled(float scale) =>
        new(Sse.Multiply(Vector128.Create(scale, scale, scale, 1f / scale), OffsetAndScale), Code);
}

public static class HavokMopp
{
    private const int ChunkBits = 9;

    [InlineArray(16)]
    private struct QueryState
    {
        private int _element;
    }

    private const int HighX = 0;
    private const int LowX = 4;
    private const int OffsetX = 8;
    private const int PrimitiveOffset = 11;
    private const int Shift = 12;

    private ref struct Query
    {
        public ReadOnlySpan<byte> Code;
        public List<uint> Keys;
        public int ChunkKeyMask;
        public int HighX;
        public int HighY;
        public int HighZ;
        public int LowX;
        public int LowY;
        public int LowZ;

        public readonly int High(int axis) => axis switch { 0 => HighX, 1 => HighY, _ => HighZ };
        public readonly int Low(int axis) => axis switch { 0 => LowX, 1 => LowY, _ => LowZ };
    }

    public static void QueryAabb(HavokMoppCode mopp, Vector128<float> min, Vector128<float> max, List<uint> keys)
    {
        var info = mopp.OffsetAndScale;
        var clampedMin = Sse.Max(info, min);
        var reach = Vector128.Create(16777216f / info.GetElement(3));
        var clampedMax = Sse.Min(Sse.Add(reach, info), max);
        var scale = info.GetElement(3);
        var query = new Query
        {
            Code = mopp.Code,
            Keys = keys,
            LowX = Truncate((clampedMin.GetElement(0) - info.GetElement(0)) * scale) - 1,
            HighX = Truncate((clampedMax.GetElement(0) - info.GetElement(0)) * scale) + 1,
            LowY = Truncate((clampedMin.GetElement(1) - info.GetElement(1)) * scale) - 1,
            HighY = Truncate((clampedMax.GetElement(1) - info.GetElement(1)) * scale) + 1,
            LowZ = Truncate((clampedMin.GetElement(2) - info.GetElement(2)) * scale) - 1,
            HighZ = Truncate((clampedMax.GetElement(2) - info.GetElement(2)) * scale) + 1,
        };
        query.ChunkKeyMask = mopp.Code.Length > 0 && mopp.Code[0] == 13 ? -1 : 0;
        var state = new QueryState();
        state[HighX] = (query.HighX >> 16) + 1;
        state[HighX + 1] = (query.HighY >> 16) + 1;
        state[HighX + 2] = (query.HighZ >> 16) + 1;
        state[LowX] = query.LowX >> 16;
        state[LowX + 1] = query.LowY >> 16;
        state[LowX + 2] = query.LowZ >> 16;
        Visit(ref query, state, 0, 0);
    }

    private static void Visit(ref Query query, QueryState state, int position, int chunkOffset)
    {
        var code = query.Code;
        while (true)
        {
            int opcode = code[position];
            int byte1 = position + 1 < code.Length ? code[position + 1] : 0;
            int byte2 = position + 2 < code.Length ? code[position + 2] : 0;
            int byte3 = position + 3 < code.Length ? code[position + 3] : 0;
            switch (opcode)
            {
                case 0:
                    return;
                case >= 1 and <= 4:
                {
                    var shift = opcode + state[Shift];
                    var offsetX = (byte1 + state[OffsetX]) << opcode;
                    var offsetY = (byte2 + state[OffsetX + 1]) << opcode;
                    var offsetZ = (byte3 + state[OffsetX + 2]) << opcode;
                    var rescaled = state;
                    rescaled[OffsetX] = offsetX;
                    rescaled[OffsetX + 1] = offsetY;
                    rescaled[OffsetX + 2] = offsetZ;
                    rescaled[Shift] = shift;
                    rescaled[LowX] = (query.LowX >> (16 - shift)) - offsetX;
                    rescaled[LowX + 1] = (query.LowY >> (16 - shift)) - offsetY;
                    rescaled[LowX + 2] = (query.LowZ >> (16 - shift)) - offsetZ;
                    rescaled[HighX] = (query.HighX >> (16 - shift)) - offsetX + 1;
                    rescaled[HighX + 1] = (query.HighY >> (16 - shift)) - offsetY + 1;
                    rescaled[HighX + 2] = (query.HighZ >> (16 - shift)) - offsetZ + 1;
                    state = rescaled;
                    position += 4;
                    continue;
                }
                case 5:
                    position += byte1 + 2;
                    continue;
                case 6:
                    position += 256 * byte1 + byte2 + 3;
                    continue;
                case 7:
                    position += 0x10000 * byte1 + 256 * byte2 + byte3 + 4;
                    continue;
                case 8:
                    position += 0x1000000 * byte1 + 256 * (byte3 + (byte2 << 8)) + code[position + 4] + 5;
                    continue;
                case 9:
                    state[PrimitiveOffset] += byte1;
                    position += 2;
                    continue;
                case 0x0A:
                    state[PrimitiveOffset] += byte2 + (byte1 << 8);
                    position += 3;
                    continue;
                case 0x0B:
                    state[PrimitiveOffset] = code[position + 4] + (byte1 << 24) + (byte2 << 16) + (byte3 << 8);
                    position += 5;
                    continue;
                case 0x0C:
                    chunkOffset = (byte2 | (byte1 << 8)) << ChunkBits;
                    position = chunkOffset;
                    continue;
                case 0x0D:
                    position += 5;
                    continue;
                case >= 0x10 and <= 0x12:
                {
                    var axis = opcode - 0x10;
                    position += 4;
                    if (state[HighX + axis] > byte2)
                    {
                        position += byte3;
                        if (state[LowX + axis] < byte1) Visit(ref query, state, position - byte3, chunkOffset);
                    }
                    else if (state[LowX + axis] >= byte1) return;
                    continue;
                }
                case >= 0x13 and <= 0x1C:
                {
                    DiagonalBounds(opcode, byte1, byte2, state, out var lowProjection, out var highProjection, out var lowPlane, out var highPlane);
                    position += 4;
                    if (highProjection > highPlane)
                    {
                        if (lowProjection < lowPlane) Visit(ref query, state, position, chunkOffset);
                        position += byte3;
                    }
                    else if (lowProjection >= lowPlane) return;
                    continue;
                }
                case >= 0x20 and <= 0x22:
                {
                    var axis = opcode - 0x20;
                    position += 3;
                    if (state[HighX + axis] > byte1)
                    {
                        position += byte2;
                        if (state[LowX + axis] <= byte1) Visit(ref query, state, position - byte2, chunkOffset);
                    }
                    continue;
                }
                case >= 0x23 and <= 0x25:
                {
                    var axis = opcode - 0x23;
                    var firstJump = code[position + 4] + (byte3 << 8);
                    var secondJump = code[position + 6] + (code[position + 5] << 8);
                    var children = position + 7;
                    if (state[HighX + axis] > byte2)
                    {
                        position = children + secondJump;
                        if (state[LowX + axis] < byte1) Visit(ref query, state, children + firstJump, chunkOffset);
                    }
                    else
                    {
                        if (state[LowX + axis] >= byte1) return;
                        position = children + firstJump;
                    }
                    continue;
                }
                case >= 0x26 and <= 0x28:
                {
                    var axis = opcode - 0x26;
                    if (state[HighX + axis] < byte1) return;
                    if (state[LowX + axis] >= byte2) return;
                    position += 3;
                    continue;
                }
                case >= 0x29 and <= 0x2B:
                {
                    var axis = opcode - 0x29;
                    if (query.High(axis) < byte3 + ((byte2 + (byte1 << 8)) << 8)) return;
                    if (query.Low(axis) > code[position + 6] + ((code[position + 5] + (code[position + 4] << 8)) << 8)) return;
                    position += 7;
                    continue;
                }
                case >= 0x30 and <= 0x4F:
                    Emit(ref query, state, opcode - 0x30, chunkOffset);
                    return;
                case 0x50:
                    Emit(ref query, state, byte1, chunkOffset);
                    return;
                case 0x51:
                    Emit(ref query, state, byte2 + (byte1 << 8), chunkOffset);
                    return;
                case 0x52:
                    Emit(ref query, state, byte3 + ((byte2 + (byte1 << 8)) << 8), chunkOffset);
                    return;
                case 0x53:
                    Emit(ref query, state, code[position + 4] + ((byte3 + ((byte2 + (byte1 << 8)) << 8)) << 8), chunkOffset);
                    return;
                case >= 0x60 and <= 0x63:
                    position += 2;
                    continue;
                case >= 0x64 and <= 0x67:
                    position += 3;
                    continue;
                case >= 0x68 and <= 0x6B:
                    position += 5;
                    continue;
                case 0x70:
                    chunkOffset = code[position + 4] + ((byte3 + ((byte2 + (byte1 << 8)) << 8)) << 8);
                    position = chunkOffset;
                    continue;
                default:
                    throw new InvalidOperationException($"MOPP opcode 0x{opcode:X2} at {position}");
            }
        }
    }

    private static void DiagonalBounds(int opcode, int byte1, int byte2, in QueryState state, out int lowProjection, out int highProjection, out int lowPlane, out int highPlane)
    {
        int highX = state[HighX], highY = state[HighX + 1], highZ = state[HighX + 2];
        int lowX = state[LowX], lowY = state[LowX + 1], lowZ = state[LowX + 2];
        switch (opcode)
        {
            case 0x13: lowPlane = 2 * byte1; highPlane = 2 * byte2; lowProjection = lowY + lowZ; highProjection = highY + highZ; return;
            case 0x14: lowPlane = 2 * byte1 - 255; highPlane = 2 * byte2 - 255; lowProjection = lowY - highZ; highProjection = highY - lowZ; return;
            case 0x15: lowPlane = 2 * byte1; highPlane = 2 * byte2; lowProjection = lowX + lowZ; highProjection = highX + highZ; return;
            case 0x16: lowPlane = 2 * byte1 - 255; highPlane = 2 * byte2 - 255; lowProjection = lowX - highZ; highProjection = highX - lowZ; return;
            case 0x17: lowPlane = 2 * byte1; highPlane = 2 * byte2; lowProjection = lowX + lowY; highProjection = highX + highY; return;
            case 0x18: lowPlane = 2 * byte1 - 255; highPlane = 2 * byte2 - 255; lowProjection = lowX - highY; highProjection = highX - lowY; return;
            case 0x19: lowPlane = 3 * byte1; highPlane = 3 * byte2; lowProjection = lowX + lowY + lowZ; highProjection = highX + highY + highZ; return;
            case 0x1A: lowPlane = 3 * (byte1 - 85); highPlane = 3 * (byte2 - 85); lowProjection = lowX + lowY - highZ; highProjection = highX + highY - lowZ; return;
            case 0x1B: lowPlane = 3 * (byte1 - 85); highPlane = 3 * (byte2 - 85); lowProjection = lowX + lowZ - highY; highProjection = highX + highZ - lowY; return;
            default: lowPlane = 3 * (byte1 - 170); highPlane = 3 * (byte2 - 170); lowProjection = lowX - highZ - highY; highProjection = highX - lowZ - lowY; return;
        }
    }

    private static int Truncate(float value) => Sse.ConvertToInt32WithTruncation(Vector128.CreateScalarUnsafe(value));

    private static void Emit(ref Query query, in QueryState state, int primitive, int chunkOffset) =>
        query.Keys.Add((uint)((primitive + state[PrimitiveOffset]) | (query.ChunkKeyMask & ((chunkOffset >> ChunkBits) << 8))));
}
