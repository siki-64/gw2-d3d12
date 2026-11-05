using System.Numerics;
using Gw2.Formats;

namespace Gw2.Client.Model;

public sealed record Gw2CollisionMesh(
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<ushort> Indices,
    IReadOnlyList<ushort> Surfaces);

public sealed record Gw2CollisionBox(Vector3 Dimensions, Vector3 Position, Vector4 Rotation, byte Surface);

public sealed record Gw2CollisionSphere(float Radius, Vector3 Position, byte Surface);

public sealed record Gw2CollisionCapsule(Vector3 P0, Vector3 P1, float Radius, byte Surface);

public sealed class Gw2ModelCollision
{
    public IReadOnlyList<Gw2CollisionMesh> Meshes { get; init; } = Array.Empty<Gw2CollisionMesh>();
    public IReadOnlyList<Gw2CollisionBox> Boxes { get; init; } = Array.Empty<Gw2CollisionBox>();
    public IReadOnlyList<Gw2CollisionSphere> Spheres { get; init; } = Array.Empty<Gw2CollisionSphere>();
    public IReadOnlyList<Gw2CollisionCapsule> Capsules { get; init; } = Array.Empty<Gw2CollisionCapsule>();

    public bool HasCollision => Meshes.Count > 0 || Boxes.Count > 0 || Spheres.Count > 0 || Capsules.Count > 0;
}

public sealed class Gw2Model
{
    public uint BaseId { get; init; }
    public IReadOnlyList<Gw2Mesh> Meshes { get; init; } = Array.Empty<Gw2Mesh>();
    public IReadOnlyList<Gw2Material> Materials { get; init; } = Array.Empty<Gw2Material>();
    public Gw2ModelCollision? Collision { get; init; }
}
