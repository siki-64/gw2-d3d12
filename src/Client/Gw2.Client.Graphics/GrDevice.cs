using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrDevice : IDisposable
{
    private const int MaximumTextureSize = 16384;
    private static readonly GrTargetBinding BackBufferTargets = new(null, null, true);
    private readonly ConcurrentDictionary<GrPipelineKey, GraphicsPipeline> _pipelines = new();
    private readonly System.Threading.Lock _backBufferDepthLock = new();
    private readonly List<ShaderLibrary> _shaderLibraries = new();
    private Texture2D? _backBufferDepth;
    private readonly Dictionary<uint, ShaderProgram> _shaderEffects = new();
    [ThreadStatic]
    private static GrTargetBinding? _boundTargets;

    public GrDevice(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        BlackFallback = SolidTexture(Color.Transparent, "MaterialBlackFallback");
        BlackFallbackCube = SolidCube(Color.Black, "MaterialBlackFallbackCube");
    }

    public GraphicsDevice GraphicsDevice { get; }

    public string? DecompiledShaderDirectory { get; init; }

    public GrMaterialShaderCache? MaterialShaderCache { get; init; }

    public float RenderScale { get; set; } = Gw2RenderScale.Native;

    public GrTargetBinding BoundTargets
    {
        get => _boundTargets ?? BackBufferTargets;
        private set => _boundTargets = value;
    }

    public GrCommandListBinding Record(GraphicsCommandList commandList)
    {
        ArgumentNullException.ThrowIfNull(commandList);
        var previousTargets = _boundTargets;
        var binding = commandList.Bind();
        _boundTargets = null;
        GrDrawConstants.ForgetAppliedFrames();
        return new GrCommandListBinding(binding, previousTargets);
    }

    internal static void EndRecording(GrTargetBinding? previousTargets)
    {
        _boundTargets = previousTargets;
        GrDrawConstants.ForgetAppliedFrames();
    }

    public (int Width, int Height) RenderTargetSize() =>
        Gw2RenderScale.TargetSize(RenderScale, GraphicsDevice.BackBufferWidth, GraphicsDevice.BackBufferHeight, MaximumTextureSize);

    public ShaderLibrary LoadShader(string name)
    {
        using var stream = typeof(GrDevice).Assembly.GetManifestResourceStream($"Shaders/{name}.crystalshader")
            ?? throw new InvalidOperationException($"Shader {name} is not embedded.");
        var library = new ShaderLibrary(GraphicsDevice, stream, name);
        lock (_shaderLibraries) _shaderLibraries.Add(library);
        return library;
    }

    public ShaderProgram ShaderEffect(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment)
    {
        var key = GrShaderEffectDescription.EffectKeyOf(vertex, fragment);
        lock (_shaderEffects)
        {
            if (_shaderEffects.TryGetValue(key, out var cached)) return cached;
            var description = DescribeMaterial(vertex, fragment);
            ShaderLibrary library;
            try
            {
                library = new ShaderLibrary(GraphicsDevice, [description], $"Material{key:X8}");
            }
            catch (Exception exception) when (description.UsesDescriptorIndices && exception is ArgumentException or GraphicsException)
            {
                description = GrShaderEffectDescription.Describe(vertex, fragment);
                library = new ShaderLibrary(GraphicsDevice, [description], $"Material{key:X8}");
            }
            lock (_shaderLibraries) _shaderLibraries.Add(library);
            var program = library.GetProgram(GrShaderEffectDescription.ProgramName);
            using var suspension = GraphicsDevice.SuspendCommandList();
            foreach (var texture in description.Resources.Where(resource => resource.Kind == ShaderResourceKind.Texture))
                program.Parameters[texture.Name]?.SetValue(texture.Dimension == ShaderResourceDimension.TextureCube ? BlackFallbackCube : BlackFallback);
            _shaderEffects[key] = program;
            return program;
        }
    }

    public Texture2D BlackFallback { get; }

    public TextureCube BlackFallbackCube { get; }

    private Texture2D SolidTexture(Color color, string name)
    {
        var texture = new Texture2D(GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = name });
        texture.SetData<Color>([color]);
        return texture;
    }

    private TextureCube SolidCube(Color color, string name)
    {
        var cube = new TextureCube(GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = name });
        foreach (var face in Enum.GetValues<CubeFace>())
            cube.SetData<Color>(face, [color]);
        return cube;
    }

    private ShaderProgramDescription DescribeMaterial(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment)
    {
        if (DecompiledShaderDirectory is not null || MaterialShaderCache is null)
            return GrShaderEffectDescription.Describe(vertex, fragment, DecompiledShaderReplacement(vertex, fragment));
        if (MaterialShaderCache.Stage(vertex) is { } compiledVertex && MaterialShaderCache.Stage(fragment) is { } compiledFragment)
            return GrShaderEffectDescription.DescribeDescriptorIndexed(vertex, fragment, compiledVertex, compiledFragment);
        return GrShaderEffectDescription.Describe(vertex, fragment);
    }

    private Func<Gw2BgfxShaderBinary, byte[]?>? DecompiledShaderReplacement(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment)
    {
        if (DecompiledShaderDirectory is null) return null;
        var vertexPath = DecompiledShaderPath(vertex);
        var fragmentPath = DecompiledShaderPath(fragment);
        if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
        {
            foreach (var path in new[] { vertexPath, fragmentPath }.Where(path => !File.Exists(path)))
                File.WriteAllText(Path.ChangeExtension(path, ".missing"), string.Empty);
            return null;
        }

        var bytecode = new Dictionary<Gw2BgfxShaderBinary, byte[]> { [vertex] = File.ReadAllBytes(vertexPath), [fragment] = File.ReadAllBytes(fragmentPath) };
        return shader => bytecode[shader];
    }

    private string DecompiledShaderPath(Gw2BgfxShaderBinary shader) =>
        Path.Combine(DecompiledShaderDirectory!, shader.CodeHash().ToString("X8", CultureInfo.InvariantCulture) + ".dxil");

    public Texture2D CreateRenderTarget(int width, int height, SurfaceFormat format, string name, int mipLevels = 1, bool unorderedAccess = false, Color clearColor = default) =>
        new(GraphicsDevice, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = format,
            MipLevels = mipLevels,
            Usage = TextureUsage.RenderTarget | TextureUsage.ShaderResource | (unorderedAccess ? TextureUsage.UnorderedAccess : TextureUsage.None),
            Name = name,
            ClearColor = clearColor.ToVector4(),
        });

    public Texture2D CreateDepthTarget(int width, int height, string name, SurfaceFormat format = SurfaceFormat.Depth24UnormStencil8) =>
        new(GraphicsDevice, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = format,
            Usage = TextureUsage.DepthStencil | TextureUsage.ShaderResource,
            Name = name,
        });

    public void SetRenderTarget(Texture2D color, Texture2D? depth = null)
    {
        ArgumentNullException.ThrowIfNull(color);
        GraphicsDevice.SetRenderTarget(color, depth);
        BoundTargets = new GrTargetBinding(color, depth, false);
    }

    public void SetDepthOnlyTarget(Texture2D depth)
    {
        ArgumentNullException.ThrowIfNull(depth);
        GraphicsDevice.SetRenderTargets([], depth);
        BoundTargets = new GrTargetBinding(null, depth, false);
    }

    public void SetBackBuffer()
    {
        var depth = BackBufferDepth();
        GraphicsDevice.SetRenderTarget(null, depth);
        BoundTargets = new GrTargetBinding(null, depth, true);
    }

    public void SetRenderTargets(GrTargetBinding binding)
    {
        if (binding.IsBackBuffer) SetBackBuffer();
        else if (binding.Color is null) SetDepthOnlyTarget(binding.Depth!);
        else SetRenderTarget(binding.Color, binding.Depth);
    }

    public void ApplyPipeline(ShaderProgram program, BlendState blendState, DepthStencilState depthStencilState, RasterizerState rasterizerState, VertexLayout? vertexLayout, PrimitiveType primitiveType = PrimitiveType.TriangleList) =>
        GraphicsDevice.SetPipeline(Pipeline(program, blendState, depthStencilState, rasterizerState, vertexLayout, primitiveType));

    public GraphicsPipeline Pipeline(ShaderProgram program, BlendState blendState, DepthStencilState depthStencilState, RasterizerState rasterizerState, VertexLayout? vertexLayout, PrimitiveType primitiveType = PrimitiveType.TriangleList)
    {
        var targets = BoundTargets;
        var colorFormat = targets.IsBackBuffer ? GraphicsDevice.BackBufferFormat : targets.Color?.Format;
        var depthFormat = targets.Depth?.Format;
        var key = new GrPipelineKey(program, blendState, depthStencilState, rasterizerState, vertexLayout, primitiveType, colorFormat, depthFormat);
        if (_pipelines.TryGetValue(key, out var pipeline)) return pipeline;
        lock (_pipelines)
        {
            if (_pipelines.TryGetValue(key, out pipeline)) return pipeline;
            pipeline = CreatePipeline(program, blendState, depthStencilState, rasterizerState, vertexLayout, primitiveType, colorFormat, depthFormat);
            _pipelines[key] = pipeline;
            return pipeline;
        }
    }

    private GraphicsPipeline CreatePipeline(ShaderProgram program, BlendState blendState, DepthStencilState depthStencilState, RasterizerState rasterizerState, VertexLayout? vertexLayout, PrimitiveType primitiveType, SurfaceFormat? colorFormat, SurfaceFormat? depthFormat) =>
        new GraphicsPipeline(GraphicsDevice, new GraphicsPipelineDescription
        {
            Program = program,
            BlendState = blendState,
            DepthStencilState = depthStencilState,
            RasterizerState = rasterizerState,
            VertexLayout = vertexLayout,
            PrimitiveType = primitiveType,
            RenderTargetFormats = colorFormat is { } format ? [format] : [],
            DepthStencilFormat = depthFormat,
            Name = $"{program.Library.Name}.{program.Name}",
        });

    public SamplerState Sampler(in SamplerDescription description) => GraphicsDevice.GetSamplerState(description);

    private Texture2D BackBufferDepth()
    {
        var width = GraphicsDevice.BackBufferWidth;
        var height = GraphicsDevice.BackBufferHeight;
        lock (_backBufferDepthLock)
        {
            if (_backBufferDepth is { } depth && depth.Width == width && depth.Height == height) return depth;
            _backBufferDepth?.Dispose();
            _backBufferDepth = CreateDepthTarget(width, height, "BackBufferDepth");
            return _backBufferDepth;
        }
    }

    public void Dispose()
    {
        foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
        _pipelines.Clear();
        foreach (var library in _shaderLibraries) library.Dispose();
        _shaderLibraries.Clear();
        _backBufferDepth?.Dispose();
        _backBufferDepth = null;
        BlackFallback.Dispose();
        BlackFallbackCube.Dispose();
        _shaderEffects.Clear();
        MaterialShaderCache?.Dispose();
    }

    private readonly record struct GrPipelineKey(
        ShaderProgram Program,
        BlendState BlendState,
        DepthStencilState DepthStencilState,
        RasterizerState RasterizerState,
        VertexLayout? VertexLayout,
        PrimitiveType PrimitiveType,
        SurfaceFormat? ColorFormat,
        SurfaceFormat? DepthFormat);
}

public readonly record struct GrTargetBinding(Texture2D? Color, Texture2D? Depth, bool IsBackBuffer);

public readonly ref struct GrCommandListBinding
{
    private readonly GraphicsCommandListBinding _binding;
    private readonly GrTargetBinding? _previousTargets;

    internal GrCommandListBinding(GraphicsCommandListBinding binding, GrTargetBinding? previousTargets)
    {
        _binding = binding;
        _previousTargets = previousTargets;
    }

    public void Dispose()
    {
        _binding.Dispose();
        GrDevice.EndRecording(_previousTargets);
    }
}
