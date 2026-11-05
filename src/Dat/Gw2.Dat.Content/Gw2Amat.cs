using Gw2.Formats;
namespace Gw2.Dat.Content;

public enum Gw2AmatShaderBackend
{
    Dx11,
    Osx,
    Pbr,
}

public sealed record Gw2AmatSamplerBinding(ulong Token, uint StateIndex, uint TextureIndex, uint TextureSlot)
{
    private const uint EngineTextureFlag = 0x20;
    private const uint VertexTextureFlag = 0x10;
    private const uint VertexTextureArrayFlag = 0x40;
    private const uint TextureFlags = EngineTextureFlag | VertexTextureFlag | VertexTextureArrayFlag;

    public bool IsEngineTexture => TextureIndex != uint.MaxValue && (TextureIndex & EngineTextureFlag) != 0;

    public bool IsVertexTexture => TextureIndex != uint.MaxValue && (TextureIndex & (VertexTextureFlag | VertexTextureArrayFlag)) != 0;

    public bool IsVertexEngineTexture => TextureIndex != uint.MaxValue && (TextureIndex & VertexTextureFlag) != 0;

    public uint ResourceIndex => TextureIndex == uint.MaxValue ? uint.MaxValue : TextureIndex & ~TextureFlags;
}

public sealed record Gw2AmatShaderConstant(uint Token);

public sealed record Gw2AmatShaderBinding(ulong Token, byte Type, byte Access, byte Byte);

public sealed record Gw2AmatShaderBinary(
    Gw2AmatShaderBackend Backend,
    ReadOnlyMemory<byte> Data,
    IReadOnlyList<Gw2AmatShaderConstant> Constants,
    IReadOnlyList<Gw2AmatSamplerBinding> Samplers,
    IReadOnlyList<Gw2AmatShaderBinding> Bindings,
    Gw2ShaderReflection? Reflection = null)
{
    public IReadOnlyDictionary<string, uint> ConstantTokensByName()
    {
        var tokens = new Dictionary<string, uint>();
        if (Reflection is null) return tokens;
        var names = Reflection.Entries.Where(entry => entry.Kind != Gw2ShaderReflectionKind.Sampler).Select(entry => entry.Name).ToList();
        if (names.Count != Constants.Count) return tokens;
        for (var index = 0; index < names.Count; index++) tokens.TryAdd(names[index], Constants[index].Token);
        return tokens;
    }
}

public sealed record Gw2AmatShader(int ShaderType, IReadOnlyList<Gw2AmatShaderBinary> Binaries)
{
    public bool IsPixelShader => ShaderType == 16;
}

public sealed record Gw2AmatVertexShaderVariant(uint Variant, uint VertexShaderIndex);

public sealed record Gw2AmatEffect(
    ulong Token,
    ulong RenderState,
    uint ShaderPassFlags,
    uint PixelShaderIndex,
    IReadOnlyList<Gw2AmatVertexShaderVariant> VertexShaderVariants)
{
    public const uint StaticVertexShaderVariant = 0;
    public const uint Flag80VertexShaderVariant = 2;
    public const uint InstancedFlag80VertexShaderVariant = 4;

    public Gw2AmatVertexShaderVariant? VertexShaderVariantFor(uint requested)
    {
        while (true)
        {
            if (VertexShaderVariants.FirstOrDefault(candidate => candidate.Variant == requested) is { } variant) return variant;
            if (requested == StaticVertexShaderVariant) return null;
            requested = requested == InstancedFlag80VertexShaderVariant ? Flag80VertexShaderVariant : StaticVertexShaderVariant;
        }
    }
}

public sealed record Gw2AmatPass(IReadOnlyList<Gw2AmatEffect> Effects);

public sealed record Gw2AmatTechnique(uint Quality, IReadOnlyList<Gw2AmatPass> Passes)
{
    public string QualityName => Gw2TechniqueQuality.DecodeName(Quality);

    public Gw2ShaderQuality ShaderQuality => Gw2TechniqueQuality.QualityOf(Quality);
}

