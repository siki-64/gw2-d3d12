using System;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public readonly record struct Gw2ColorShift(float Brightness, float Contrast, float Hue, float Saturation, float Lightness)
{
    private const float InverseSquareRootTwo = 0.707107f;
    private const float InverseSquareRootSix = 0.408248f;
    private const float TwoOverSquareRootSix = 0.816497f;
    private const float InverseSquareRootThree = 0.57735f;
    private const float SquareRootThree = 3f * InverseSquareRootThree;

    public static Gw2ColorShift Identity => new(0f, 1f, 0f, 1f, 1f);

    public Matrix4x4 ToMatrix()
    {
        var offset = Brightness + (1f - Contrast) * 0.5f;
        var hueAxisX = new Vector4(InverseSquareRootTwo * Contrast, 0f, -InverseSquareRootTwo * Contrast, 0f);
        var hueAxisY = new Vector4(-InverseSquareRootSix * Contrast, TwoOverSquareRootSix * Contrast, -InverseSquareRootSix * Contrast, 0f);
        var greyAxis = new Vector4(InverseSquareRootThree * Contrast, InverseSquareRootThree * Contrast, InverseSquareRootThree * Contrast, SquareRootThree * offset);

        var cosine = MathF.Cos(Hue) * Saturation;
        var sine = MathF.Sin(Hue) * Saturation;
        var rotatedX = cosine * hueAxisX + sine * hueAxisY;
        var rotatedY = cosine * hueAxisY - sine * hueAxisX;
        var scaledGrey = Lightness * greyAxis;

        var red = InverseSquareRootTwo * rotatedX - InverseSquareRootSix * rotatedY + InverseSquareRootThree * scaledGrey;
        var green = TwoOverSquareRootSix * rotatedY + InverseSquareRootThree * scaledGrey;
        var blue = -InverseSquareRootTwo * rotatedX - InverseSquareRootSix * rotatedY + InverseSquareRootThree * scaledGrey;
        return new Matrix4x4(
            red.X, red.Y, red.Z, red.W,
            green.X, green.Y, green.Z, green.W,
            blue.X, blue.Y, blue.Z, blue.W,
            0f, 0f, 0f, 1f);
    }

    public (Vector4 Red, Vector4 Green, Vector4 Blue) ToColumnConstants()
    {
        var matrix = ToMatrix();
        const float ByteOffsetScale = 256f / 255f;
        return (
            new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M14 * ByteOffsetScale),
            new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M24 * ByteOffsetScale),
            new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M34 * ByteOffsetScale));
    }

    public static uint Apply(in Matrix4x4 matrix, uint rgba)
    {
        var red = (rgba & 0xFF) / 255f;
        var green = ((rgba >> 8) & 0xFF) / 255f;
        var blue = ((rgba >> 16) & 0xFF) / 255f;
        var shiftedRed = matrix.M11 * red + matrix.M21 * green + matrix.M31 * blue + matrix.M14;
        var shiftedGreen = matrix.M12 * red + matrix.M22 * green + matrix.M32 * blue + matrix.M24;
        var shiftedBlue = matrix.M13 * red + matrix.M23 * green + matrix.M33 * blue + matrix.M34;
        return ToByte(shiftedRed) | ToByte(shiftedGreen) << 8 | ToByte(shiftedBlue) << 16 | (rgba & 0xFF000000);
    }

    private static uint ToByte(float channel) => (uint)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
}
