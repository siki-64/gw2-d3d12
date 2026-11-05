using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Dat.Content;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed record GrArchiveUniform(uint Token, GrDrawConstantId Id, ShaderParameter Parameter);

public sealed class GrArchiveShaderMaterial
{
    public const uint StaticVertexShaderVariant = 0;
    public const uint InstancedVertexShaderVariant = 3;

    private GrArchiveShaderMaterial(ShaderProgram program, IReadOnlyList<GrArchiveSampler> samplers, Gw2AmatRenderState renderState, Gw2AmatCull cull, uint shaderPassFlags, int passIndex, ulong effectKey, IReadOnlyDictionary<string, uint> constantTokenByName)
    {
        Uniforms = program.Parameters
            .Where(parameter => parameter.Kind == ShaderParameterKind.Value && parameter.Rows == 1)
            .Select(parameter => (Token: constantTokenByName.TryGetValue(parameter.Name, out var token) ? token : Token.Encode(parameter.Name), Parameter: parameter))
            .Select(uniform => new GrArchiveUniform(uniform.Token, GrDrawConstantIds.Of(uniform.Token), uniform.Parameter))
            .ToList();
        Program = program;
        Samplers = samplers;
        RenderState = renderState;
        Cull = cull;
        ShaderPassFlags = shaderPassFlags;
        PassIndex = passIndex;
        EffectKey = effectKey;
        CreationOrder = System.Threading.Interlocked.Increment(ref _createdCount);
    }

    public ShaderProgram Program { get; }

    public IReadOnlyList<GrArchiveSampler> Samplers { get; }

    public Gw2AmatRenderState RenderState { get; }

    public Gw2AmatCull Cull { get; }

    public Gw2AmatDrawState DrawState => DrawStateFor();

    public uint DrawFlags { get; private set; }

    public uint StageFlags { get; private set; }

    public uint ShaderPassFlags { get; }

    public int PassIndex { get; }

    public ulong EffectKey { get; }

    private static int _createdCount;

    public int CreationOrder { get; }

    public Gw2AmatDrawState DrawStateFor(bool viewDepthTest = true, uint stageFlags = 0, float viewDepthBias = 0f) =>
        Gw2AmatDrawState.Of(ShaderPassFlags, PassIndex, EffectKey, DrawFlags, viewDepthBias, viewDepthTest, StageFlags | stageFlags);

    public IReadOnlyList<GrArchiveUniform> Uniforms { get; }

    public IReadOnlyList<GrArchiveShaderMaterial> LaterPasses { get; private set; } = [];

    public int SortBucket { get; private set; } = Gw2SortBuckets.Default;

    public int SortBucketOverride { get; private set; }

    public static GrArchiveShaderMaterial? Create(GrDevice device, Gw2AmatMaterial material, ulong effectKey = Gw2AmatEffectKeys.Default, uint vertexShaderVariant = StaticVertexShaderVariant, Gw2ShaderQuality shaderQuality = Gw2ShaderQuality.High, uint drawFlags = 0, int sortBucketOverride = 0)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(material);
        var techniqueIndex = material.TechniqueIndexFor(shaderQuality);
        if (CreatePass(device, material, techniqueIndex, 0, effectKey, vertexShaderVariant) is not { } first) return null;
        var laterPasses = new List<GrArchiveShaderMaterial>();
        for (var passIndex = 1; passIndex < material.Techniques[techniqueIndex].Passes.Count; passIndex++)
        {
            if (CreatePass(device, material, techniqueIndex, passIndex, effectKey, vertexShaderVariant) is not { } pass) break;
            laterPasses.Add(pass);
        }

