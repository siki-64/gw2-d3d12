using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2ParticleFieldVertex(
    Vector3 Position,
    byte Phase,
    byte CornerU,
    byte CornerV,
    byte Opacity,
    Vector2 Scale,
    float Angle,
    float Rotation,
    float StartFrame,
    float CornerCode);

public sealed class Gw2ParticleFieldMesh
{
    public const uint UniformScaleFlag = 0x1;
    public const uint FlatClustersFlag = 0x100;
    public const int VerticesPerParticle = 4;
    public static readonly ushort[] QuadIndices = [0, 1, 2, 2, 1, 3];

    private const int PhaseModulus = 255;
    private static readonly (byte U, byte V, float Code)[] Corners = [(0, 0, 0.25f), (byte.MaxValue, 0, 0.75f), (0, byte.MaxValue, -0.25f), (byte.MaxValue, byte.MaxValue, -0.75f)];

    private Gw2ParticleFieldMesh(Gw2ParticleFieldVertex[] vertices) => Vertices = vertices;

    public Gw2ParticleFieldVertex[] Vertices { get; }

    public int ParticleCount => Vertices.Length / VerticesPerParticle;

    public static Gw2ParticleFieldMesh Build(Gw2MapEnvParticleField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var random = new Gw2MinimalStandardRandom(field.Seed);
        var clusterCount = Math.Max(field.ClusterCount, 1u);
        var clusters = new Vector3[clusterCount];
        for (var index = 0; index < clusters.Length; index++)
        {
            var x = random.NextFloat();
            var y = random.NextFloat();
            var z = random.NextFloat();
            clusters[index] = new(z, y, x);
        }

        var frameCount = Math.Max(field.TextureColumns * field.TextureRows, 1u);
        var vertices = new Gw2ParticleFieldVertex[field.ParticleCount * VerticesPerParticle];
        for (var particle = 0; particle < field.ParticleCount; particle++)
        {
            var z = random.NextFloat();
            var y = random.NextFloat();
            var x = random.NextFloat();
            var position = new Vector3(x, y, z);
            var clustering = Lerp(field.Clustering, random.NextFloat());
            if (clustering != 0f)
            {
                var cluster = clusters[random.NextUInt() % clusterCount];
                var toCluster = new Vector3(NearestImage(cluster.X - x), NearestImage(cluster.Y - y), NearestImage(cluster.Z - z));
                if ((field.Flags & FlatClustersFlag) != 0) toCluster.Z = 0f;
                var clusteredX = WrapUnit(toCluster.X * clustering + x);
                var clusteredY = toCluster.Y * clustering + y;
                clusteredY = clusteredY < 0f ? clusteredY + 1f : clusteredX > 1f ? clusteredY - 1f : clusteredY;
                position = new(clusteredX, clusteredY, WrapUnit(toCluster.Z * clustering + z));
            }

            var phase = (byte)(random.NextUInt() % PhaseModulus);
            var opacity = (byte)(int)(Lerp(field.Opacity, random.NextFloat()) * 255f);
            Vector2 scale;
            if ((field.Flags & UniformScaleFlag) != 0)
            {
                var uniform = Lerp(field.ScaleX, random.NextFloat());
                scale = new(uniform, uniform);
            }
            else
            {
                var scaleY = Lerp(field.ScaleY, random.NextFloat());
                scale = new(Lerp(field.ScaleX, random.NextFloat()), scaleY);
            }

            var rotation = Lerp(field.Rotation, random.NextFloat());
            var angle = Lerp(field.Angle, random.NextFloat());
            var startFrame = (float)(int)(random.NextUInt() % frameCount);
            for (var corner = 0; corner < VerticesPerParticle; corner++)
            {
                var (u, v, code) = Corners[corner];
                vertices[particle * VerticesPerParticle + corner] = new(position, phase, u, v, opacity, scale, angle, rotation, startFrame, code);
            }
        }

        return new(vertices);
    }

    private static float Lerp(Vector2 range, float amount) => amount * (range.Y - range.X) + range.X;

    private static float NearestImage(float delta)
    {
        var magnitude = MathF.Abs(delta);
        return magnitude > 0.5f ? delta / -magnitude * (1f - magnitude) : delta;
    }

    private static float WrapUnit(float value) => value < 0f ? value + 1f : value > 1f ? value - 1f : value;
}

public sealed class Gw2MinimalStandardRandom
{
    private const uint DefaultSeed = 123459876;
    private const uint Multiplier = 48271;
    private const uint Modulus = 0x7FFFFFFF;
    private const uint Quotient = 44488;
    private const float FloatScale = 4.6566129e-10f;

    private uint _state;

    public Gw2MinimalStandardRandom(uint seed) => _state = seed == 0 ? DefaultSeed : seed;

    public uint NextUInt()
    {
        var next = unchecked(Multiplier * _state - Modulus * (_state / Quotient));
        if (next > Modulus) next = unchecked(next + 0x80000000);
        if (next == 0) next = DefaultSeed;
        _state = next;
        return next;
    }

    public float NextFloat() => (int)NextUInt() * FloatScale;
}
