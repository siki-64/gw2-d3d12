using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrGpuScene : IDisposable
{
    private const int ThreadsPerGroup = 64;
    private const int MaximumPlanes = 6;
    private static readonly int ArgumentsStride = Unsafe.SizeOf<IndirectDrawIndexedArguments>();

    public readonly record struct GrDrawGroupKey(GrStaticMesh Mesh, IReadOnlyDictionary<uint, Vector4> Constants, Vector4 ModelColor);

    [StructLayout(LayoutKind.Sequential)]
    private struct GrCullConstants
    {
        public Vector4 Plane0;
        public Vector4 Plane1;
        public Vector4 Plane2;
        public Vector4 Plane3;
        public Vector4 Plane4;
        public Vector4 Plane5;
        public uint PlaneCount;
        public uint InstanceCount;
        public uint CullsCasters;
        public uint TestsOcclusion;
        public Matrix4x4 OcclusionViewProjection;
        public uint OcclusionDepthWidth;
        public uint OcclusionDepthHeight;
        public uint UnusedOcclusion0;
        public uint UnusedOcclusion1;
    }

    public readonly record struct GrOcclusion(GrHiZ HiZ, Matrix4x4 ViewProjection);

    private sealed class GrCullSlot : IDisposable
    {
        public GraphicsBuffer? Rows;
        public int RowCapacity;
        public GraphicsBuffer? Arguments;
        public int ArgumentCapacity;
        public GrStaticMeshIndirectBatch?[] Batches = [];

        public void Dispose()
        {
            Rows?.Dispose();
            Arguments?.Dispose();
        }
    }

    private readonly GrDevice _device;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ShaderProgram _cull;
    private readonly ComputePipeline _cullPipeline;
    private readonly List<GrSceneProp> _props = [];
    private readonly List<GrCullSlot> _slots = [];
    private readonly Dictionary<GrPropLodState, int> _lodStateIndex = new(ReferenceEqualityComparer.Instance);
    private readonly List<GrPropLodState> _lodStates = [];
    private GrSceneProp[] _instances = [];
    private uint[] _lodLevels = [];
    private bool _layoutDirty;
    private GraphicsBuffer? _instanceBuffer;
    private int _instanceCapacity;
    private GraphicsBuffer? _lodLevelBuffer;
    private GrGroupSet _draws = GrGroupSet.Empty;
    private GrGroupSet _casters = GrGroupSet.Empty;

    private sealed class GrGroupSet
    {
        public static readonly GrGroupSet Empty = new([], [], [], []);

        public GrGroupSet(int[] groupOfInstance, GrStaticMesh[] meshes, GrSceneProp[] representatives, IndirectDrawIndexedArguments[] template)
        {
            GroupOfInstance = groupOfInstance;
            Meshes = meshes;
            Representatives = representatives;
            Template = template;
            ActiveCount = new int[meshes.Length];
            BoundsMin = new Vector3[meshes.Length];
            BoundsMax = new Vector3[meshes.Length];
        }

        public int[] GroupOfInstance { get; }
        public GrStaticMesh[] Meshes { get; }
        public GrSceneProp[] Representatives { get; }
        public IndirectDrawIndexedArguments[] Template { get; }
        public int[] ActiveCount { get; }
        public Vector3[] BoundsMin { get; }
        public Vector3[] BoundsMax { get; }
        public int Count => Meshes.Length;
    }

    public GrGpuScene(GrDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _graphicsDevice = device.GraphicsDevice;
        _cull = device.LoadShader("GrGpuCull").GetProgram("GpuCull");
        _cullPipeline = new ComputePipeline(_graphicsDevice, _cull, "GpuCull");
    }

    public int InstanceCount => _instances.Length;

    public int DrawGroupCount => _draws.Count;

    public int CasterGroupCount => _casters.Count;

    public void Add(GrSceneProp prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        if (!prop.DrawsOnGpu && !prop.CastsShadowOnGpu) return;
        _props.Add(prop);
        _layoutDirty = true;
    }

    public void Remove(IReadOnlySet<GrPropLodState> lodStates)
    {
        if (_props.RemoveAll(prop => lodStates.Contains(prop.LodState)) != 0) _layoutDirty = true;
    }

    public void BeginFrame(int firstCasterSlot, int cullSlotCount)
    {
        var stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_layoutDirty)
        {
            Rebuild();
            GrSceneRecordingTimes.PrepareGpuSceneRebuildCount++;
        }
        GrSceneRecordingTimes.PrepareGpuSceneRebuildMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds;
        GrSceneRecordingTimes.PrepareGpuSceneInstanceCount = _instances.Length;
        if (_instances.Length == 0) return;
        for (var slot = 0; slot < cullSlotCount; slot++) Slot(slot, slot < firstCasterSlot ? _draws : _casters);
        for (var state = 0; state < _lodStates.Count; state++) _lodLevels[state] = (uint)_lodStates[state].Level;
        _lodLevelBuffer!.SetData<uint>(_lodLevels.AsSpan(0, _lodStates.Count));
        ResetActive(_draws);
        ResetActive(_casters);
        for (var instance = 0; instance < _instances.Length; instance++)
        {
            var prop = _instances[instance];
            if (prop.LodState.Level != prop.LodLevel) continue;
            AddActive(_draws, _draws.GroupOfInstance[instance], prop.WorldBounds);
            AddActive(_casters, _casters.GroupOfInstance[instance], prop.WorldBounds);
        }
    }

    private static void ResetActive(GrGroupSet set)
    {
        Array.Clear(set.ActiveCount);
        Array.Fill(set.BoundsMin, new Vector3(float.MaxValue));
        Array.Fill(set.BoundsMax, new Vector3(float.MinValue));
    }

    private static void AddActive(GrGroupSet set, int group, BoundingBox bounds)
    {
        if (group == GrGpuSceneGroups<GrStaticMesh>.NoGroup) return;
        set.ActiveCount[group]++;
        set.BoundsMin[group] = Vector3.Min(set.BoundsMin[group], bounds.Min);
        set.BoundsMax[group] = Vector3.Max(set.BoundsMax[group], bounds.Max);
    }

    public void Cull(int slot, GrCullFrustum frustum, bool casters, List<int> visibleGroups, GrOcclusion? occlusion = null)
    {
        ArgumentNullException.ThrowIfNull(frustum);
        ArgumentNullException.ThrowIfNull(visibleGroups);
        visibleGroups.Clear();
        var set = casters ? _casters : _draws;
        for (var group = 0; group < set.Count; group++)
            if (set.ActiveCount[group] != 0 && frustum.Intersects(new BoundingBox(set.BoundsMin[group], set.BoundsMax[group])))
                visibleGroups.Add(group);
        if (visibleGroups.Count == 0) return;

        var cullSlot = Slot(slot, set);
        cullSlot.Arguments!.SetData<IndirectDrawIndexedArguments>(set.Template);
        var planes = frustum.OutwardPlanes;
        var constants = new GrCullConstants { PlaneCount = (uint)Math.Min(planes.Length, MaximumPlanes), InstanceCount = (uint)_instances.Length, CullsCasters = casters ? 1u : 0u };
        var constantPlanes = MemoryMarshal.CreateSpan(ref constants.Plane0, MaximumPlanes);
        for (var plane = 0; plane < constants.PlaneCount; plane++) constantPlanes[plane] = new Vector4(planes[plane].Normal, planes[plane].D);
        var pyramid = occlusion?.HiZ.Pyramid;
        if (occlusion is { } test && pyramid is not null)
        {
            constants.TestsOcclusion = 1;
            constants.OcclusionViewProjection = test.ViewProjection;
            constants.OcclusionDepthWidth = (uint)test.HiZ.DepthWidth;
            constants.OcclusionDepthHeight = (uint)test.HiZ.DepthHeight;
        }
        _cull.ConstantBuffers["Cull"]!.SetValue(constants);
        var parameters = _cull.Parameters;
        parameters["Instances"]!.SetValue(_instanceBuffer);
        parameters["LodLevels"]!.SetValue(_lodLevelBuffer);
        parameters["HiZ"]!.SetValue(constants.TestsOcclusion != 0 ? pyramid : _device.BlackFallback);
        parameters["Rows"]!.SetValue(cullSlot.Rows);
        parameters["Arguments"]!.SetValue(cullSlot.Arguments);
        _graphicsDevice.Dispatch(_cullPipeline, (_instances.Length + ThreadsPerGroup - 1) / ThreadsPerGroup);
    }

    public GrStaticMeshIndirectBatch Batch(int slot, bool casters, int group)
    {
        var set = casters ? _casters : _draws;
        var cullSlot = Slot(slot, set);
        return cullSlot.Batches[group] ??= new GrStaticMeshIndirectBatch(set.Meshes[group], cullSlot.Rows!, cullSlot.Arguments!, (long)group * ArgumentsStride);
    }

    public GrSceneProp DrawGroupProp(int group) => _draws.Representatives[group];

    public GrSceneProp CasterGroupProp(int group) => _casters.Representatives[group];

    private GrCullSlot Slot(int slot, GrGroupSet set)
    {
        while (_slots.Count <= slot) _slots.Add(new GrCullSlot());
        var cullSlot = _slots[slot];
        if (cullSlot.Rows is null || cullSlot.RowCapacity < _instances.Length)
        {
            cullSlot.Rows?.Dispose();
            cullSlot.RowCapacity = _instanceCapacity;
            cullSlot.Rows = new GraphicsBuffer(_graphicsDevice, new BufferDescription
            {
                Size = (long)cullSlot.RowCapacity * GrVertexInstance.SizeInBytes,
                Stride = GrVertexInstance.SizeInBytes,
                Usage = BufferUsage.Vertex | BufferUsage.UnorderedAccess,
                Name = "GpuCullRows",
            });
            cullSlot.Batches = [];
        }
        if (cullSlot.Arguments is null || cullSlot.ArgumentCapacity < set.Count)
        {
            cullSlot.Arguments?.Dispose();
            cullSlot.ArgumentCapacity = GrowCapacity(set.Count);
            cullSlot.Arguments = new GraphicsBuffer(_graphicsDevice, new BufferDescription
            {
                Size = (long)cullSlot.ArgumentCapacity * ArgumentsStride,
                Usage = BufferUsage.Indirect | BufferUsage.UnorderedAccess,
                Name = "GpuCullArguments",
            });
            cullSlot.Batches = [];
        }
        if (cullSlot.Batches.Length != set.Count) cullSlot.Batches = new GrStaticMeshIndirectBatch?[set.Count];
        return cullSlot;
    }

    private void Rebuild()
    {
        _layoutDirty = false;
        var stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _instances = _props.ToArray();
        _lodStateIndex.Clear();
        _lodStates.Clear();
        var draws = GrGpuSceneGroups<GrDrawGroupKey>.Build(_instances.Length, DrawMembers());
        var casters = GrGpuSceneGroups<GrStaticMesh>.Build(_instances.Length, CasterMembers());
        GrSceneRecordingTimes.RebuildGroupsMilliseconds += RebuildStepMilliseconds(ref stepStarted);
        var rows = new GrGpuInstance[_instances.Length];
        for (var instance = 0; instance < _instances.Length; instance++)
        {
            var prop = _instances[instance];
            if (!_lodStateIndex.TryGetValue(prop.LodState, out var lodState))
            {
                _lodStateIndex[prop.LodState] = lodState = _lodStates.Count;
                _lodStates.Add(prop.LodState);
            }
            rows[instance] = new GrGpuInstance
            {
                Row = GrVertexInstance.From(prop.World, prop.ModelColor),
                BoundsMin = prop.WorldBounds.Min,
                BoundsMax = prop.WorldBounds.Max,
                LodState = (uint)lodState,
                LodLevel = (uint)prop.LodLevel,
                DrawGroup = GroupIndex(draws.GroupOfInstance[instance]),
                CasterGroup = GroupIndex(casters.GroupOfInstance[instance]),
            };
        }

        GrSceneRecordingTimes.RebuildRowsMilliseconds += RebuildStepMilliseconds(ref stepStarted);
        _draws = GroupSet(draws, draws.Keys.Select(key => key.Mesh).ToArray());
        _casters = GroupSet(casters, casters.Keys.ToArray());
        foreach (var slot in _slots) slot.Batches = [];
        GrSceneRecordingTimes.RebuildGroupSetsMilliseconds += RebuildStepMilliseconds(ref stepStarted);
        if (_instances.Length == 0) return;

        if (_instanceBuffer is null || _instanceCapacity < _instances.Length)
        {
            _instanceBuffer?.Dispose();
            _instanceCapacity = GrowCapacity(_instances.Length);
            _instanceBuffer = new GraphicsBuffer(_graphicsDevice, new BufferDescription
            {
                Size = (long)_instanceCapacity * GrGpuInstance.SizeInBytes,
                Stride = GrGpuInstance.SizeInBytes,
                Usage = BufferUsage.Structured,
                Name = "GpuCullInstances",
            });
        }
        _instanceBuffer.SetData<GrGpuInstance>(rows);
        GrSceneRecordingTimes.RebuildUploadMilliseconds += RebuildStepMilliseconds(ref stepStarted);
        if (_lodLevelBuffer is null || _lodLevels.Length < _lodStates.Count)
        {
            _lodLevelBuffer?.Dispose();
            _lodLevels = new uint[GrowCapacity(_lodStates.Count)];
            _lodLevelBuffer = new GraphicsBuffer(_graphicsDevice, new BufferDescription
            {
                Size = (long)_lodLevels.Length * sizeof(uint),
                Stride = sizeof(uint),
                Usage = BufferUsage.Structured,
                Name = "GpuCullLodLevels",
            });
        }
    }

    private IEnumerable<(int Instance, GrDrawGroupKey Key)> DrawMembers()
    {
        for (var instance = 0; instance < _instances.Length; instance++)
            if (_instances[instance] is { DrawsOnGpu: true } prop)
                yield return (instance, new GrDrawGroupKey(prop.Mesh, prop.Constants, prop.ModelColor));
    }

    private IEnumerable<(int Instance, GrStaticMesh Key)> CasterMembers()
    {
        for (var instance = 0; instance < _instances.Length; instance++)
            if (_instances[instance] is { CastsShadowOnGpu: true } prop)
                yield return (instance, prop.Mesh);
    }

    private GrGroupSet GroupSet<TKey>(GrGpuSceneGroups<TKey> groups, GrStaticMesh[] meshes) where TKey : notnull
    {
        var representatives = new GrSceneProp[groups.Count];
        for (var instance = _instances.Length - 1; instance >= 0; instance--)
            if (groups.GroupOfInstance[instance] is var group and not GrGpuSceneGroups<TKey>.NoGroup)
                representatives[group] = _instances[instance];
        var template = new IndirectDrawIndexedArguments[groups.Count];
        for (var group = 0; group < groups.Count; group++)
            template[group] = new IndirectDrawIndexedArguments((uint)meshes[group].IndexCount, 0, 0, 0, (uint)groups.First[group]);
        return new GrGroupSet(groups.GroupOfInstance.ToArray(), meshes, representatives, template);
    }

    private static double RebuildStepMilliseconds(ref long started)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started, now).TotalMilliseconds;
        started = now;
        return elapsed;
    }

    private static uint GroupIndex(int group) => group == GrGpuSceneGroups<GrDrawGroupKey>.NoGroup ? GrGpuInstance.NoGroup : (uint)group;

    private static int GrowCapacity(int count) => Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)count));

    public void Dispose()
    {
        foreach (var slot in _slots) slot.Dispose();
        _slots.Clear();
        _instanceBuffer?.Dispose();
        _lodLevelBuffer?.Dispose();
        _cullPipeline.Dispose();
    }
}
