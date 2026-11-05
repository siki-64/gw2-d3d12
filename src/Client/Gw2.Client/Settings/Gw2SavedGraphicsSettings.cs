using System.Collections.Generic;
using Gw2.Client.Graphics;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Settings;

public static class Gw2SavedGraphicsSettings
{
    public const string Shadows = "shadowsPreset";
    public const string ShadowResolution = "shadowsResolution";
    public const string ShadowCascades = "shadowsCascadeCount";
    public const string Reflections = "reflections";
    public const string TextureDetail = "textureDetail";
    public const string LodDistance = "lodDistance";
    public const string Sampling = "sampling";
    public const string Shaders = "shaders";
    public const string Gamma = "gamma";
    public const string FrameLimit = "frameLimit";
    public const string VerticalSync = "verticalSync";
    public const string ScreenSpaceShadows = "screenspaceShadows";
    public const string BestTextureFiltering = "bestTextureFiltering";
    public const string DepthBlur = "depthBlur";

    public static readonly string[] Keys =
    [
        Shadows, ShadowResolution, ShadowCascades, Reflections, TextureDetail, LodDistance, Sampling, Shaders,
        Gamma, FrameLimit, VerticalSync, ScreenSpaceShadows, BestTextureFiltering, DepthBlur,
    ];

    private const int HighestTextureDetail = 2;

    public static (Gw2GraphicsOptions Graphics, int FrameLimit) Apply(Gw2GraphicsOptions graphics, int frameLimit, GraphicsSettingsFile saved, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            switch (key)
            {
                case Shadows when saved.EnumIndexOf(key) is { } index: graphics = graphics with { Shadows = (Gw2ShadowsPreset)index }; break;
                case ShadowResolution when saved.EnumIndexOf(key) is { } index: graphics = graphics with { CustomShadowResolutionOption = index }; break;
                case ShadowCascades when saved.EnumIndexOf(key) is { } index: graphics = graphics with { CustomShadowCascadeCount = index }; break;
                case Reflections when saved.EnumIndexOf(key) is { } index: graphics = graphics with { Reflections = (GrReflections)index }; break;
                case TextureDetail when saved.EnumIndexOf(key) is { } index: graphics = graphics with { TextureQuality = (GrTextureQuality)(HighestTextureDetail - index) }; break;
                case LodDistance when saved.EnumIndexOf(key) is { } index: graphics = graphics with { LodDistance = (Gw2LodDistance)index }; break;
                case Sampling when saved.EnumIndexOf(key) is { } index: graphics = graphics with { RenderSampling = (Gw2RenderSampling)index }; break;
                case Shaders when saved.EnumIndexOf(key) is { } index: graphics = graphics with { ShaderQuality = Gw2ShaderQuality.Low + index }; break;
                case Gamma when saved.FloatOf(key) is { } value: graphics = graphics with { Gamma = GrGamma.Clamp(value) }; break;
                case FrameLimit when saved.ValueOf(key) is { } value: frameLimit = Gw2FrameLimit.Parse(value); break;
                case VerticalSync when saved.BoolOf(key) is { } value: graphics = graphics with { VerticalSync = value }; break;
                case ScreenSpaceShadows when saved.BoolOf(key) is { } value: graphics = graphics with { ScreenSpaceShadows = value }; break;
                case BestTextureFiltering when saved.BoolOf(key) is { } value: graphics = graphics with { AnisotropicFiltering = value }; break;
                case DepthBlur when saved.BoolOf(key) is { } value:
                    graphics = graphics with { PostProcessing = value ? graphics.PostProcessing | Gw2PostProcessingOptions.DepthBlur : graphics.PostProcessing & ~Gw2PostProcessingOptions.DepthBlur };
                    break;
            }
        }
        return (graphics, frameLimit);
    }
}
