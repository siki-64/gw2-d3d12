using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public readonly record struct Gw2ParticleCorner(Vector3 Position, Vector3 Normal, Vector4 Colour, Vector2 TexCoord)
{
    public Vector2? SecondTexCoord { get; init; }

    public Vector2? ThirdTexCoord { get; init; }

    public Vector2? FourthTexCoord { get; init; }

    public Vector3 Tangent { get; init; }

    public Vector3 Bitangent { get; init; }
}

public readonly record struct Gw2ParticleCamera(Vector3 Eye, Vector3 Right, Vector3 Up, Vector3 Forward);

public static class Gw2ParticleQuads
{
    public const int CornersPerQuad = 4;
    private const float MinimumAxisLengthSquared = 1e-12f;
    private const float BentNormalScale = 0.70720518f;

    public static void Build(Gw2ParticleSimulator simulator, Matrix4x4 emitterWorld, float sizeScale, Gw2ParticleCamera camera, List<Gw2ParticleCorner> corners)
    {
        var emitter = simulator.Emitter;
        foreach (var particle in simulator.Particles)
        {
            var centre = simulator.IsWorldSpace ? particle.PositionInGameUnits : Vector3.Transform(particle.PositionInGameUnits, emitterWorld);
            var velocity = simulator.IsWorldSpace ? particle.VelocityInGameUnits : Vector3.TransformNormal(particle.VelocityInGameUnits, emitterWorld);
            var scale = simulator.ScaleOf(particle) * sizeScale;
            var halfWidth = particle.Width * scale;
            var halfHeight = particle.Height * scale;
            var (right, up) = Axes(emitter, emitterWorld, centre, velocity, particle.RotationRadians, camera);
            var colour = simulator.ColourOf(particle);
            var cell = Cell(emitter, simulator.FrameOf(particle));
            var (nextCell, third) = ExtraTexCoords(simulator, particle);
            var fourth = (emitter.Flags & Gw2ParticleSimulator.RandomTexCoordsFlag) != 0 ? RotatedRandomPair(particle.RandomByte, 3) : (Vector2?)null;
            AddCorner(corners, centre, centre - right * halfWidth + up * halfHeight, colour, new(cell.X, cell.Y), camera, nextCell is { } a ? new(a.X, a.Y) : null, third?.Second, third?.Third, fourth);
            AddCorner(corners, centre, centre + right * halfWidth + up * halfHeight, colour, new(cell.Z, cell.Y), camera, nextCell is { } b ? new(b.Z, b.Y) : null, third?.Second, third?.Third, fourth);
            AddCorner(corners, centre, centre + right * halfWidth - up * halfHeight, colour, new(cell.Z, cell.W), camera, nextCell is { } c ? new(c.Z, c.W) : null, third?.Second, third?.Third, fourth);
            AddCorner(corners, centre, centre - right * halfWidth - up * halfHeight, colour, new(cell.X, cell.W), camera, nextCell is { } d ? new(d.X, d.W) : null, third?.Second, third?.Third, fourth);
        }
    }

    public static Vector3 BentNormal(Vector3 centre, Vector3 corner, Vector3 cameraForward) =>
        (Normalised(corner - centre, -cameraForward) - cameraForward) * BentNormalScale;

    private static void AddCorner(List<Gw2ParticleCorner> corners, Vector3 centre, Vector3 corner, Vector4 colour, Vector2 texCoord, Gw2ParticleCamera camera, Vector2? nextFrameTexCoord, Vector2? randomSecond, Vector2? third, Vector2? fourth) =>
        corners.Add(new(corner, BentNormal(centre, corner, camera.Forward), colour, texCoord) { SecondTexCoord = nextFrameTexCoord ?? randomSecond, ThirdTexCoord = third, FourthTexCoord = fourth });

