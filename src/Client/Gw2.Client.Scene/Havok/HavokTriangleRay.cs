using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public static class HavokTriangleRay
{
    public static readonly float EdgeTolerance = BitConverter.Int32BitsToSingle(0x38D1B717);

    private static Vector128<float> Yzx(Vector128<float> value) => Sse.Shuffle(value, value, 0xC9);

    private static float Dot(Vector128<float> product) => product.GetElement(1) + product.GetElement(0) + product.GetElement(2);

    public static bool CastRay(Vector128<float> from, Vector128<float> to, Vector128<float> a, Vector128<float> b, Vector128<float> c, ref float hitFraction, out Vector128<float> normal)
    {
        normal = default;
        var direction = Sse.Subtract(to, from);
        direction = Sse.AndNot(Sse.CompareEqual(direction, Vector128<float>.Zero), direction);

        var ab = Sse.Subtract(b, a);
        var ac = Sse.Subtract(c, a);
        var planeNormal = Yzx(Sse.Subtract(Sse.Multiply(Yzx(ac), ab), Sse.Multiply(Yzx(ab), ac)));
        var startDistance = Dot(Sse.Multiply(Sse.Subtract(from, a), planeNormal));
        var directionDistance = Dot(Sse.Multiply(direction, planeNormal));

        var reciprocal = Sse.ReciprocalScalar(Vector128.CreateScalar(directionDistance)).ToScalar();
        var endDistance = directionDistance + startDistance;
        var inverseDirectionDistance = (2f - directionDistance * reciprocal) * reciprocal;
        var fraction = 0f - inverseDirectionDistance * startDistance;
        var staysOnOneSide = (endDistance == 0f || startDistance != 0f) && 0f <= endDistance * startDistance;
        if (staysOnOneSide || !(fraction < hitFraction)) return false;

        var facing = BitConverter.SingleToInt32Bits(startDistance) < 0
            ? Sse.Xor(planeNormal, Vector128.Create(int.MinValue).AsSingle())
            : planeNormal;
        var point = Sse.Add(Sse.Multiply(direction, Vector128.Create(fraction)), from);
        var toA = Sse.Subtract(a, point);
        var toB = Sse.Subtract(b, point);
        var toC = Sse.Subtract(c, point);
        var edgeAb = Sse.Multiply(Yzx(Sse.Subtract(Sse.Multiply(Yzx(toB), toA), Sse.Multiply(Yzx(toA), toB))), planeNormal);
        var edgeCa = Sse.Multiply(Yzx(Sse.Subtract(Sse.Multiply(Yzx(toA), toC), Sse.Multiply(Yzx(toC), toA))), planeNormal);
        var edgeBc = Sse.Multiply(Yzx(Sse.Subtract(Sse.Multiply(Yzx(toC), toB), Sse.Multiply(Yzx(toB), toC))), planeNormal);
        var lengthSquared = Dot(Sse.Multiply(planeNormal, planeNormal));

        var reciprocalRoot = Sse.ReciprocalSqrtScalar(Vector128.CreateScalar(lengthSquared)).ToScalar();
        var inverseLength = lengthSquared <= 0f ? 0f : (3f - lengthSquared * reciprocalRoot * reciprocalRoot) * (reciprocalRoot * 0.5f);
        var bound = (0f - lengthSquared) * EdgeTolerance;
        if (!(bound <= Dot(edgeAb)) || !(bound <= Dot(edgeBc)) || !(bound <= Dot(edgeCa))) return false;

        normal = Sse.Multiply(Vector128.Create(inverseLength), facing);
        hitFraction = fraction;
        return true;
    }
}
