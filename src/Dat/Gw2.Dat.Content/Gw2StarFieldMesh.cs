using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2StarVertex(Vector3 Position, uint Color, Vector2 TextureCoordinate);

public sealed record Gw2StarFieldMesh(Gw2StarVertex[] Vertices, ushort[] Indices)
{
    public const float RadiusOfViewDistance = 0.5f;
    private const float CornerScale = 2500f;
    private const float LargeStarFade = 500f;
    private const int ColorSeed = 1337;
    private const float MaximumAlpha = 128f;
    private const float MaximumChannel = 255f;
    private static readonly ushort[] StarIndices = [0, 1, 2, 1, 2, 3];

    public static Gw2StarFieldMesh Build(Gw2StarField starField, float radius)
    {
        ArgumentNullException.ThrowIfNull(starField);
        var vertices = new Gw2StarVertex[starField.Stars.Count * 4];
        var indices = new ushort[starField.Stars.Count * StarIndices.Length];
        for (var index = 0; index < starField.Stars.Count; index++)
        {
            var star = starField.Stars[index];
            var width = (star.U1 - star.U0) * starField.Size;
            var height = (star.V1 - star.V0) * starField.Size;
            var alphaScale = 1f - MathF.Min(1f, width * height * LargeStarFade);
            var corners = new[]
            {
                new Vector3(radius, CornerScale * width, -CornerScale * height),
                new Vector3(radius, CornerScale * width, CornerScale * height),
                new Vector3(radius, -CornerScale * width, -CornerScale * height),
                new Vector3(radius, -CornerScale * width, CornerScale * height),
            };
            var cornerDistance = corners[0].Length();
            if (cornerDistance > radius)
            {
                for (var corner = 0; corner < corners.Length; corner++) corners[corner] *= radius / cornerDistance;
            }

            var tilt = Rotation(new Vector3(0f, -1f, 0f), star.AngleY);
            var turn = Rotation(new Vector3(0f, 0f, 1f), star.AngleZ);
            var textureCoordinates = new[]
            {
                new Vector2(star.U0, star.V0),
                new Vector2(star.U0, star.V1),
                new Vector2(star.U1, star.V0),
                new Vector2(star.U1, star.V1),
            };
            var random = new Gw2ParkMillerRandom(ColorSeed);
            for (var corner = 0; corner < corners.Length; corner++)
            {
                var position = Rotate(turn, Rotate(tilt, corners[corner]));
                var red = ByteOf(random.Next() * MaximumChannel);
                var green = ByteOf(random.Next() * MaximumChannel);
                var blue = ByteOf(random.Next() * MaximumChannel);
                var alpha = ByteOf(random.Next() * MaximumAlpha * alphaScale);
                var color = red | green << 8 | blue << 16 | alpha << 24;
                vertices[index * 4 + corner] = new(position, color, textureCoordinates[corner]);
            }

            for (var offset = 0; offset < StarIndices.Length; offset++) indices[index * StarIndices.Length + offset] = (ushort)(index * 4 + StarIndices[offset]);
        }

        return new(vertices, indices);
    }

    private static uint ByteOf(float value) => (uint)MathF.Round(value, MidpointRounding.ToEven) & 0xFF;

    private static Matrix3x3Rows Rotation(Vector3 axis, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        var oneMinusCosine = 1f - cosine;
        return new(
            new(axis.X * axis.X * oneMinusCosine + cosine, axis.X * axis.Y * oneMinusCosine + axis.Z * sine, axis.X * axis.Z * oneMinusCosine - axis.Y * sine),
            new(axis.X * axis.Y * oneMinusCosine - axis.Z * sine, axis.Y * axis.Y * oneMinusCosine + cosine, axis.Y * axis.Z * oneMinusCosine + axis.X * sine),
            new(axis.X * axis.Z * oneMinusCosine + axis.Y * sine, axis.Y * axis.Z * oneMinusCosine - axis.X * sine, axis.Z * axis.Z * oneMinusCosine + cosine));
    }

    private static Vector3 Rotate(Matrix3x3Rows rows, Vector3 point) =>
        new(Vector3.Dot(rows.First, point), Vector3.Dot(rows.Second, point), Vector3.Dot(rows.Third, point));

    private readonly record struct Matrix3x3Rows(Vector3 First, Vector3 Second, Vector3 Third);
}

public struct Gw2ParkMillerRandom
{
    private const uint DefaultSeed = 123459876;
    private const uint Multiplier = 48271;
    private const uint Modulus = 0x7FFFFFFF;
    private const uint Quotient = 44488;
    private const float ToUnit = 4.6566129e-10f;
    private uint _state;

    public Gw2ParkMillerRandom(int seed) => _state = seed == 0 ? DefaultSeed : (uint)seed;

    public float Next()
    {
        var state = unchecked(Multiplier * _state - Modulus * (_state / Quotient));
        if (state > Modulus) state = unchecked(state + 0x80000000);
        if (state == 0) state = DefaultSeed;
        _state = state;
        return (int)state * ToUnit;
    }
}
