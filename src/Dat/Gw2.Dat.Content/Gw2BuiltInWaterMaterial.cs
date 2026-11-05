namespace Gw2.Dat.Content;

public sealed record Gw2BuiltInWaterSampler(string Name, uint TextureSlot, bool IsEngineTexture, uint ResourceIndex, Gw2AmatSamplerState State);

public sealed record Gw2BuiltInWaterMaterial(string Technique, long PackAddress, int PackSize, ulong RenderState, uint ShaderPassFlags, IReadOnlyList<Gw2BuiltInWaterSampler> Samplers)
{
    private static readonly Gw2AmatSamplerState ClampedNoMip = new(0x40A);
    private static readonly Gw2AmatSamplerState WrappedNoMip = new(0x400);
    private static readonly Gw2AmatSamplerState ClampedLinearMip = new(0xA);

    public static IReadOnlyDictionary<string, uint> ConstantTokens { get; } = new Dictionary<string, uint>
    {
        ["CameraPosition"] = 0xBEE568EF,
        ["FogDepthCue"] = 0x42E868B3,
        ["FogParam0"] = 0x35B8AF24,
        ["World"] = 0x4E958DEE,
        ["WorldViewProjection"] = 0x303EBB81,
        ["wtrprmv"] = 0xB8DA19B0,
        ["wtrprmd"] = 0x4EF7EB64,
        ["wtrprme"] = 0x57CAC495,
        ["FogColorNearMinusFar"] = 0x311E2790,
        ["FogColorFar"] = 0x39F100C1,
        ["FogColorHeight"] = 0x42C3D9F2,
        ["ScreenDims"] = 0x4F57E8DC,
        ["BlurryWater"] = 0xCF2BCCA0,
        ["wtrprmp"] = 0xB007407F,
        ["wtrclrs"] = 0xCC4F00CB,
        ["wtrclrd"] = 0x50C7201D,
        ["wtrclrp"] = 0xB1D67538,
        ["FresnelTerminator"] = 0x9780A142,
    };

    public static Gw2BuiltInWaterMaterial Above { get; } = new(
        "AboveWater",
        0x7FF7B3C3F1F0,
        28948,
        0,
        Gw2AmatShaderPassFlags.NoAlphaWrite,
        [
            new("ssReflect", 0, false, 2, ClampedNoMip),
            new("ssBump", 1, false, 3, WrappedNoMip),
            new("ssPattern", 2, false, 4, WrappedNoMip),
            new("ssDepth", 12, true, 3, ClampedNoMip),
            new("ssEnvMap", 13, true, 1, ClampedLinearMip),
            new("ssScreen", 14, true, 4, ClampedNoMip),
        ]);

    public static Gw2BuiltInWaterMaterial Below { get; } = new(
        "BelowWater",
        0x7FF7B3C46310,
        14644,
        0,
        Gw2AmatShaderPassFlags.NoAlphaWrite,
        [
            new("ssBump", 1, false, 1, WrappedNoMip),
            new("ssPattern", 2, false, 2, WrappedNoMip),
            new("ssDepth", 12, true, 3, ClampedNoMip),
            new("ssScreen", 14, true, 4, ClampedNoMip),
        ]);
}