public sealed record Gw2AmatEffectUsage(
    int TechniqueIndex,
    int PassIndex,
    int EffectIndex,
    ulong EffectToken,
    int PixelShaderIndex,
    IReadOnlyList<int> OutputResources);

public sealed record Gw2AmatEffectSelection(
    int TechniqueIndex,
    int PassIndex,
    int EffectIndex,
    Gw2AmatEffect Effect)
{
    public int PixelShaderIndex => checked((int)Effect.PixelShaderIndex);
}

public static class Gw2AmatEffectKeys
{
    public const ulong Default = 0x0000_0000_0002_CC22;
    public const ulong NormalSpecDepth = 0x0000_6584_D816_C9EE;
    public const ulong Fading = 0x0051_C590_6137_0654;
    public const ulong ShadowDepth = 0x005D_E40A_268A_40A4;
    public const ulong Reflection = 0x0914_C6A8_A883_B1EE;
    public const ulong SkyCardAdditive = 4225;

    public const ulong FadingAlias = 0x0000_0001_4813_11EC;
    public const ulong ShadowDepthAlias = 0x0000_1779_0289_91EC;
    public const ulong NormalSpecDepthAlias = 0x02C2_6C0B_64F7_11EC;
    public const ulong DepthAlias = 0x0000_0004_8A63_5447;
    public const ulong NormalSpecDepthAliasChain = 0x0002_4531_AA23_91EC;

    public static IReadOnlyList<ulong> FallbacksOf(ulong requested) => requested switch
    {
        FadingAlias => [Fading],
        ShadowDepthAlias => [ShadowDepth],
        NormalSpecDepthAlias => [NormalSpecDepth],
        DepthAlias => [NormalSpecDepth],
        NormalSpecDepthAliasChain => [NormalSpecDepthAlias, DepthAlias, NormalSpecDepth],
        _ => [],
    };
}

public sealed class Gw2AmatMaterial
{
    public Gw2AmatMaterial(
        IReadOnlyList<Gw2AmatShader> shaders,
        IReadOnlyList<uint> samplerStates,
        IReadOnlyList<Gw2AmatTechnique> techniques,
        IReadOnlyList<ulong>? textureTokens = null)
    {
        Shaders = shaders;
        SamplerStates = samplerStates;
        Techniques = techniques;
        TextureTokens = textureTokens ?? [];
    }

    public IReadOnlyList<Gw2AmatShader> Shaders { get; }
    public IReadOnlyList<uint> SamplerStates { get; }

    public Gw2AmatSamplerState SamplerStateOf(Gw2AmatSamplerBinding binding) =>
        binding.StateIndex < SamplerStates.Count ? new Gw2AmatSamplerState(SamplerStates[(int)binding.StateIndex]) : default;
    public IReadOnlyList<Gw2AmatTechnique> Techniques { get; }

    public IReadOnlyList<ulong> TextureTokens { get; }

    public byte SortOrder { get; init; }

    public uint GrFlags { get; init; }

    public uint GrDrawFlags { get; init; }

    public int SortBucket => Gw2SortBuckets.Of(SortOrder);

    public bool HasContent => Shaders.Count != 0 || Techniques.Count != 0;

    public Gw2AmatTechnique? HighestQualityTechnique
    {
        get
        {
            Gw2AmatTechnique? best = null;
            foreach (var technique in Techniques)
                if (technique.ShaderQuality != Gw2ShaderQuality.Invalid &&
                    (best is null || technique.ShaderQuality > best.ShaderQuality))
                    best = technique;
            return best;
        }
    }

    public int TechniqueIndexFor(Gw2ShaderQuality quality)
    {
        var fallback = -1;
        for (var index = 0; index < Techniques.Count; index++)
        {
            var techniqueQuality = Gw2TechniqueQuality.SelectionQualityOf(Techniques[index].Quality);
            if (techniqueQuality == Gw2ShaderQuality.Invalid || techniqueQuality <= quality) return index;
            fallback = index;
        }

        return fallback;
    }

    public Gw2AmatTechnique? TechniqueFor(Gw2ShaderQuality quality) =>
        TechniqueIndexFor(quality) is var index and >= 0 ? Techniques[index] : null;