        drawFlags |= material.GrDrawFlags;
        var stageFlags = Gw2DrawStageFlags.Of(material.GrFlags, drawFlags);
        foreach (var pass in laterPasses.Prepend(first)) (pass.DrawFlags, pass.StageFlags) = (drawFlags, stageFlags);
        first.LaterPasses = laterPasses;
        first.SortBucketOverride = sortBucketOverride;
        first.SortBucket = sortBucketOverride != 0 ? sortBucketOverride : material.SortBucket;
        return first;
    }

    public static GrArchiveShaderMaterial FromBuiltIn(GrDevice device, Gw2BuiltInWaterMaterial material)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(material);
        var samplers = material.Samplers.Select(sampler => new GrArchiveSampler(sampler.Name, (int)sampler.TextureSlot, sampler.IsEngineTexture, (int)sampler.ResourceIndex, sampler.State)).ToList();
        return new(device.LoadShader("GrBuiltInWater").GetProgram(material.Technique), samplers, new Gw2AmatRenderState(material.RenderState), Gw2AmatEffectCull.Of(material.ShaderPassFlags), material.ShaderPassFlags, 0, Gw2AmatEffectKeys.Default, Gw2BuiltInWaterMaterial.ConstantTokens);
    }

    private static GrArchiveShaderMaterial? CreatePass(GrDevice device, Gw2AmatMaterial material, int techniqueIndex, int passIndex, ulong effectKey, uint vertexShaderVariant)
    {
        if (!material.TryResolveEffect(techniqueIndex, passIndex, effectKey, out var selection)) return null;
        var variant = selection.Effect.VertexShaderVariantFor(vertexShaderVariant);
        if (variant is null) return null;

        var fragmentBinary = Dx11BinaryOf(material, selection.PixelShaderIndex);
        var vertexBinary = Dx11BinaryOf(material, checked((int)variant.VertexShaderIndex));
        if (fragmentBinary is null || vertexBinary is null) return null;
        if (Gw2BgfxShaderBinary.TryParse(vertexBinary.Data) is not { } vertex || Gw2BgfxShaderBinary.TryParse(fragmentBinary.Data) is not { } fragment) return null;

        var textureNames = GrShaderEffectDescription.TextureNamesOf(vertex, fragment);
        var samplerNames = textureNames.Fragment;
        var vertexSamplerNames = textureNames.Vertex;
        var cubeTextureNames = GrShaderEffectDescription.Describe(vertex, fragment).Resources
            .Where(resource => resource.Kind == ShaderResourceKind.Texture && resource.Dimension == ShaderResourceDimension.TextureCube)
            .Select(resource => resource.Name)
            .ToHashSet();
        var samplers = fragmentBinary.Samplers
            .Where(binding => samplerNames.ContainsKey((int)binding.TextureSlot))
            .Select(binding => new GrArchiveSampler(samplerNames[(int)binding.TextureSlot], (int)binding.TextureSlot, binding.IsEngineTexture, (int)binding.ResourceIndex, material.SamplerStateOf(binding), IsCube: cubeTextureNames.Contains(samplerNames[(int)binding.TextureSlot])))
            .Concat(vertexBinary.Samplers
                .Where(binding => binding.IsVertexTexture && vertexSamplerNames.ContainsKey((int)binding.TextureSlot))
                .Select(binding => new GrArchiveSampler(vertexSamplerNames[(int)binding.TextureSlot], (int)binding.TextureSlot, binding.IsVertexEngineTexture, binding.IsVertexEngineTexture ? (int)binding.ResourceIndex : (int)binding.TextureSlot, material.SamplerStateOf(binding), IsVertexStage: true)))
            .ToList();
        var constantTokenByName = new Dictionary<string, uint>(vertexBinary.ConstantTokensByName());
        foreach (var (name, token) in fragmentBinary.ConstantTokensByName()) constantTokenByName.TryAdd(name, token);
        return new(device.ShaderEffect(vertex, fragment), samplers, new Gw2AmatRenderState(selection.Effect.RenderState), Gw2AmatEffectCull.Of(selection.Effect.ShaderPassFlags), selection.Effect.ShaderPassFlags, selection.PassIndex, effectKey, constantTokenByName);
    }

    private static Gw2AmatShaderBinary? Dx11BinaryOf(Gw2AmatMaterial material, int shaderIndex) =>
        shaderIndex < 0 || shaderIndex >= material.Shaders.Count
            ? null
            : material.Shaders[shaderIndex].Binaries.FirstOrDefault(binary => binary.Backend == Gw2AmatShaderBackend.Dx11 && binary.Data.Length > 0);
}
