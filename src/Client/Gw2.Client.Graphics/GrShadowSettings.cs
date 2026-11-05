using System;

namespace Gw2.Client.Graphics;

public sealed record GrShadowSettings(
    float LightRange,
    int CascadeResolution,
    float CascadeSplitBlend,
    float MaximumShadowDistance,
    int CascadeCount,
    bool PixelSnapCascades,
    float SlopeBias,
    float SampleBias,
    int BakedShadowResolution,
    GrContactShadowSettings ContactShadows)
{
    public static GrShadowSettings Defaults { get; } = new(
        LightRange: 20000f,
        CascadeResolution: 1024,
        CascadeSplitBlend: 0.5f,
        MaximumShadowDistance: 5000f,
        CascadeCount: 2,
        PixelSnapCascades: true,
        SlopeBias: 1.5f,
        SampleBias: 0.00025f,
        BakedShadowResolution: 1024,
        ContactShadows: GrContactShadowSettings.Defaults);

    private const int ResolutionOptionBase = 256;
    private const int MinimumCascadeCount = 1;
    private const int MaximumCascadeCount = 3;

    public static GrShadowSettings FromGraphicsOptions(int resolutionOption, int cascadeCount) =>
        Defaults with
        {
            CascadeResolution = ResolutionOptionBase << resolutionOption,
            CascadeCount = Math.Clamp(cascadeCount, MinimumCascadeCount, MaximumCascadeCount),
        };
}
