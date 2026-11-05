using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public static class Gw2ClothMesh
{
    private const int MaximumBoneInfluences = 4;

    public static Gw2Mesh Decode(Gw2ModelCloth cloth, string? materialName)
    {
        ArgumentNullException.ThrowIfNull(cloth);
        var stride = new Gw2VertexBuffer(cloth.MeshFvf, 0, ReadOnlyMemory<byte>.Empty).Stride;
        var vertexCount = stride == 0 ? 0 : cloth.MeshVertices.Length / stride;
        var vertices = Gw2VertexDecoder.DecodeVertices(new Gw2VertexBuffer(cloth.MeshFvf, (uint)vertexCount, cloth.MeshVertices));
        var indices = cloth.Indices.Select(index => (int)index).ToArray();
        var boneBindings = cloth.Groups.SelectMany(group => group.Select(weight => weight.Token)).Distinct().ToList();

        for (var index = 0; index < vertices.Length && index < cloth.Groups.Count; index++)
        {
            var influences = cloth.Groups[index].OrderByDescending(weight => weight.Weight).Take(MaximumBoneInfluences).ToArray();
            uint packedWeights = 0;
            uint packedIndices = 0;
            for (var slot = 0; slot < influences.Length; slot++)
            {
                packedWeights |= (uint)influences[slot].Weight << (8 * slot);
                packedIndices |= (uint)boneBindings.IndexOf(influences[slot].Token) << (8 * slot);
            }
            vertices[index].BoneWeights = packedWeights;
            vertices[index].BoneIndices = packedIndices;
            vertices[index].Flags |= Gw2VertexFlags.Skin;
        }
        if (!vertices.Any(vertex => vertex.HasNormal)) WriteTriangleNormals(vertices, indices);

        return new Gw2Mesh
        {
            MaterialIndex = cloth.MaterialIndex,
            MaterialName = materialName,
            VisBone = unchecked((long)cloth.VisBone),
            BoneBindings = boneBindings.Select(token => unchecked((long)token)).ToArray(),
            Vertices = vertices,
            Indices = indices,
        };
    }

    private static void WriteTriangleNormals(Gw2Vertex[] vertices, int[] indices)
    {
        var sums = new Vector3[vertices.Length];
        for (var triangle = 0; triangle + 2 < indices.Length; triangle += 3)
        {
            var (a, b, c) = (indices[triangle], indices[triangle + 1], indices[triangle + 2]);
            if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) continue;
            var areaNormal = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
            sums[a] += areaNormal;
            sums[b] += areaNormal;
            sums[c] += areaNormal;
        }
        for (var index = 0; index < vertices.Length; index++)
        {
            if (sums[index] == Vector3.Zero) continue;
            vertices[index].Normal = Vector3.Normalize(sums[index]);
            vertices[index].Flags |= Gw2VertexFlags.Normal;
        }
    }
}
