using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public static class HavokMoppRay
{
    private const int ChunkBits = 9;
    private const float CodeUnitsPerScale = 1f / 65536f;

    private sealed class RayQuery
    {
        public required byte[] Code;
        public required Vector128<float> WorldFrom;
        public required Vector128<float> WorldTo;
        public required Vector128<float> CodeOffset;
        public required float InverseCodeScale;
        public required int ChunkKeyMask;
        public required Func<uint, float> CastChild;
        public float HitFraction;
    }

    private struct Frame
    {
        public Vector128<float> Offset;
        public float Scale;
        public int PrimitiveOffset;
    }

    public static float CastRay(HavokMoppCode mopp, Vector128<float> from, Vector128<float> to, float earlyOutHitFraction, Func<uint, float> castChild)
    {
        var info = mopp.OffsetAndScale;
        var codeScale = info.GetElement(3);
        var query = new RayQuery
        {
            Code = mopp.Code,
            WorldFrom = from,
            WorldTo = to,
            CodeOffset = info,
            InverseCodeScale = 1f / codeScale,
            ChunkKeyMask = mopp.Code.Length > 0 && mopp.Code[0] == 0x0D ? -1 : 0,
            CastChild = castChild,
            HitFraction = earlyOutHitFraction,
        };
        var rootScale = codeScale * CodeUnitsPerScale;
        var scale = Vector128.Create(rootScale);
        var frame = new Frame { Offset = Vector128<float>.Zero, Scale = rootScale };
        Walk(query, frame, Sse.Multiply(Sse.Subtract(from, info), scale), Sse.Multiply(Sse.Subtract(to, info), scale), 0, 0);
        return query.HitFraction;
    }

    private static Vector128<float> Interpolate(Vector128<float> from, Vector128<float> to, float fraction) =>
        Sse.Add(Sse.Multiply(Vector128.Create(fraction), Sse.Subtract(to, from)), from);

    private static Vector128<float> ShortenedEnd(RayQuery query, in Frame frame)
    {
        var world = Sse.Add(Sse.Multiply(Sse.Subtract(query.WorldTo, query.WorldFrom), Vector128.Create(query.HitFraction)), query.WorldFrom);
        return Sse.Subtract(Sse.Multiply(Vector128.Create(frame.Scale), Sse.Subtract(world, query.CodeOffset)), frame.Offset);
    }

    private static void Walk(RayQuery query, Frame frame, Vector128<float> from, Vector128<float> to, int position, int chunkOffset)
    {
        var code = query.Code;
        while (true)
        {
            int opcode = code[position];
            int byte1 = position + 1 < code.Length ? code[position + 1] : 0;
            int byte2 = position + 2 < code.Length ? code[position + 2] : 0;
            int byte3 = position + 3 < code.Length ? code[position + 3] : 0;
            float lowPlane, highPlane, fromProjection, toProjection;
            int leftChild, rightChild, axis;
            switch (opcode)
            {
                case 0:
                    return;
                case >= 1 and <= 4:
                {
                    var step = Vector128.Create(byte1, byte2, byte3, 0f);
                    var scale = Vector128.Create((float)(1 << opcode));
                    from = Sse.Multiply(scale, Sse.Subtract(from, step));
                    to = Sse.Multiply(scale, Sse.Subtract(to, step));
                    frame.Offset = Sse.Multiply(Sse.Add(step, frame.Offset), scale);
                    frame.Scale = (1 << opcode) * frame.Scale;
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
                    frame.PrimitiveOffset += byte1;
                    position += 2;
                    continue;
                case 0x0A:
                    frame.PrimitiveOffset += byte2 + (byte1 << 8);
                    position += 3;
                    continue;
                case 0x0B:
                    frame.PrimitiveOffset = code[position + 4] + (byte1 << 24) + (byte2 << 16) + (byte3 << 8);
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
                    axis = opcode - 0x10;
                    lowPlane = byte1;
                    highPlane = byte2;
                    fromProjection = from.GetElement(axis);
                    toProjection = to.GetElement(axis);
                    leftChild = position + 4;
                    rightChild = leftChild + byte3;
                    break;
                case >= 0x13 and <= 0x1C:
                    axis = -1;
                    DiagonalPlanes(opcode, byte1, byte2, from, to, out lowPlane, out highPlane, out fromProjection, out toProjection);
                    leftChild = position + 4;
                    rightChild = leftChild + byte3;
                    break;
                case >= 0x20 and <= 0x22:
                    axis = opcode - 0x20;
                    highPlane = byte1;
                    lowPlane = highPlane + 1f;
                    fromProjection = from.GetElement(axis);
                    toProjection = to.GetElement(axis);
                    leftChild = position + 3;
                    rightChild = leftChild + byte2;
                    break;
                case >= 0x23 and <= 0x25:
                    axis = opcode - 0x23;
                    lowPlane = byte1;
                    highPlane = byte2;
                    fromProjection = from.GetElement(axis);
                    toProjection = to.GetElement(axis);
                    leftChild = position + 7 + (code[position + 4] + (byte3 << 8));
                    rightChild = position + 7 + (code[position + 6] + (code[position + 5] << 8));
                    break;
                case >= 0x26 and <= 0x28:
                    if (!Clip(opcode - 0x26, byte1, byte2, ref from, ref to)) return;
                    position += 3;
                    continue;
                case >= 0x29 and <= 0x2B:
                {
                    var clipAxis = opcode - 0x29;
                    var offset = frame.Offset.GetElement(clipAxis);
                    var low = (float)(byte3 + ((byte2 + (byte1 << 8)) << 8)) * query.InverseCodeScale * frame.Scale - offset;
                    var high = (float)(code[position + 6] + ((code[position + 5] + (code[position + 4] << 8)) << 8)) * query.InverseCodeScale * frame.Scale - offset;
                    if (!Clip(clipAxis, low, high, ref from, ref to)) return;
                    position += 7;
                    continue;
                }
                case >= 0x30 and <= 0x4F:
                    Terminal(query, frame, opcode - 0x30, chunkOffset);
                    return;
                case 0x50:
                    Terminal(query, frame, byte1, chunkOffset);
                    return;
                case 0x51:
                    Terminal(query, frame, byte2 + (byte1 << 8), chunkOffset);
                    return;
                case 0x52:
                    Terminal(query, frame, byte3 + ((byte2 + (byte1 << 8)) << 8), chunkOffset);
                    return;
                case 0x53:
                    Terminal(query, frame, code[position + 4] + ((byte3 + ((byte2 + (byte1 << 8)) << 8)) << 8), chunkOffset);
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

            if (!(toProjection >= highPlane) && !(fromProjection >= highPlane))
            {
                position = leftChild;
                continue;
            }
            if (fromProjection > lowPlane && toProjection > lowPlane)
            {
                position = rightChild;
                continue;
            }

            var fromToLow = fromProjection - lowPlane;
            var toToLow = toProjection - lowPlane;
            var fromToHigh = fromProjection - highPlane;
            var toToHigh = toProjection - highPlane;
            if (!(fromToLow >= toToLow))
            {
                var nearEnd = to;
                if (!(toToLow * fromToLow >= 0f)) nearEnd = Interpolate(from, to, fromToLow / (fromToLow - toToLow));
                Walk(query, frame, from, nearEnd, leftChild, chunkOffset);
                if (!(toToHigh * fromToHigh >= 0f)) from = Interpolate(from, to, fromToHigh / (fromToHigh - toToHigh));
                if (!(query.HitFraction >= 1f))
                {
                    to = ShortenedEnd(query, frame);
                    if (axis >= 0 && highPlane > to.GetElement(axis)) return;
                }
                position = rightChild;
            }
            else
            {
                var nearEnd = to;
                if (!(toToHigh * fromToHigh >= 0f)) nearEnd = Interpolate(from, to, fromToHigh / (fromToHigh - toToHigh));
                Walk(query, frame, from, nearEnd, rightChild, chunkOffset);
                if (!(toToLow * fromToLow >= 0f)) from = Interpolate(from, to, fromToLow / (fromToLow - toToLow));
                if (!(query.HitFraction >= 1f))
                {
                    to = ShortenedEnd(query, frame);
                    if (axis >= 0 && !(lowPlane >= to.GetElement(axis))) return;
                }
                position = leftChild;
            }
        }
    }

    private static bool Clip(int axis, float lowPlane, float highPlane, ref Vector128<float> from, ref Vector128<float> to)
    {
        var fromCoordinate = from.GetElement(axis);
        var toCoordinate = to.GetElement(axis);
        bool backward;
        if (!(fromCoordinate >= toCoordinate))
        {
            if (!(toCoordinate >= lowPlane) || fromCoordinate > highPlane) return false;
            backward = false;
        }
        else
        {
            if (!(fromCoordinate >= lowPlane) || toCoordinate > highPlane) return false;
            backward = true;
        }

        var originalFrom = from;
        var originalTo = to;
        var fromToHigh = fromCoordinate - highPlane;
        var toToHigh = toCoordinate - highPlane;
        if (!(toToHigh * fromToHigh >= 0f))
        {
            var crossing = Interpolate(originalFrom, originalTo, fromToHigh / (fromToHigh - toToHigh));
            if (backward) from = crossing;
            else to = crossing;
        }
        var fromToLow = fromCoordinate - lowPlane;
        var toToLow = toCoordinate - lowPlane;
        if (!(toToLow * fromToLow >= 0f))
        {
            var crossing = Interpolate(originalFrom, originalTo, fromToLow / (fromToLow - toToLow));
            if (backward) to = crossing;
            else from = crossing;
        }
        return true;
    }

    private static void DiagonalPlanes(int opcode, int byte1, int byte2, Vector128<float> from, Vector128<float> to, out float lowPlane, out float highPlane, out float fromProjection, out float toProjection)
    {
        float fromX = from.GetElement(0), fromY = from.GetElement(1), fromZ = from.GetElement(2);
        float toX = to.GetElement(0), toY = to.GetElement(1), toZ = to.GetElement(2);
        var planeStep = opcode <= 0x18 ? 2f : 3f;
        var planeShift = opcode switch { 0x14 or 0x16 or 0x18 or 0x1A or 0x1B => 255f, 0x1C => 510f, _ => 0f };
        lowPlane = byte1 * planeStep - planeShift;
        highPlane = byte2 * planeStep - planeShift;
        switch (opcode)
        {
            case 0x13: fromProjection = fromZ + fromY; toProjection = toZ + toY; return;
            case 0x14: fromProjection = fromY - fromZ; toProjection = toY - toZ; return;
            case 0x15: fromProjection = fromZ + fromX; toProjection = toZ + toX; return;
            case 0x16: fromProjection = fromX - fromZ; toProjection = toX - toZ; return;
            case 0x17: fromProjection = fromY + fromX; toProjection = toY + toX; return;
            case 0x18: fromProjection = fromX - fromY; toProjection = toX - toY; return;
            case 0x19: fromProjection = fromY + fromX + fromZ; toProjection = toY + toX + toZ; return;
            case 0x1A: fromProjection = fromY + fromX - fromZ; toProjection = toY + toX - toZ; return;
            case 0x1B: fromProjection = fromX - fromY + fromZ; toProjection = toX - toY + toZ; return;
            default: fromProjection = fromX - fromY - fromZ; toProjection = toX - toY - toZ; return;
        }
    }

    private static void Terminal(RayQuery query, in Frame frame, int primitive, int chunkOffset)
    {
        var key = (uint)((frame.PrimitiveOffset + primitive) | (((chunkOffset >> ChunkBits) << 8) & query.ChunkKeyMask));
        query.HitFraction = query.CastChild(key);
    }
}
