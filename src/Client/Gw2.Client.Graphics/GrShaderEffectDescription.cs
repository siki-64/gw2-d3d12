using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed record GrShaderTextureNames(IReadOnlyDictionary<int, string> Vertex, IReadOnlyDictionary<int, string> Fragment)
{
    public string Of(bool isVertexStage, int register) => (isVertexStage ? Vertex : Fragment)[register];

    public static string SamplerOf(string textureName) => textureName + "Sampler";
}

public static class GrShaderEffectDescription
{
    public const string ProgramName = "Material";
    private const int BytesPerRegister = 16;
    private const int FloatsPerRegister = 4;

    public static uint EffectKeyOf(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var stage in new[] { vertex, fragment })
        {
            foreach (var value in stage.Code.Span)
                hash = unchecked((hash ^ value) * prime);
            foreach (var uniform in stage.Uniforms)
                foreach (var value in Encoding.UTF8.GetBytes($"{uniform.Name}:{uniform.Kind}:{uniform.Register}:{uniform.Count};"))
                    hash = unchecked((hash ^ value) * prime);
        }

        return hash;
    }

    public static GrShaderTextureNames TextureNamesOf(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment)
    {
        ArgumentNullException.ThrowIfNull(vertex);
        ArgumentNullException.ThrowIfNull(fragment);
        var fragmentDeclarations = ProgramOf(fragment, "Fragment").ResourceDeclarations();
        var fragmentNames = fragmentDeclarations.ToDictionary(resource => resource.Register, resource => BgfxTextureNameOf(fragment, resource.Register) ?? $"FragmentTexture{resource.Register}");
        var fragmentByName = fragmentDeclarations.ToDictionary(resource => fragmentNames[resource.Register]);
        var vertexNames = ProgramOf(vertex, "Vertex").ResourceDeclarations().ToDictionary(resource => resource.Register, resource =>
        {
            var name = BgfxTextureNameOf(vertex, resource.Register) ?? $"VertexTexture{resource.Register}";
            return !fragmentByName.TryGetValue(name, out var shared) || shared == resource ? name : "Vertex" + name;
        });
        return new(vertexNames, fragmentNames);
    }

    public static ShaderProgramDescription Describe(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment, Func<Gw2BgfxShaderBinary, byte[]?>? compiledReplacement = null)
    {
        ArgumentNullException.ThrowIfNull(vertex);
        ArgumentNullException.ThrowIfNull(fragment);
        if (vertex.Stage != Gw2BgfxShaderStage.Vertex) throw new ArgumentException("Not a vertex shader.", nameof(vertex));
        if (fragment.Stage != Gw2BgfxShaderStage.Fragment) throw new ArgumentException("Not a fragment shader.", nameof(fragment));

        var textureNames = TextureNamesOf(vertex, fragment);
        var constantBuffers = new List<ShaderConstantBufferDescription>();
        var resources = new Dictionary<string, ShaderResourceDescription>();
        DescribeStage(vertex, "Vertex", ShaderStages.Vertex, textureNames.Vertex, constantBuffers, resources);
        DescribeStage(fragment, "Fragment", ShaderStages.Pixel, textureNames.Fragment, constantBuffers, resources);
        return new ShaderProgramDescription
        {
            Name = ProgramName,
            VertexShader = BytecodeOf(vertex, compiledReplacement),
            PixelShader = BytecodeOf(fragment, compiledReplacement),
            ConstantBuffers = constantBuffers,
            Resources = [.. resources.Values],
            VertexInputs = VertexInputsOf(vertex),
        };
    }

    public static ShaderProgramDescription DescribeDescriptorIndexed(Gw2BgfxShaderBinary vertex, Gw2BgfxShaderBinary fragment, GrCompiledMaterialStage compiledVertex, GrCompiledMaterialStage compiledFragment)
    {
        ArgumentNullException.ThrowIfNull(compiledVertex);
        ArgumentNullException.ThrowIfNull(compiledFragment);
        var description = Describe(vertex, fragment);
        return description with
        {
            VertexShader = compiledVertex.Bytecode,
            PixelShader = compiledFragment.Bytecode,
            UsesDescriptorIndices = true,
            Resources = [.. description.Resources.Select(resource => resource with
            {
                VertexDescriptorSlot = (resource.Stages & ShaderStages.Vertex) != 0 ? SlotOf(compiledVertex, resource) : -1,
                PixelDescriptorSlot = (resource.Stages & ShaderStages.Pixel) != 0 ? SlotOf(compiledFragment, resource) : -1,
            })],
        };
    }

    private static int SlotOf(GrCompiledMaterialStage stage, ShaderResourceDescription resource)
    {
        var registerClass = resource.Kind == ShaderResourceKind.Sampler ? 's' : 't';
        foreach (var slot in stage.DescriptorSlots)
            if (slot.RegisterClass == registerClass && slot.Register == resource.Register && slot.Space == resource.Space) return slot.Slot;
        return -1;
    }

    private static void DescribeStage(Gw2BgfxShaderBinary shader, string stageName, ShaderStages stage, IReadOnlyDictionary<int, string> textureNames, List<ShaderConstantBufferDescription> constantBuffers, Dictionary<string, ShaderResourceDescription> resources)
    {
        var program = ProgramOf(shader, stageName);
        if (shader.ConstantBufferSize > 0)
        {
            var register = program.ConstantBufferDeclarations().FirstOrDefault()?.Register ?? 0;
            var variables = shader.Uniforms
                .Where(uniform => uniform.Kind == Gw2ShaderReflectionKind.Constant)
                .Select(uniform => ConstantVariable(uniform.Name, uniform.Register, Math.Max(1, uniform.Count)))
                .ToList();
            constantBuffers.Add(new($"{stageName}Constants", register, shader.ConstantBufferSize, stage, variables));
        }

        foreach (var resource in program.ResourceDeclarations())
            AddResource(resources, new(textureNames[resource.Register], ShaderResourceKind.Texture, DimensionOf(resource.Dimension), resource.Register, stage));
        foreach (var register in program.SamplerDeclarations())
        {
            var textureName = textureNames.TryGetValue(register, out var name) ? name : $"{stageName}Texture{register}";
            AddResource(resources, new(GrShaderTextureNames.SamplerOf(textureName), ShaderResourceKind.Sampler, ShaderResourceDimension.None, register, stage));
        }
    }

    private static void AddResource(Dictionary<string, ShaderResourceDescription> resources, ShaderResourceDescription resource)
    {
        if (!resources.TryGetValue(resource.Name, out var existing))
        {
            resources.Add(resource.Name, resource);
            return;
        }

        if (existing.Kind != resource.Kind || existing.Dimension != resource.Dimension || existing.Register != resource.Register)
            throw new InvalidDataException($"Resource {resource.Name} differs between the vertex and fragment shader.");
        resources[resource.Name] = existing with { Stages = existing.Stages | resource.Stages };
    }

    private static ShaderVariableDescription ConstantVariable(string name, int offset, int registers) =>
        new(name, offset, registers * BytesPerRegister, ShaderVariableClass.Vector, 1, FloatsPerRegister, registers == 1 ? 0 : registers);

    private static string? BgfxTextureNameOf(Gw2BgfxShaderBinary shader, int register) =>
        shader.Uniforms.FirstOrDefault(uniform => uniform.Kind == Gw2ShaderReflectionKind.Sampler && uniform.Register == register)?.Name;

    private static Gw2ShaderProgram ProgramOf(Gw2BgfxShaderBinary shader, string stageName)
    {
        var container = shader.Container ?? throw new InvalidDataException($"{stageName} shader has no DXBC container.");
        if (!container.TryGetChunk("SHEX", out var programChunk) || Gw2ShaderProgram.TryParse(programChunk.Data) is not { } program)
            throw new InvalidDataException($"{stageName} shader has no SHEX program.");
        return program;
    }

    private static ShaderResourceDimension DimensionOf(Gw2ShaderResourceDimension dimension) => dimension switch
    {
        Gw2ShaderResourceDimension.Buffer => ShaderResourceDimension.Buffer,
        Gw2ShaderResourceDimension.Texture1D => ShaderResourceDimension.Texture1D,
        Gw2ShaderResourceDimension.Texture1DArray => ShaderResourceDimension.Texture1DArray,
        Gw2ShaderResourceDimension.Texture2DArray => ShaderResourceDimension.Texture2DArray,
        Gw2ShaderResourceDimension.Texture2DMultisampled => ShaderResourceDimension.Texture2DMS,
        Gw2ShaderResourceDimension.Texture2DMultisampledArray => ShaderResourceDimension.Texture2DMSArray,
        Gw2ShaderResourceDimension.Texture3D => ShaderResourceDimension.Texture3D,
        Gw2ShaderResourceDimension.TextureCube => ShaderResourceDimension.TextureCube,
        Gw2ShaderResourceDimension.TextureCubeArray => ShaderResourceDimension.TextureCubeArray,
        _ => ShaderResourceDimension.Texture2D,
    };

    private static ShaderVertexInputDescription[] VertexInputsOf(Gw2BgfxShaderBinary vertex)
    {
        if (vertex.Container is not { } container || !container.TryGetChunk("ISGN", out var chunk) || Gw2DxbcSignature.TryParse(chunk.Data) is not { } inputs)
            throw new InvalidDataException("Vertex shader has no input signature.");
        return inputs
            .Where(input => input.SystemValue == 0)
            .Select(input => new ShaderVertexInputDescription(input.SemanticName, input.SemanticIndex, input.Register))
            .ToArray();
    }

    private static byte[] BytecodeOf(Gw2BgfxShaderBinary shader, Func<Gw2BgfxShaderBinary, byte[]?>? compiledReplacement) =>
        compiledReplacement?.Invoke(shader) ?? shader.ChecksummedCode() ?? throw new InvalidDataException($"{shader.Stage} shader code does not checksum.");
}