    public IReadOnlyList<Gw2AmatSamplerBinding> EnumerateTextureBindings()
    {
        var seen = new HashSet<ulong>();
        var bindings = new List<Gw2AmatSamplerBinding>();
        foreach (var shader in Shaders)
            foreach (var binary in shader.Binaries)
                foreach (var sampler in binary.Samplers)
                    if (seen.Add(sampler.Token))
                        bindings.Add(sampler);
        return bindings;
    }

    public bool TryGetTextureBinding(ulong textureToken, out Gw2AmatSamplerBinding binding)
    {
        foreach (var candidate in EnumerateTextureBindings())
        {
            if (candidate.Token == textureToken)
            {
                binding = candidate;
                return true;
            }
        }

        binding = null!;
        return false;
    }

    public bool TryGetTextureSlot(ulong textureToken, out uint textureSlot)
    {
        if (TryGetTextureBinding(textureToken, out var binding))
        {
            textureSlot = binding.TextureSlot;
            return true;
        }

        textureSlot = 0;
        return false;
    }

    public bool TryGetMaterialSampler(int materialTextureIndex, out Gw2AmatSamplerBinding binding)
    {
        var binary = FindMaterialBinary(materialTextureIndex);
        if (binary is not null)
        {
            binding = binary.Samplers[materialTextureIndex];
            return true;
        }

        binding = null!;
        return false;
    }

    private Gw2AmatShaderBinary? FindMaterialBinary(int materialTextureIndex)
    {
        if (materialTextureIndex < 0) return null;

        var target = TextureTokens.Count;
        Gw2AmatShader? shader = null;
        var canonical = CanonicalPixelShader();
        if (target > 0)
        {
            if (canonical is not null && SamplerCount(canonical) == target)
                shader = canonical;
            else
                foreach (var candidate in Shaders)
                    if (candidate.IsPixelShader && SamplerCount(candidate) == target && candidate.Binaries.Any(binary => binary.Samplers.Count > 0))
                    {
                        shader = candidate;
                        break;
                    }
        }

        shader ??= canonical;
        if (shader is null) return null;
        foreach (var binary in shader.Binaries)
            if (materialTextureIndex < binary.Samplers.Count)
                return binary;
        return null;
    }

    private static int SamplerCount(Gw2AmatShader shader)
    {
        foreach (var binary in shader.Binaries)
            if (binary.Samplers.Count > 0)
                return binary.Samplers.Count;
        return 0;
    }

    private Gw2AmatShader? CanonicalPixelShader()
    {
        foreach (var technique in Techniques)
            foreach (var pass in technique.Passes)
                foreach (var effect in pass.Effects)
                {
                    var index = checked((int)effect.PixelShaderIndex);
                    if (index >= 0 && index < Shaders.Count && Shaders[index].IsPixelShader)
                        return Shaders[index];
                }

        foreach (var shader in Shaders)
            if (shader.IsPixelShader)
                return shader;
        return null;
    }

    public IReadOnlyList<int> MaterialResources(int materialTextureIndex)
    {
        var binary = FindMaterialBinary(materialTextureIndex);
        return binary is null ? [] : SampledResources(binary, materialTextureIndex);
    }

    public IReadOnlyList<int> MaterialResources(int materialTextureIndex, int pixelShaderIndex)
    {
        if (pixelShaderIndex < 0 || pixelShaderIndex >= Shaders.Count) return [];
        foreach (var binary in Shaders[pixelShaderIndex].Binaries)
            if (binary.Samplers.Count > 0)
                return SampledResources(binary, materialTextureIndex);
        return [];
    }

    private static IReadOnlyList<int> SampledResources(Gw2AmatShaderBinary binary, int materialTextureIndex)
    {
        var resources = new List<int>();
        if (materialTextureIndex < 0 || materialTextureIndex >= binary.Samplers.Count) return resources;

        var program = TryGetProgram(binary);
        if (program is null) return resources;

        var samplerSlot = (int)binary.Samplers[materialTextureIndex].TextureSlot;
        foreach (var instruction in program.Instructions)
        {
            if (!Gw2ShaderProgram.IsTextureRead(instruction.OpCode)) continue;
            if (instruction.Operand(Gw2ShaderRegisterType.Sampler) is { } sampler && sampler.RegisterIndex == samplerSlot
                && instruction.Operand(Gw2ShaderRegisterType.Resource) is { } texture)
                resources.Add(texture.RegisterIndex);
        }

        return resources.Distinct().OrderBy(value => value).ToList();
    }

