using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

public sealed class HavokExtendedMesh
{
    public const int SubpartKeyBits = 12;
    public const float TriangleRadius = 0f;

    private readonly float[] _vertices;
    private readonly ushort[] _indices;
    private readonly int _flipMask;

    public HavokExtendedMesh(float[] vertices, ushort[] indices, float scale, int flipMask = 0)
    {
        _vertices = vertices;
        _indices = indices;
        _flipMask = flipMask;
        Scaling = Vector128.Create(scale, scale, scale, 0f);
        Rotation = Vector128.Create(0f, 0f, 0f, 1f);
        Translation = Vector128<float>.Zero;
    }

    public Vector128<float> Scaling { get; init; }
    public Vector128<float> Rotation { get; init; }
    public Vector128<float> Translation { get; init; }
    public int TriangleCount => _indices.Length / 3;

    public void Triangle(uint key, Span<Vector128<float>> vertices)
    {
        var triangle = (int)(key & (0xFFFFFFFFu >> SubpartKeyBits));
        var flip = (short)(triangle & _flipMask);
        var first = 3 * triangle;
        vertices[0] = Transform(_indices[first]);
        vertices[1] = Transform(_indices[first + (short)(flip + 1)]);
        vertices[2] = Transform(_indices[first + (short)((flip ^ 1) + 1)]);
    }

    private Vector128<float> Transform(int index)
    {
        var raw = Vector128.Create(_vertices[3 * index], _vertices[3 * index + 1], _vertices[3 * index + 2], 0f);
        var vertex = Sse.Multiply(Scaling, raw);
        var rotation = Rotation;
        var product = Sse.Multiply(rotation, vertex);
        var real = Splat(rotation, 3);
        var cross = Sse.Subtract(Sse.Multiply(YZX(vertex), rotation), Sse.Multiply(YZX(rotation), vertex));
        var dot = Sse.Add(Sse.Add(Splat(product, 1), Splat(product, 0)), Splat(product, 2));
        var rotated = Sse.Add(
            Sse.Add(Sse.Multiply(dot, rotation), Sse.Multiply(Sse.Subtract(Sse.Multiply(real, real), Broadcast(0.5f)), vertex)),
            Sse.Multiply(YZX(cross), real));
        return Sse.Add(Sse.Add(rotated, rotated), Translation);
    }
}
