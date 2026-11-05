using System.Numerics;

namespace Gw2.Client.Model;

[Flags]
public enum Gw2VertexFlags
{
    None = 0,
    Normal = 1,
    Tangent = 2,
    Color = 4,
    Uv0 = 8,
    Uv1 = 16,
    Uv2 = 32,
    Uv3 = 64,
    Skin = 128,
    Uv4 = 256,
    Uv5 = 512,
    Uv6 = 1024,
    Uv7 = 2048,
}

public readonly record struct Gw2Color32(byte R, byte G, byte B, byte A)
{
    public static Gw2Color32 White => new(255, 255, 255, 255);
}

public struct Gw2Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector2 Uv0;
    public Vector2 Uv1;
    public Vector2 Uv2;
    public Vector2 Uv3;
    public Vector2 Uv4;
    public Vector2 Uv5;
    public Vector2 Uv6;
    public Vector2 Uv7;
    public Gw2Color32 Color;
    public Gw2VertexFlags Flags;
    public uint BoneWeights;
    public uint BoneIndices;

    public bool HasNormal => (Flags & Gw2VertexFlags.Normal) != 0;
    public bool HasTangent => (Flags & Gw2VertexFlags.Tangent) != 0;
    public bool HasColor => (Flags & Gw2VertexFlags.Color) != 0;
    public bool HasUv0 => (Flags & Gw2VertexFlags.Uv0) != 0;
    public bool HasUv1 => (Flags & Gw2VertexFlags.Uv1) != 0;
    public bool HasUv2 => (Flags & Gw2VertexFlags.Uv2) != 0;
    public bool HasUv3 => (Flags & Gw2VertexFlags.Uv3) != 0;
    public bool HasSkin => (Flags & Gw2VertexFlags.Skin) != 0;

    public int BoneIndex(int i) => (int)((BoneIndices >> (8 * i)) & 0xFF);

    public float BoneWeight(int i) => ((BoneWeights >> (8 * i)) & 0xFF) / 255f;

    public Vector2 Uv(int channel) => channel switch
    {
        0 => Uv0,
        1 => Uv1,
        2 => Uv2,
        3 => Uv3,
        4 => Uv4,
        5 => Uv5,
        6 => Uv6,
        7 => Uv7,
        _ => Vector2.Zero,
    };

    public void SetUv(int channel, Vector2 value)
    {
        switch (channel)
        {
            case 0: Uv0 = value; Flags |= Gw2VertexFlags.Uv0; break;
            case 1: Uv1 = value; Flags |= Gw2VertexFlags.Uv1; break;
            case 2: Uv2 = value; Flags |= Gw2VertexFlags.Uv2; break;
            case 3: Uv3 = value; Flags |= Gw2VertexFlags.Uv3; break;
            case 4: Uv4 = value; Flags |= Gw2VertexFlags.Uv4; break;
            case 5: Uv5 = value; Flags |= Gw2VertexFlags.Uv5; break;
            case 6: Uv6 = value; Flags |= Gw2VertexFlags.Uv6; break;
            case 7: Uv7 = value; Flags |= Gw2VertexFlags.Uv7; break;
        }
    }

    public bool HasUv(int channel) => (Flags & UvFlag(channel)) != 0;

    private static Gw2VertexFlags UvFlag(int channel) => channel switch
    {
        0 => Gw2VertexFlags.Uv0,
        1 => Gw2VertexFlags.Uv1,
        2 => Gw2VertexFlags.Uv2,
        3 => Gw2VertexFlags.Uv3,
        4 => Gw2VertexFlags.Uv4,
        5 => Gw2VertexFlags.Uv5,
        6 => Gw2VertexFlags.Uv6,
        7 => Gw2VertexFlags.Uv7,
        _ => Gw2VertexFlags.None,
    };
}

public sealed record Gw2MeshBounds(Vector3 Center, Vector3 BoxExtent, float SphereRadius);

public sealed record Gw2MorphVertex(int Index, Vector3 Position);

public sealed record Gw2MorphTarget(
    IReadOnlyList<Gw2MorphVertex> Positions,
    IReadOnlyList<Gw2MorphVertex> Normals,
    long Mesh);

public sealed class Gw2Mesh
{
    public long Name { get; init; }
    public uint MaterialIndex { get; init; }
    public int LodLevel { get; init; }
    public uint Flags { get; init; }
    public long VisBone { get; init; }
    public string? MaterialName { get; init; }
    public IReadOnlyList<uint> SeamVertIndices { get; init; } = Array.Empty<uint>();
    public IReadOnlyList<long> BoneBindings { get; init; } = Array.Empty<long>();
    public IReadOnlyList<uint> Transforms { get; init; } = Array.Empty<uint>();
    public Vector3? MinBound { get; init; }
    public Vector3? MaxBound { get; init; }
    public IReadOnlyList<Gw2MeshBounds> Bounds { get; init; } = Array.Empty<Gw2MeshBounds>();
    public IReadOnlyList<Gw2MorphTarget> MorphTargets { get; init; } = Array.Empty<Gw2MorphTarget>();
    public Gw2Vertex[] Vertices { get; init; } = Array.Empty<Gw2Vertex>();
    public int[] Indices { get; init; } = Array.Empty<int>();
    public int[][] Lods { get; init; } = Array.Empty<int[]>();

    public bool IsRenderable => Vertices.Length > 0 && Indices.Length > 0;
}
