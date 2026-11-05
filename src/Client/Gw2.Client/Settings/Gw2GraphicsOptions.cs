using System;

namespace Gw2.Client.Settings;

public enum Gw2ShadowsPreset { None, Low, Medium, High, Ultra, Custom }

[Flags]
public enum Gw2PostProcessingOptions : uint
{
    None = 0,
    Bloom = 0x1,
    ColorGrading = 0x2,
    DepthBlur = 0x4,
    Distortion = 0x20,
    LightAdaptation = 0x80,
    LightRays = 0x200,
    SelectionOutline = 0x800,
    AmbientOcclusion = 0x8000,
    ColorTint = 0x10000,
    LiveDefault = Bloom | ColorGrading | AmbientOcclusion,
}

public enum Gw2RenderSampling { Subsample, Native, Supersample }

public sealed record Gw2GraphicsOptions
{
    public Gw2ShadowsPreset Shadows { get; init; } = Gw2ShadowsPreset.Medium;
    public int CustomShadowResolutionOption { get; init; } = 2;
    public int CustomShadowCascadeCount { get; init; } = 2;
    public bool VerticalSync { get; init; }
    public bool ScreenSpaceShadows { get; init; } = true;
    public bool AnisotropicFiltering { get; init; } = true;
    public Gw2.Dat.Content.Gw2ShaderQuality ShaderQuality { get; init; } = Gw2.Dat.Content.Gw2ShaderQuality.High;
    public Gw2PostProcessingOptions PostProcessing { get; init; } = Gw2PostProcessingOptions.LiveDefault;
    public Gw2RenderSampling RenderSampling { get; init; } = Gw2RenderSampling.Native;
    public Gw2.Client.Graphics.GrReflections Reflections { get; init; } = Gw2.Client.Graphics.GrReflections.TerrainAndSky;
    public bool GpuDriven { get; init; } = true;
    public bool HiZ { get; init; }
    public bool ParallelRecording { get; init; } = true;
    public bool VolumetricFog { get; init; }
    public bool PhysicalBloom { get; init; }
    public float Gamma { get; init; } = Gw2.Client.Graphics.GrGamma.Neutral;
    public Gw2.Dat.Content.Gw2LodDistance LodDistance { get; init; } = Gw2.Dat.Content.Gw2LodDistance.High;
    public Gw2.Client.Graphics.GrTextureQuality TextureQuality { get; init; } = Gw2.Client.Graphics.GrTextureQuality.High;

    public bool DynamicShadows => Shadows >= Gw2ShadowsPreset.Medium;

    public Gw2.Client.Graphics.GrShadowSettings ShadowSettings
    {
        get
        {
            var (resolutionOption, cascadeCount) = Shadows switch
            {
                Gw2ShadowsPreset.High => (3, 2),
                Gw2ShadowsPreset.Ultra => (3, 3),
                Gw2ShadowsPreset.Custom => (CustomShadowResolutionOption, CustomShadowCascadeCount),
                _ => (2, 2),
            };
            var settings = Gw2.Client.Graphics.GrShadowSettings.FromGraphicsOptions(resolutionOption, cascadeCount);
            return settings with { ContactShadows = settings.ContactShadows with { Enabled = ScreenSpaceShadows } };
        }
    }
}
