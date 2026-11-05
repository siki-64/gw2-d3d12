using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2LensFlareVertex(Vector3 Position, Vector4 Color, Vector2 Uv, Vector2 NextUv, float Blend);

public static class Gw2SkyCardLensFlare
{
    public const uint FadeBandToken = 0x99E737C1;
    public const uint FlareWorldToken = 0x4E958CF5;
    public const byte WidenOffAxisFlag = 0x1;
    public const float FlareDistance = 25000f;
    public const int VerticesPerAtom = 4;
    public const int IndicesPerAtom = 6;
    private const float LineDistance = 40f;
    private const float OffAxisWidening = 3f;
    private const float MinimumFacing = 0.000001f;
    private const float PoleFacing = 0.99f;
    private static readonly int[] QuadIndices = [0, 1, 2, 2, 1, 3];

    public static Vector3 Position(Vector3 eye, float azimuth, float latitude) =>
        eye + FlareDistance * new Vector3(MathF.Cos(azimuth) * MathF.Cos(latitude), MathF.Sin(azimuth) * MathF.Cos(latitude), -MathF.Sin(latitude));

    public static float Visibility(long visiblePixels, long totalPixels, float dayWeight, float intensity)
    {
        if (visiblePixels <= 0 || totalPixels <= 0) return 0f;
        return (float)visiblePixels / totalPixels * (2f * MathF.Max(dayWeight, 1f - dayWeight) - 1f) * intensity;
    }

    public static int[] Indices(int atomCount)
    {
        var indices = new int[atomCount * IndicesPerAtom];
        for (var index = 0; index < indices.Length; index++)
            indices[index] = index / IndicesPerAtom * VerticesPerAtom + QuadIndices[index % IndicesPerAtom];
        return indices;
    }

    public static Gw2LensFlareVertex[] Build(Gw2MapEnvLensFlare flare, Vector3 flarePosition, float visibility, float seconds, Vector3 eye, Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward)
    {
        ArgumentNullException.ThrowIfNull(flare);
        var vertices = new Gw2LensFlareVertex[flare.Atoms.Count * VerticesPerAtom];
        var forward = cameraForward;
        var helper = MathF.Abs(forward.Z) >= PoleFacing ? new Vector3(0f, -1f, 0f) : new Vector3(0f, 0f, 1f);
        var right = SafeNormalize(Vector3.Cross(helper, forward));
        var up = SafeNormalize(Vector3.Cross(forward, right));
        var direction = SafeNormalize(flarePosition - eye);
        var facing = Vector3.Dot(forward, direction);
        if (facing < MinimumFacing || visibility == 0f) return vertices;

        var size = (flare.Flags & WidenOffAxisFlag) != 0 ? (1f - facing * facing) * OffAxisWidening + facing * facing : 1f;
        var lineStart = eye + direction * (LineDistance / facing);
        var lineEnd = eye + forward * LineDistance;
        var along = SafeNormalize(lineEnd - lineStart);
        var across = Vector3.Cross(along, forward);
        var cameraRotationScale = cameraRight.X * cameraUp.X;

        for (var atomIndex = 0; atomIndex < flare.Atoms.Count; atomIndex++)
        {
            var atom = flare.Atoms[atomIndex];
            var rows = (int)Math.Max(atom.Rows, 1u);
            var columns = (int)Math.Max(atom.Columns, 1u);
            var cellCount = rows * columns;
            var progress = seconds * atom.Fps;
            var frame = (int)MathF.Floor(MathF.Max(progress, 0f));
            var cell = (int)((atom.Start + (uint)frame) % (uint)cellCount);
            var nextCell = (cell + 1) % cellCount;
            var blend = progress - MathF.Truncate(progress);
            var (uv0, uv1) = CellRect(cell, rows, columns);
            var (nextUv0, nextUv1) = CellRect(nextCell, rows, columns);

            var angle = atom.CameraRotation * cameraRotationScale + atom.BaseRotation * MathF.Tau;
            var rotation = Quaternion.CreateFromAxisAngle(forward, angle);
            var rotatedRight = Vector3.Transform(right, rotation);
            var rotatedUp = Vector3.Transform(up, rotation);
            var scaleX = size * (atom.Scale.X == 0f ? 1f : atom.Scale.X);
            var scaleY = size * (atom.Scale.Y == 0f ? 1f : atom.Scale.Y);
            var center = Vector3.Lerp(lineStart, lineEnd, atom.Offset.X) + atom.Offset.Y * across;
            var color = AtomColor(atom.Color, flare.OpacityCoeff * visibility);

            var first = atomIndex * VerticesPerAtom;
            vertices[first] = new(center - rotatedRight * scaleX - rotatedUp * scaleY, color, uv0, nextUv0, blend);
            vertices[first + 1] = new(center - rotatedRight * scaleX + rotatedUp * scaleY, color, new(uv0.X, uv1.Y), new(nextUv0.X, nextUv1.Y), blend);
            vertices[first + 2] = new(center + rotatedRight * scaleX - rotatedUp * scaleY, color, new(uv1.X, uv0.Y), new(nextUv1.X, nextUv0.Y), blend);
            vertices[first + 3] = new(center + rotatedRight * scaleX + rotatedUp * scaleY, color, uv1, nextUv1, blend);
        }

        return vertices;
    }

    private static (Vector2 Min, Vector2 Max) CellRect(int cell, int rows, int columns)
    {
        var min = new Vector2((float)(cell % columns) / columns, (float)(cell / rows) / rows);
        return (min, min + new Vector2(1f / columns, 1f / rows));
    }

    private static Vector4 AtomColor(byte[] bgra, float alphaScale)
    {
        byte Channel(int index) => index < bgra.Length ? bgra[index] : (byte)0xFF;
        var alpha = (int)(Channel(3) * alphaScale);
        return new Vector4(Channel(2), Channel(1), Channel(0), Math.Clamp(alpha, 0, 255)) / 255f;
    }

    private static Vector3 SafeNormalize(Vector3 vector) =>
        vector.LengthSquared() >= 1e-12f ? Vector3.Normalize(vector) : Vector3.Zero;
}