    public Gw2ShaderResourceRole MaterialRole(int materialTextureIndex)
    {
        var roles = ResourceRoles();
        var merged = Gw2ShaderResourceRole.Unknown;
        foreach (var resource in MaterialResources(materialTextureIndex))
            if (roles.TryGetValue(resource, out var role))
                merged = Merge(merged, role);
        return merged;
    }

    public Gw2ShaderResourceRole MaterialRole(int materialTextureIndex, int pixelShaderIndex)
    {
        var roles = ResourceRoles(pixelShaderIndex);
        var merged = Gw2ShaderResourceRole.Unknown;
        foreach (var resource in MaterialResources(materialTextureIndex, pixelShaderIndex))
            if (roles.TryGetValue(resource, out var role))
                merged = Merge(merged, role);
        return merged;
    }

    public IReadOnlyList<int> MaterialResources(ulong textureToken, int pixelShaderIndex)
    {
        var textureIndex = TextureTokenIndex(textureToken);
        if (textureIndex < 0 || pixelShaderIndex < 0 || pixelShaderIndex >= Shaders.Count) return [];
        foreach (var binary in Shaders[pixelShaderIndex].Binaries)
            if (binary.Samplers.Count > 0)
                return SampledResourcesBySlot(binary, FindSamplerSlot(binary, textureIndex));
        return [];
    }

    public IReadOnlyList<int> MaterialResources(ulong textureToken)
    {
        var textureIndex = TextureTokenIndex(textureToken);
        if (textureIndex < 0) return [];
        var resources = new HashSet<int>();
        var shaders = new HashSet<int>();
        foreach (var technique in Techniques)
        foreach (var pass in technique.Passes)
        foreach (var effect in pass.Effects)
        {
            var pixelShaderIndex = checked((int)effect.PixelShaderIndex);
            if (!shaders.Add(pixelShaderIndex)) continue;
            foreach (var resource in MaterialResources(textureToken, pixelShaderIndex)) resources.Add(resource);
        }

        return resources.OrderBy(value => value).ToList();
    }

    public Gw2ShaderResourceRole MaterialRole(ulong textureToken, int pixelShaderIndex)
    {
        var roles = ResourceRoles(pixelShaderIndex);
        var merged = Gw2ShaderResourceRole.Unknown;
        foreach (var resource in MaterialResources(textureToken, pixelShaderIndex))
            if (roles.TryGetValue(resource, out var role))
                merged = Merge(merged, role);
        return merged;
    }

    public Gw2ShaderResourceRole MaterialRole(ulong textureToken)
    {
        var roles = ResourceRoles();
        var merged = Gw2ShaderResourceRole.Unknown;
        foreach (var resource in MaterialResources(textureToken))
            if (roles.TryGetValue(resource, out var role))
                merged = Merge(merged, role);
        return merged;
    }

    public bool TryGetTextureSampler(ulong textureToken, out Gw2AmatSamplerBinding binding)
    {
        var textureIndex = TextureTokenIndex(textureToken);
        if (textureIndex >= 0)
            foreach (var shader in Shaders)
            {
                if (!shader.IsPixelShader) continue;
                foreach (var binary in shader.Binaries)
                    foreach (var sampler in binary.Samplers)
                        if ((int)sampler.TextureIndex == textureIndex)
                        {
                            binding = sampler;
                            return true;
                        }
            }

        binding = null!;
        return false;
    }

    public int TextureTokenIndex(ulong textureToken)
    {
        for (var i = 0; i < TextureTokens.Count; i++)
            if (TextureTokens[i] == textureToken)
                return i;
        return -1;
    }

