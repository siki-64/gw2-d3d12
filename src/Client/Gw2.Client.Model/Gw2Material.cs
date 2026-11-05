namespace Gw2.Client.Model;

public sealed record Gw2MaterialTexture(
    int MaterialTextureIndex,
    ulong Token,
    string Role,
    uint? ContentFileId,
    int SamplerTextureSlot,
    int SamplerStateIndex,
    string? SamplerName,
    int UvPSInputIndex,
    ulong BlitId = 0,
    uint UvAnimId = 0);

public sealed record Gw2MaterialConstant(uint Token, float[] Value, uint Flags);

public static class Gw2MaterialConstantTokens
{
    public const uint BaseColor = 0x8D45B133;
    public const uint DyeColor = 0x365CD871;
    public const uint FadeMod = 0x4FA505DD;
    public const uint CutFade = 0x54731B35;
    public const uint AlphaRef = 0x624307EA;
    public const uint Specular = 0x3567E669;
}

public sealed record Gw2MaterialTechnique(string Name, uint Token);

public sealed class Gw2Material
{
    public uint MaterialId { get; init; }
    public uint? MaterialFileId { get; init; }
    public IReadOnlyList<Gw2MaterialTexture> Textures { get; init; } = Array.Empty<Gw2MaterialTexture>();
    public IReadOnlyList<Gw2MaterialConstant> Constants { get; init; } = Array.Empty<Gw2MaterialConstant>();
    public bool AlphaTested { get; init; }
    public IReadOnlyList<Gw2MaterialTechnique> Techniques { get; init; } = Array.Empty<Gw2MaterialTechnique>();
    public string? Quality { get; init; }
    public uint MaterialFlags { get; init; }
    public uint SortOrder { get; init; }
    public bool TwoSided { get; init; }
    public ulong RenderState { get; init; }
    public string Surface { get; init; } = "Opaque";
    public string Blend { get; init; } = "Opaque";
    public IReadOnlyList<Gw2.Dat.Content.Gw2ModelTextureTransform> TextureTransforms { get; init; } = Array.Empty<Gw2.Dat.Content.Gw2ModelTextureTransform>();
}
