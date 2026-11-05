using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrDepthCasterBatcher
{
    private readonly GrInstanceStream _stream;
    private readonly GrVertexInstance[] _scratch = new GrVertexInstance[GrInstanceStream.MaximumInstancesPerDraw];
    private readonly Dictionary<(GrStaticMesh Mesh, Texture? AlphaMask, SamplerState? AlphaMaskSampler), List<GrDepthCaster>> _groups = new();
    private readonly List<GrDepthCaster> _batched = new();

    public GrDepthCasterBatcher(GrInstanceStream stream) => _stream = stream;

    public IReadOnlyList<GrDepthCaster> Batch(List<GrDepthCaster> casters)
    {
        foreach (var group in _groups.Values) group.Clear();
        _batched.Clear();
        foreach (var caster in casters)
        {
            if (caster.Mesh is not GrStaticMesh mesh)
            {
                _batched.Add(caster);
                continue;
            }
            var key = (mesh, caster.AlphaMask, caster.AlphaMaskSampler);
            if (!_groups.TryGetValue(key, out var group)) _groups[key] = group = new List<GrDepthCaster>();
            group.Add(caster);
        }

        foreach (var ((mesh, alphaMask, alphaMaskSampler), group) in _groups)
            for (var first = 0; first < group.Count; first += GrInstanceStream.MaximumInstancesPerDraw)
            {
                var count = System.Math.Min(GrInstanceStream.MaximumInstancesPerDraw, group.Count - first);
                if (count == 1)
                {
                    _batched.Add(group[first]);
                    continue;
                }
                for (var index = 0; index < count; index++) _scratch[index] = GrVertexInstance.From(group[first + index].World, Vector4.One);
                _batched.Add(new GrDepthCaster(_stream.Append(mesh, _scratch, count), Matrix4x4.Identity, alphaMask, alphaMaskSampler));
            }
        return _batched;
    }
}