    public static (Vector4? NextCell, (Vector2? Second, Vector2 Third)? Extra) ExtraTexCoords(Gw2ParticleSimulator simulator, in Gw2Particle particle)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        var flags = simulator.Emitter.Flags;
        if ((flags & Gw2ParticleSimulator.SmoothFlipbookFlag) != 0)
            return (Cell(simulator.Emitter, simulator.FrameOf(particle, 1)), (null, new Vector2(simulator.FrameBlendOf(particle), 0f)));
        if ((flags & Gw2ParticleSimulator.RandomTexCoordsFlag) != 0)
            return (null, (RotatedRandomPair(particle.RandomByte, 1), RotatedRandomPair(particle.RandomByte, 2)));
        return (null, null);
    }

    public static Vector2 RotatedRandomPair(byte random, int shift) =>
        new((byte)(random << shift | random >> (8 - shift)) / 255f, (byte)(random >> shift | random << (8 - shift)) / 255f);

    public static Vector4 Cell(Gw2ParticleEmitter emitter, int frame)
    {
        var rect = emitter.TexCoordRect;
        if (emitter.Flipbook is not { Columns: > 0, Rows: > 0 } flipbook) return rect;
        var columns = (int)flipbook.Columns;
        var rows = (int)flipbook.Rows;
        var column = frame % columns;
        var row = frame / columns % rows;
        var cellWidth = (rect.Z - rect.X) / columns;
        var cellHeight = (rect.W - rect.Y) / rows;
        return new(rect.X + cellWidth * column, rect.Y + cellHeight * row, rect.X + cellWidth * (column + 1), rect.Y + cellHeight * (row + 1));
    }

    private static (Vector3 Right, Vector3 Up) Axes(Gw2ParticleEmitter emitter, Matrix4x4 emitterWorld, Vector3 centre, Vector3 velocity, float rotation, Gw2ParticleCamera camera)
    {
        var toEye = camera.Eye - centre;
        switch (emitter.AlignmentType)
        {
            case 0:
            {
                var normal = Normalised(Vector3.TransformNormal(emitter.AlignmentDirection, emitterWorld), camera.Forward);
                var right = Normalised(Vector3.Cross(Perpendicular(normal), normal), camera.Right);
                return Rotated(right, Vector3.Cross(normal, right), rotation);
            }
            case 2:
            case 3:
            {
                if (velocity.LengthSquared() < MinimumAxisLengthSquared) break;
                var axis = Vector3.Normalize(emitter.AlignmentType == 3 ? -velocity : velocity);
                var side = Normalised(Vector3.Cross(axis, toEye), camera.Up);
                var rotated = Vector3.Transform(side, Quaternion.CreateFromAxisAngle(axis, rotation));
                return (axis, rotated);
            }
            case 4:
            {
                var projected = velocity - Vector3.Dot(velocity, camera.Forward) * camera.Forward;
                if (projected.LengthSquared() < MinimumAxisLengthSquared) break;
                var axis = Vector3.Normalize(projected);
                return (axis, Vector3.Cross(camera.Forward, axis));
            }
            case 5:
            {
                if (velocity.LengthSquared() < MinimumAxisLengthSquared) break;
                var axis = Vector3.Normalize(velocity);
                return (axis, Normalised(Vector3.Cross(axis, toEye), camera.Up));
            }
        }
        return Rotated(camera.Right, camera.Up, rotation);
    }

    private static (Vector3 Right, Vector3 Up) Rotated(Vector3 right, Vector3 up, float rotation)
    {
        var cosine = MathF.Cos(rotation);
        var sine = MathF.Sin(rotation);
        return (right * cosine + up * sine, up * cosine - right * sine);
    }

    private static Vector3 Perpendicular(Vector3 normal) => MathF.Abs(normal.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;

    private static Vector3 Normalised(Vector3 vector, Vector3 fallback) =>
        vector.LengthSquared() < MinimumAxisLengthSquared ? fallback : Vector3.Normalize(vector);
}
