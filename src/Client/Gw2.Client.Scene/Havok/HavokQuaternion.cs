using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public static class HavokQuaternion
{
    private static readonly Vector128<int> One = Vector128.Create(1);
    private static readonly Vector128<float> SignBit = Vector128.Create(-0f);
    private static readonly Vector128<float> FourOverPi = Vector128.Create(BitConverter.Int32BitsToSingle(1067645315));
    private static readonly Vector128<float> QuarterPiHigh = Vector128.Create(BitConverter.Int32BitsToSingle(unchecked((int)3209232384)));
    private static readonly Vector128<float> QuarterPiMiddle = Vector128.Create(BitConverter.Int32BitsToSingle(unchecked((int)3112017920)));
    private static readonly Vector128<float> QuarterPiLow = Vector128.Create(BitConverter.Int32BitsToSingle(unchecked((int)3005358441)));
    private static readonly Vector128<float> SineCubic = Vector128.Create(BitConverter.Int32BitsToSingle(unchecked((int)3108807161)));
    private static readonly Vector128<float> SineQuadratic = Vector128.Create(BitConverter.Int32BitsToSingle(1007190942));
    private static readonly Vector128<float> SineLinear = Vector128.Create(BitConverter.Int32BitsToSingle(unchecked((int)3190467235)));
    private static readonly Vector128<float> CosineCubic = Vector128.Create(BitConverter.Int32BitsToSingle(936179150));
    private static readonly Vector128<float> CosineQuadratic = Vector128.Create(BitConverter.Int32BitsToSingle(unchecked((int)3132491290)));
    private static readonly Vector128<float> CosineLinear = Vector128.Create(BitConverter.Int32BitsToSingle(1026206373));
    private static readonly Vector128<float> Half = Vector128.Create(0.5f);
    private static readonly Vector128<float> Unit = Vector128.Create(1f);
    private static readonly Vector128<float> UnitX = Vector128.Create(1f, 0f, 0f, 0f);
    private static readonly Vector128<float> NegateYzw = Vector128.Create(0f, -0f, -0f, -0f);
    private static readonly Vector128<float> NegateXzw = Vector128.Create(-0f, 0f, -0f, -0f);

    public static Vector128<float> Identity => Vector128.Create(0f, 0f, 0f, 1f);

    public static void SinCos(Vector128<float> angle, out Vector128<float> sine, out Vector128<float> cosine)
    {
        var two = Sse2.Add(One, One);
        var four = Sse2.Add(two, two);
        var magnitude = Sse.AndNot(SignBit, angle);
        var octant = Sse2.AndNot(One, Sse2.Add(Sse2.ConvertToVector128Int32WithTruncation(Sse.Multiply(FourOverPi, magnitude)), One));
        var octantFloat = Sse2.ConvertToVector128Single(octant);
        var reduced = Sse.Add(
            Sse.Add(Sse.Add(Sse.Multiply(QuarterPiHigh, octantFloat), magnitude), Sse.Multiply(QuarterPiMiddle, octantFloat)),
            Sse.Multiply(QuarterPiLow, octantFloat));
        var squared = Sse.Multiply(reduced, reduced);
        var sinePolynomial = Sse.Add(
            Sse.Multiply(Sse.Multiply(Sse.Add(Sse.Multiply(Sse.Add(Sse.Multiply(squared, SineCubic), SineQuadratic), squared), SineLinear), squared), reduced),
            reduced);
        var useSine = Sse2.CompareEqual(Sse2.And(octant, two), Vector128<int>.Zero).AsSingle();
        var sinePart = Sse.And(useSine, sinePolynomial);
        var cosinePolynomial = Sse.Add(
            Sse.Subtract(
                Sse.Multiply(Sse.Multiply(Sse.Add(Sse.Multiply(Sse.Add(Sse.Multiply(squared, CosineCubic), CosineQuadratic), squared), CosineLinear), squared), squared),
                Sse.Multiply(squared, Half)),
            Unit);
        var cosinePart = Sse.AndNot(useSine, cosinePolynomial);
        sine = Sse.Xor(
            Sse.Min(Sse.Add(cosinePart, sinePart), Unit),
            Sse.Xor(Sse.And(angle, SignBit), Sse2.ShiftLeftLogical(Sse2.And(octant, four), 29).AsSingle()));
        cosine = Sse.Xor(
            Sse.Min(Sse.Add(Sse.Subtract(sinePolynomial, sinePart), Sse.Subtract(cosinePolynomial, cosinePart)), Unit),
            Sse2.ShiftLeftLogical(Sse2.AndNot(Sse2.Subtract(octant, two), four), 29).AsSingle());
    }

    public static Vector128<float> FromAxisAngle(Vector128<float> axis, float angle)
    {
        SinCos(Sse.Multiply(Vector128.Create(angle), Half), out var sine, out var cosine);
        var scaled = Sse.Multiply(axis, sine);
        return Sse.Shuffle(scaled, Sse.UnpackHigh(scaled, cosine), 196);
    }

    public static Vector128<float> Multiply(Vector128<float> first, Vector128<float> second)
    {
        var firstW = Sse.Shuffle(first, first, 255);
        var secondW = Sse.Shuffle(second, second, 255);
        var wProduct = Sse.Multiply(secondW, firstW);
        var cross = Sse.Subtract(Sse.Multiply(Sse.Shuffle(second, second, 201), first), Sse.Multiply(Sse.Shuffle(first, first, 201), second));
        var scaledFirst = Sse.Multiply(secondW, first);
        var products = Sse.Multiply(first, second);
        var vector = Sse.Add(Sse.Add(Sse.Shuffle(cross, cross, 201), Sse.Multiply(firstW, second)), scaledFirst);
        var dot = Sse.Add(Sse.Add(Sse.Shuffle(products, products, 85), Sse.Shuffle(products, products, 0)), Sse.Shuffle(products, products, 170));
        return Sse.Shuffle(vector, Sse.UnpackHigh(vector, Sse.Subtract(wProduct, dot)), 196);
    }

    public static HavokTransform ToTransform(Vector128<float> rotation, Vector128<float> translation)
    {
        var doubled = Sse.Add(rotation, rotation);
        var xx = rotation.ToScalar() * doubled.ToScalar();
        var products = Sse.Multiply(Sse.Shuffle(doubled, doubled, 165), Sse.Shuffle(rotation, rotation, 65));
        var negatedProducts = Sse.Xor(NegateYzw, products);
        var mixed = Sse.Xor(Sse.Multiply(Sse.Shuffle(rotation, rotation, 254), Sse.Shuffle(doubled, doubled, 26)), NegateXzw);
        var diagonal = UnitX.WithElement(0, (UnitX.ToScalar() - xx) - products.ToScalar());
        mixed = mixed.WithElement(0, mixed.ToScalar() + UnitX.ToScalar());
        var column0 = Sse.Subtract(mixed, negatedProducts);
        negatedProducts = negatedProducts.WithElement(0, xx);
        var swapped = Sse.Subtract(Sse.Xor(NegateYzw, mixed), negatedProducts);
        var column1 = Sse.Shuffle(swapped, swapped, 177);
        var column2 = Sse.Shuffle(Sse.MoveHighToLow(column0, column1), diagonal, 141);
        return new HavokTransform { Column0 = column0, Column1 = column1, Column2 = column2, Translation = translation };
    }
}
