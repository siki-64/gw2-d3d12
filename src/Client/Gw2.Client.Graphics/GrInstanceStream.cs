using System;

namespace Gw2.Client.Graphics;

public sealed class GrInstanceStream
{
    public const int MaximumInstancesPerDraw = 1000;
    private const int InitialCapacity = 1 << 16;

    private GrVertexInstance[] _instances = new GrVertexInstance[InitialCapacity];
    private int _count;

    public void BeginFrame() => _count = 0;

    public GrStaticMeshBatch Append(GrStaticMesh mesh, GrVertexInstance[] instances, int instanceCount)
    {
        if (_count + instanceCount > _instances.Length)
        {
            _instances = new GrVertexInstance[Math.Max(_instances.Length * 2, instanceCount)];
            _count = 0;
        }

        Array.Copy(instances, 0, _instances, _count, instanceCount);
        var batch = new GrStaticMeshBatch(mesh, new ReadOnlyMemory<GrVertexInstance>(_instances, _count, instanceCount));
        _count += instanceCount;
        return batch;
    }
}
