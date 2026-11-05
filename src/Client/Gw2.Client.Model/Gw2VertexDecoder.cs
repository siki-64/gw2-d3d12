using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public static class Gw2VertexDecoder
{
    public const int UvChannelCount = 8;

    public static Gw2Mesh Decode(Gw2ModelMesh mesh)
    {
        if (mesh is null) throw new ArgumentNullException(nameof(mesh));

        return new Gw2Mesh
        {
            Name = unchecked((long)mesh.Name),
            MaterialIndex = unchecked((uint)Math.Max(0, mesh.MaterialIndex)),
            LodLevel = mesh.LodLevel,
            Flags = mesh.Flags,
            VisBone = unchecked((long)mesh.VisBone),
            MaterialName = mesh.MaterialName,
            SeamVertIndices = mesh.SeamVertIndices ?? Array.Empty<uint>(),
            BoneBindings = (mesh.BoneBindings ?? Array.Empty<ulong>()).Select(bone => unchecked((long)bone)).ToArray(),
            Transforms = mesh.Transforms ?? Array.Empty<uint>(),
            MinBound = mesh.MinBound,
            MaxBound = mesh.MaxBound,
            Bounds = (mesh.Bounds ?? Array.Empty<Gw2ModelMeshBounds>())
                .Select(bound => new Gw2MeshBounds(bound.Center, bound.BoxExtent, bound.SphereRadius))
                .ToArray(),
            MorphTargets = (mesh.MorphTargets ?? Array.Empty<Gw2ModelMorphTarget>())
                .Select(target => new Gw2MorphTarget(
                    target.Positions.Select(vertex => new Gw2MorphVertex(vertex.Index, vertex.Position)).ToArray(),
                    target.Normals.Select(vertex => new Gw2MorphVertex(vertex.Index, vertex.Position)).ToArray(),
                    unchecked((long)target.Mesh)))
                .ToArray(),
            Vertices = DecodeVertices(mesh.Vertices),
            Indices = (mesh.Indices ?? Array.Empty<ushort>()).Select(index => (int)index).ToArray(),
            Lods = (mesh.Lods ?? Array.Empty<IReadOnlyList<ushort>>())
                .Select(lod => lod.Select(index => (int)index).ToArray())
                .ToArray(),
        };
    }

    public static Gw2Vertex[] DecodeVertices(Gw2VertexBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));

        var count = checked((int)buffer.Count);
        var stride = buffer.Stride;
        if (count <= 0 || stride <= 0) return Array.Empty<Gw2Vertex>();

        var position = FindAttribute(buffer, "Position") ?? FindAttribute(buffer, "PositionHalf");
        if (position is null) return Array.Empty<Gw2Vertex>();

        var normal = FindAttribute(buffer, "Normal");
        var tangent = FindAttribute(buffer, "Tangent");
        var bitangent = FindAttribute(buffer, "Bitangent");
        var frame = FindAttribute(buffer, "TangentFrame");
        var color = FindAttribute(buffer, "Color");
        var weights = FindAttribute(buffer, "Weights");
        var group = FindAttribute(buffer, "Group");

        var uvAttributes = new Gw2VertexAttribute?[UvChannelCount];
        for (var channel = 0; channel < UvChannelCount; channel++) uvAttributes[channel] = FindUv(buffer, channel);

        var bytes = buffer.Bytes.Span;
        var result = new Gw2Vertex[count];

        for (var index = 0; index < count; index++)
        {
            var offset = index * stride;
            var vertex = new Gw2Vertex
            {
                Position = ReadVector3(bytes, offset + position.Offset, position.Size == Gw2VertexFvf.PositionSize),
            };

            if (color is not null)
            {
                vertex.Color = ReadColor(bytes, offset + color.Offset);
                vertex.Flags |= Gw2VertexFlags.Color;
            }

            if (weights is not null && group is not null)
            {
                vertex.BoneWeights = ReadPackedBytes(bytes, offset + weights.Offset);
                vertex.BoneIndices = ReadPackedBytes(bytes, offset + group.Offset);
                vertex.Flags |= Gw2VertexFlags.Skin;
            }

            if (normal is not null)
            {
                vertex.Normal = Normalize(ReadVector3(bytes, offset + normal.Offset, true));
                vertex.Flags |= Gw2VertexFlags.Normal;
            }
            else if (frame is not null)
            {
                DecodePackedTangentFrame(bytes, offset + frame.Offset, out var frameNormal, out var frameTangent);
                vertex.Normal = frameNormal;
                vertex.Tangent = frameTangent;
                vertex.Flags |= Gw2VertexFlags.Normal | Gw2VertexFlags.Tangent;
            }

            if (tangent is not null)
            {
                var decodedTangent = Normalize(ReadVector3(bytes, offset + tangent.Offset, true));
                var handedness = 1f;
                if (bitangent is not null)
                {
                    var decodedBitangent = ReadVector3(bytes, offset + bitangent.Offset, true);
                    handedness = Vector3.Dot(Vector3.Cross(vertex.Normal, decodedTangent), decodedBitangent) < 0f ? -1f : 1f;
                }

                vertex.Tangent = new Vector4(decodedTangent.X, decodedTangent.Y, decodedTangent.Z, handedness);
                vertex.Flags |= Gw2VertexFlags.Tangent;
            }

            for (var channel = 0; channel < UvChannelCount; channel++)
            {
                var attribute = uvAttributes[channel];
                if (attribute is null) continue;
                vertex.SetUv(channel, ReadUv(bytes, offset + attribute.Offset, attribute.Name));
            }

            result[index] = vertex;
        }

        return result;
    }

    public static void DecodePackedTangentFrame(ReadOnlySpan<byte> bytes, int offset, out Vector3 normal, out Vector4 tangent)
    {
        normal = DecodePackedVector(bytes, offset);
        var decodedTangent = DecodePackedVector(bytes, offset + 4);
        var bitangent = DecodePackedVector(bytes, offset + 8);
        var handedness = Vector3.Dot(Vector3.Cross(normal, decodedTangent), bitangent) < 0f ? -1f : 1f;
        tangent = new Vector4(decodedTangent.X, decodedTangent.Y, decodedTangent.Z, handedness);
    }

    public static Vector3 DecodePackedVector(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + 4 > bytes.Length) return Vector3.Zero;
        var packed = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
        var x = ((packed >> 16) & 0xFF) * (2f / 255f) - 1f;
        var y = ((packed >> 8) & 0xFF) * (2f / 255f) - 1f;
        var z = (packed & 0xFF) * (2f / 255f) - 1f;
        var vector = new Vector3(x, y, z);
        return vector.LengthSquared() >= 1e-12f ? Vector3.Normalize(vector) : vector;
    }

    public static Vector3 ReadVector3(ReadOnlySpan<byte> bytes, int offset, bool isFloat)
    {
        if (isFloat)
        {
            if (offset + 12 > bytes.Length) return Vector3.Zero;
            return new Vector3(
                Gw2Binary.ReadSingleLittleEndian(bytes[offset..]),
                Gw2Binary.ReadSingleLittleEndian(bytes[(offset + 4)..]),
                Gw2Binary.ReadSingleLittleEndian(bytes[(offset + 8)..]));
        }

        if (offset + 6 > bytes.Length) return Vector3.Zero;
        return new Vector3(
            Gw2Binary.ReadHalfLittleEndian(bytes[offset..]),
            Gw2Binary.ReadHalfLittleEndian(bytes[(offset + 2)..]),
            Gw2Binary.ReadHalfLittleEndian(bytes[(offset + 4)..]));
    }

    public static Vector2 ReadUv(ReadOnlySpan<byte> bytes, int offset, string? name)
    {
        if (name is not null && name.StartsWith("UV16", StringComparison.Ordinal))
        {
            if (offset + 4 > bytes.Length) return Vector2.Zero;
            return new Vector2(
                Gw2Binary.ReadHalfLittleEndian(bytes[offset..]),
                Gw2Binary.ReadHalfLittleEndian(bytes[(offset + 2)..]));
        }

        if (offset + 8 > bytes.Length) return Vector2.Zero;
        return new Vector2(
            Gw2Binary.ReadSingleLittleEndian(bytes[offset..]),
            Gw2Binary.ReadSingleLittleEndian(bytes[(offset + 4)..]));
    }

    public static Gw2Color32 ReadColor(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + 4 > bytes.Length) return Gw2Color32.White;
        return new Gw2Color32(bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]);
    }

    public static uint ReadPackedBytes(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + 4 > bytes.Length) return 0;
        return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
    }

    public static Gw2VertexAttribute? FindAttribute(Gw2VertexBuffer buffer, string name)
    {
        foreach (var attribute in buffer.Attributes)
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
                return attribute;
        return null;
    }

    public static Gw2VertexAttribute? FindUv(Gw2VertexBuffer buffer, int channel)
    {
        Gw2VertexAttribute? match = null;
        var semantic = $"TEXCOORD{channel}";
        foreach (var attribute in buffer.Attributes)
        {
            if (!string.Equals(attribute.Semantic, semantic, StringComparison.Ordinal)) continue;
            if (match is null || (attribute.Name is not null && attribute.Name.StartsWith("UV32", StringComparison.Ordinal))) match = attribute;
        }

        return match;
    }

    private static Vector3 Normalize(Vector3 value) =>
        value.LengthSquared() >= 1e-12f ? Vector3.Normalize(value) : value;
}
