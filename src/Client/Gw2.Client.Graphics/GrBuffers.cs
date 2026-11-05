using System;
using System.Runtime.CompilerServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public static class GrBuffers
{
    public static GraphicsBuffer Vertex<T>(GraphicsDevice device, ReadOnlySpan<T> vertices, string name, BufferUsage extraUsage = BufferUsage.None) where T : unmanaged
    {
        var buffer = new GraphicsBuffer(device, new BufferDescription
        {
            Size = (long)Math.Max(vertices.Length, 1) * Unsafe.SizeOf<T>(),
            Stride = Unsafe.SizeOf<T>(),
            Usage = BufferUsage.Vertex | extraUsage,
            Name = name,
        });
        if (!vertices.IsEmpty) buffer.SetData(vertices);
        return buffer;
    }

    public static GraphicsBuffer Index(GraphicsDevice device, ReadOnlySpan<int> indices, string name)
    {
        var buffer = new GraphicsBuffer(device, new BufferDescription
        {
            Size = (long)Math.Max(indices.Length, 1) * sizeof(int),
            Stride = sizeof(int),
            Usage = BufferUsage.Index,
            Name = name,
        });
        if (!indices.IsEmpty) buffer.SetData(indices);
        return buffer;
    }

    public static GraphicsBuffer Index(GraphicsDevice device, ReadOnlySpan<ushort> indices, string name)
    {
        var buffer = new GraphicsBuffer(device, new BufferDescription
        {
            Size = (long)Math.Max(indices.Length, 1) * sizeof(ushort),
            Stride = sizeof(ushort),
            Usage = BufferUsage.Index,
            Name = name,
        });
        if (!indices.IsEmpty) buffer.SetData(indices);
        return buffer;
    }
}
