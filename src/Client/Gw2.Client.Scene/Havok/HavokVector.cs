using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

internal static class HavokVector
{
    public const float Epsilon = 1.1920928955078125e-7f;

    public static Vector128<float> Broadcast(float value) => Vector128.Create(value);

    public static Vector128<float> Splat(Vector128<float> value, byte lane) => Sse.Shuffle(value, value, (byte)(lane * 0x55));

    public static Vector128<float> YZX(Vector128<float> value) => Sse.Shuffle(value, value, 201);

    public static Vector128<float> Dot3(Vector128<float> first, Vector128<float> second)
    {
        var product = Sse.Multiply(first, second);
        return Sse.Add(Sse.Add(Splat(product, 1), Splat(product, 0)), Splat(product, 2));
    }

    public static float Dot3Scalar(Vector128<float> first, Vector128<float> second)
    {
        var product = Sse.Multiply(first, second);
        return product.GetElement(1) + product.GetElement(0) + product.GetElement(2);
    }

    public static Vector128<float> Cross(Vector128<float> first, Vector128<float> second) =>
        YZX(Sse.Subtract(Sse.Multiply(YZX(second), first), Sse.Multiply(YZX(first), second)));

    public static Vector128<float> InverseSquareRoot(Vector128<float> value)
    {
        var estimate = Sse.ReciprocalSqrt(value);
        return Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(value, estimate), estimate)), Sse.Multiply(estimate, Broadcast(0.5f)));
    }

    public static Vector128<float> SquareRoot(Vector128<float> value) =>
        Sse.AndNot(Sse.CompareLessThanOrEqual(value, Vector128<float>.Zero), Sse.Multiply(InverseSquareRoot(value), value));

    public static Vector128<float> Reciprocal(Vector128<float> value)
    {
        var estimate = Sse.Reciprocal(value);
        return Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(value, estimate)), estimate);
    }

    public static Vector128<float> Absolute(Vector128<float> value) => Sse.AndNot(Broadcast(-0f), value);

    public static bool AnyXyzGreater(Vector128<float> value, Vector128<float> limit) =>
        (Sse.MoveMask(Sse.CompareLessThan(limit, Absolute(value))) & 7) != 0;
}