    private static int FindSamplerSlot(Gw2AmatShaderBinary binary, int textureIndex)
    {
        foreach (var sampler in binary.Samplers)
            if ((int)sampler.TextureIndex == textureIndex)
                return (int)sampler.TextureSlot;
        return -1;
    }

    private static IReadOnlyList<int> SampledResourcesBySlot(Gw2AmatShaderBinary binary, int samplerSlot)
    {
        var resources = new List<int>();
        if (samplerSlot < 0) return resources;
        var program = TryGetProgram(binary);
        if (program is null) return resources;

        foreach (var instruction in program.Instructions)
        {
            if (!Gw2ShaderProgram.IsTextureRead(instruction.OpCode)) continue;
            if (instruction.Operand(Gw2ShaderRegisterType.Sampler) is { } sampler && sampler.RegisterIndex == samplerSlot
                && instruction.Operand(Gw2ShaderRegisterType.Resource) is { } texture)
                resources.Add(texture.RegisterIndex);
        }

        return resources.Distinct().OrderBy(value => value).ToList();
    }

    public bool TryGetSamplerName(int materialTextureIndex, out string name)
    {
        if (TryGetMaterialSampler(materialTextureIndex, out var binding))
            return TryGetSamplerName(binding.TextureSlot, out name);

        name = string.Empty;
        return false;
    }

    public bool TryGetSamplerName(ulong textureToken, out string name)
    {
        if (TryGetTextureSampler(textureToken, out var binding))
            return TryGetSamplerName(binding.TextureSlot, out name);

        name = string.Empty;
        return false;
    }

    private bool TryGetSamplerName(uint samplerSlot, out string name)
    {
        foreach (var shader in Shaders)
        {
            if (!shader.IsPixelShader) continue;
            foreach (var binary in shader.Binaries)
                if (binary.Reflection is { } reflection && reflection.TryGetSampler((int)samplerSlot, out var entry))
                {
                    name = entry.Name;
                    return true;
                }
        }

        name = string.Empty;
        return false;
    }

    public IReadOnlyList<Gw2AmatEffectUsage> AnalyzeEffects()
    {
        var programs = new Dictionary<int, Gw2ShaderProgram?>();
        var result = new List<Gw2AmatEffectUsage>();
        for (var t = 0; t < Techniques.Count; t++)
            for (var p = 0; p < Techniques[t].Passes.Count; p++)
                for (var e = 0; e < Techniques[t].Passes[p].Effects.Count; e++)
                {
                    var effect = Techniques[t].Passes[p].Effects[e];
                    var pixelShaderIndex = checked((int)effect.PixelShaderIndex);
                    if (pixelShaderIndex < 0 || pixelShaderIndex >= Shaders.Count)
                    {
                        result.Add(new(t, p, e, effect.Token, pixelShaderIndex, []));
                        continue;
                    }

                    if (!programs.TryGetValue(pixelShaderIndex, out var program))
                        programs[pixelShaderIndex] = program = TryGetProgram(Shaders[pixelShaderIndex]);

                    var resources = new HashSet<int>();
                    if (program is not null)
                        foreach (var usage in Gw2ShaderUsage.ResourcesByOutput(program))
                            resources.UnionWith(usage.ResourceIndices);
                    result.Add(new(t, p, e, effect.Token, pixelShaderIndex, resources.OrderBy(value => value).ToList()));
                }

        return result;
    }

    public IReadOnlyList<int> OutputResources()
    {
        var resources = new HashSet<int>();
        foreach (var usage in AnalyzeEffects()) resources.UnionWith(usage.OutputResources);
        return resources.OrderBy(value => value).ToList();
    }

    public bool TryResolveEffect(int techniqueIndex, int passIndex, ulong requestedKey, out Gw2AmatEffectSelection selection)
    {
        selection = null!;
        if (techniqueIndex < 0 || techniqueIndex >= Techniques.Count) return false;
        var technique = Techniques[techniqueIndex];
        if (passIndex < 0 || passIndex >= technique.Passes.Count) return false;
        var effects = technique.Passes[passIndex].Effects;

        if (TryFindEffect(effects, requestedKey, out var effectIndex))
        {
            selection = new(techniqueIndex, passIndex, effectIndex, effects[effectIndex]);
            return true;
        }

        foreach (var fallback in Gw2AmatEffectKeys.FallbacksOf(requestedKey))
            if (TryFindEffect(effects, fallback, out effectIndex))
            {
                selection = new(techniqueIndex, passIndex, effectIndex, effects[effectIndex]);
                return true;
            }

        if (passIndex == 0 && TryFindEffect(effects, Gw2AmatEffectKeys.Default, out effectIndex))
        {
            selection = new(techniqueIndex, passIndex, effectIndex, effects[effectIndex]);
            return true;
        }

        return false;
    }

    public bool TryResolveDefaultEffect(int techniqueIndex, int passIndex, out Gw2AmatEffectSelection selection) =>
        TryResolveEffect(techniqueIndex, passIndex, Gw2AmatEffectKeys.Default, out selection);

    private static bool TryFindEffect(IReadOnlyList<Gw2AmatEffect> effects, ulong token, out int index)
    {
        for (var i = 0; i < effects.Count; i++)
            if (effects[i].Token == token)
            {
                index = i;
                return true;
            }

        index = -1;
        return false;
    }

    public IReadOnlyList<int> OutputResources(int pixelShaderIndex)
    {
        if (pixelShaderIndex < 0 || pixelShaderIndex >= Shaders.Count) return [];
        var program = TryGetProgram(Shaders[pixelShaderIndex]);
        if (program is null) return [];

        var resources = new HashSet<int>();
        foreach (var usage in Gw2ShaderUsage.ResourcesByOutput(program)) resources.UnionWith(usage.ResourceIndices);
        return resources.OrderBy(value => value).ToList();
    }

    public IReadOnlyDictionary<int, Gw2ShaderResourceRole> ResourceRoles(int pixelShaderIndex)
    {
        var roles = new Dictionary<int, Gw2ShaderResourceRole>();
        if (pixelShaderIndex < 0 || pixelShaderIndex >= Shaders.Count) return roles;
        var program = TryGetProgram(Shaders[pixelShaderIndex]);
        if (program is null) return roles;

        foreach (var use in Gw2ShaderUsage.ClassifyResources(program))
        {
            var current = roles.GetValueOrDefault(use.Resource, Gw2ShaderResourceRole.Unknown);
            roles[use.Resource] = Merge(current, use.Role);
        }

        return roles;
    }

    public IReadOnlyDictionary<int, Gw2ShaderResourceRole> ResourceRoles()
    {
        var roles = new Dictionary<int, Gw2ShaderResourceRole>();
        var programs = new Dictionary<int, Gw2ShaderProgram?>();
        foreach (var technique in Techniques)
            foreach (var pass in technique.Passes)
                foreach (var effect in pass.Effects)
                {
                    var index = checked((int)effect.PixelShaderIndex);
                    if (index < 0 || index >= Shaders.Count) continue;
                    if (!programs.TryGetValue(index, out var program))
                        programs[index] = program = TryGetProgram(Shaders[index]);
                    if (program is null) continue;

                    foreach (var use in Gw2ShaderUsage.ClassifyResources(program))
                    {
                        var current = roles.GetValueOrDefault(use.Resource, Gw2ShaderResourceRole.Unknown);
                        roles[use.Resource] = Merge(current, use.Role);
                    }
                }

        return roles;
    }

    private static Gw2ShaderResourceRole Merge(Gw2ShaderResourceRole left, Gw2ShaderResourceRole right)
    {
        if (left == Gw2ShaderResourceRole.Normal || right == Gw2ShaderResourceRole.Normal)
            return Gw2ShaderResourceRole.Normal;
        if (left == Gw2ShaderResourceRole.Direction || right == Gw2ShaderResourceRole.Direction)
            return Gw2ShaderResourceRole.Direction;
        if (left == Gw2ShaderResourceRole.BaseColor || right == Gw2ShaderResourceRole.BaseColor)
            return Gw2ShaderResourceRole.BaseColor;
        return Gw2ShaderResourceRole.Unknown;
    }

    private static Gw2ShaderProgram? TryGetProgram(Gw2AmatShader shader)
    {
        foreach (var binary in shader.Binaries)
        {
            var program = TryGetProgram(binary);
            if (program is not null) return program;
        }

        return null;
    }

    private static Gw2ShaderProgram? TryGetProgram(Gw2AmatShaderBinary binary)
    {
        if (binary.Backend != Gw2AmatShaderBackend.Dx11 || binary.Data.Length == 0) return null;
        var offset = IndexOf(binary.Data.Span, "DXBC"u8);
        if (offset < 0) return null;
        var container = Gw2Dxbc.TryParse(binary.Data[offset..]);
        if (container is null || !container.TryGetChunk("SHEX", out var shex)) return null;
        return Gw2ShaderProgram.TryParse(shex.Data);
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    public static Gw2AmatMaterial Parse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var gr = Gw2AmatGr.TryParse(document);
        var root = Root(document, "AmatMaterial");
        if (root is null) return new([], [], [], gr?.TextureTokens);

        return new(ReadShaders(root), ReadSamplerStates(root), ReadTechniques(root), gr?.TextureTokens) { SortOrder = gr?.SortOrder ?? 0, GrFlags = gr?.Flags ?? 0, GrDrawFlags = gr is null ? 0 : Gw2DrawFlags.OfGr(gr.Flags, gr.SortTriangles, gr.ProceduralAnimation) };
    }

    private static IReadOnlyList<Gw2AmatShader> ReadShaders(PackObject root)
    {
        var objects = Array(root, "shaders");
        if (objects is null) return [];

        var shaders = new List<Gw2AmatShader>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject shader)
                shaders.Add(new(checked((int)PackValue.AsU32(shader.Fields.GetValueOrDefault("isPixelShader"))), ReadBinaries(shader)));
        return shaders;
    }

    private static IReadOnlyList<Gw2AmatShaderBinary> ReadBinaries(PackObject shader)
    {
        var binaries = new List<Gw2AmatShaderBinary>(2);
        AddBinary(Gw2AmatShaderBackend.Dx11, "dx11Shader");
        AddBinary(Gw2AmatShaderBackend.Osx, "osxShader");
        AddBinary(Gw2AmatShaderBackend.Pbr, "pbrShader");
        return binaries;

        void AddBinary(Gw2AmatShaderBackend backend, string field)
        {
            if (shader.Optional(field) is PackObject binary)
            {
                var data = Blob(binary, "data");
                binaries.Add(new(backend, data, ReadConstants(binary), ReadSamplers(binary), ReadBindings(binary),
                    backend == Gw2AmatShaderBackend.Dx11 ? Gw2ShaderReflection.TryParse(data) : null));
            }
        }
    }

    private static IReadOnlyList<Gw2AmatShaderConstant> ReadConstants(PackObject binary)
    {
        var objects = Array(binary, "constants");
        if (objects is null) return [];

        var constants = new List<Gw2AmatShaderConstant>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject constant)
                constants.Add(new(PackValue.AsU32(constant.Fields.GetValueOrDefault("token"))));
        return constants;
    }

    private static IReadOnlyList<Gw2AmatSamplerBinding> ReadSamplers(PackObject binary)
    {
        var objects = Array(binary, "samplers");
        if (objects is null) return [];

        var samplers = new List<Gw2AmatSamplerBinding>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject sampler)
                samplers.Add(new(
                    PackValue.AsU64(sampler.Fields.GetValueOrDefault("token")),
                    PackValue.AsU32(sampler.Fields.GetValueOrDefault("stateIndex")),
                    PackValue.AsU32(sampler.Fields.GetValueOrDefault("textureIndex")),
                    PackValue.AsU32(sampler.Fields.GetValueOrDefault("textureSlot"))));
        return samplers;
    }

    private static IReadOnlyList<Gw2AmatShaderBinding> ReadBindings(PackObject binary)
    {
        var objects = binary.Optional("bindings") as object?[];
        if (objects is null) return [];

        var bindings = new List<Gw2AmatShaderBinding>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject binding)
                bindings.Add(new(
                    PackValue.AsU64(binding.Fields.GetValueOrDefault("token")),
                    PackValue.AsU8(binding.Fields.GetValueOrDefault("type")),
                    PackValue.AsU8(binding.Fields.GetValueOrDefault("access")),
                    PackValue.AsU8(binding.Fields.GetValueOrDefault("byte"))));
        return bindings;
    }

    private static IReadOnlyList<uint> ReadSamplerStates(PackObject root)
    {
        var objects = Array(root, "samplers");
        if (objects is null) return [];

        var states = new List<uint>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject sampler)
                states.Add(PackValue.AsU32(sampler.Fields.GetValueOrDefault("state")));
        return states;
    }

    private static IReadOnlyList<Gw2AmatTechnique> ReadTechniques(PackObject root)
    {
        var objects = Array(root, "techniques");
        if (objects is null) return [];

        var techniques = new List<Gw2AmatTechnique>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject technique)
                techniques.Add(new(PackValue.AsU32(technique.Fields.GetValueOrDefault("quality")), ReadPasses(technique)));
        return techniques;
    }

    private static IReadOnlyList<Gw2AmatPass> ReadPasses(PackObject technique)
    {
        var objects = Array(technique, "passes");
        if (objects is null) return [];

        var passes = new List<Gw2AmatPass>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject pass)
                passes.Add(new(ReadEffects(pass)));
        return passes;
    }

    private static IReadOnlyList<Gw2AmatEffect> ReadEffects(PackObject pass)
    {
        var objects = Array(pass, "effects");
        if (objects is null) return [];

        var effects = new List<Gw2AmatEffect>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject effect)
                effects.Add(new(
                    PackValue.AsU64(effect.Fields.GetValueOrDefault("token")),
                    PackValue.AsU64(effect.Fields.GetValueOrDefault("renderState")),
                    PackValue.AsU32(effect.Fields.GetValueOrDefault("shaderPassFlags")),
                    PackValue.AsU32(effect.Fields.GetValueOrDefault("pixelShaderIndex")),
                    ReadVariants(effect)));
        return effects;
    }

    private static IReadOnlyList<Gw2AmatVertexShaderVariant> ReadVariants(PackObject effect)
    {
        var objects = Array(effect, "vertexShaderVariants");
        if (objects is null) return [];

        var variants = new List<Gw2AmatVertexShaderVariant>(objects.Length);
        foreach (var value in objects)
            if (value is PackObject variant)
                variants.Add(new(
                    PackValue.AsU32(variant.Fields.GetValueOrDefault("variant")),
                    PackValue.AsU32(variant.Fields.GetValueOrDefault("vertexShaderIndex"))));
        return variants;
    }

    private static PackObject? Root(Gw2AssetDocument document, string prefix)
    {
        foreach (var chunk in document.Chunks)
            if (chunk.Root is { } root && root.TypeName.StartsWith(prefix, StringComparison.Ordinal))
                return root;
        return null;
    }

    private static object?[]? Array(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) ? value as object?[] : null;

    private static ReadOnlyMemory<byte> Blob(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) && value is ReadOnlyMemory<byte> bytes ? bytes : default;
}

public enum Gw2AmatCull
{
    None,
    Clockwise,
    CounterClockwise,
}

public static class Gw2AmatEffectCull
{
    public const uint CullCounterClockwiseFlag = 0x1;
    public const uint CullClockwiseFlag = 0x2;

    public static Gw2AmatCull Of(uint shaderPassFlags) =>
        (shaderPassFlags & CullCounterClockwiseFlag) != 0 ? Gw2AmatCull.CounterClockwise
        : (shaderPassFlags & CullClockwiseFlag) != 0 ? Gw2AmatCull.Clockwise
        : Gw2AmatCull.None;

    public static Gw2AmatCull Mirrored(Gw2AmatCull cull) => cull switch
    {
        Gw2AmatCull.CounterClockwise => Gw2AmatCull.Clockwise,
        Gw2AmatCull.Clockwise => Gw2AmatCull.CounterClockwise,
        _ => Gw2AmatCull.None,
    };
}
