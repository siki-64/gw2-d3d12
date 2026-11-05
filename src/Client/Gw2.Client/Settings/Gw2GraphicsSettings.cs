using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Gw2.Client.Graphics;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Settings;

public sealed record Gw2GraphicsState(Gw2GraphicsOptions Graphics, int FrameLimit);

public enum Gw2GraphicsSettingKind { Choice, Toggle, Amount }

public sealed record Gw2GraphicsSetting(
    string Section,
    string Key,
    string Label,
    Gw2GraphicsSettingKind Kind,
    string[] Choices,
    float MinAmount,
    float MaxAmount,
    bool AppliesWhileRunning,
    Func<Gw2GraphicsState, float> ValueOf,
    Func<Gw2GraphicsState, float, Gw2GraphicsState> WithValue);

public static class Gw2GraphicsSettings
{
    private const string RootElement = "GRAPHICS";
    private const string OptionElement = "OPTION";
    private const int HighestTextureDetail = 2;
    private const string Display = "Display";
    private const string Detail = "Detail";
    private const string Lighting = "Lighting and shadows";
    private const string PostProcessing = "Post-processing";
    private const string Performance = "Performance";
    private static readonly int[] FrameLimitChoices = [.. Gw2FrameLimit.Caps, Gw2FrameLimit.Uncapped];

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gw2ClientRemake", "GraphicsSettings.xml");

    public static IReadOnlyList<Gw2GraphicsSetting> All { get; } =
    [
        Choice(Display, "frameLimit", "Frame limiter", [.. Gw2FrameLimit.Caps.Select(cap => cap.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Unlimited"], true, state => Math.Max(0, Array.IndexOf(FrameLimitChoices, state.FrameLimit)), (state, index) => state with { FrameLimit = FrameLimitChoices[index] }),
        Toggle(Display, "verticalSync", "Vertical sync", false, state => state.Graphics.VerticalSync, (state, on) => Graphics(state, state.Graphics with { VerticalSync = on })),
        Choice(Display, "renderSampling", "Render sampling", ["Subsample", "Native", "Supersample"], false, state => (int)state.Graphics.RenderSampling, (state, index) => Graphics(state, state.Graphics with { RenderSampling = (Gw2RenderSampling)index })),
        new(Display, "gamma", "Gamma", Gw2GraphicsSettingKind.Amount, [], GrGamma.Minimum, GrGamma.Maximum, true, state => state.Graphics.Gamma, (state, value) => Graphics(state, state.Graphics with { Gamma = GrGamma.Clamp(value) })),
        Choice(Detail, "textures", "Textures", ["Low", "Medium", "High"], false, state => HighestTextureDetail - (int)state.Graphics.TextureQuality, (state, index) => Graphics(state, state.Graphics with { TextureQuality = (GrTextureQuality)(HighestTextureDetail - index) })),
        Toggle(Detail, "bestTextureFiltering", "Best texture filtering", true, state => state.Graphics.AnisotropicFiltering, (state, on) => Graphics(state, state.Graphics with { AnisotropicFiltering = on })),
        Choice(Detail, "shaders", "Shaders", ["Low", "Medium", "High", "Ultra"], false, state => state.Graphics.ShaderQuality - Gw2ShaderQuality.Low, (state, index) => Graphics(state, state.Graphics with { ShaderQuality = Gw2ShaderQuality.Low + index })),
        Choice(Detail, "lodDistance", "LOD distance", ["Low", "Medium", "High", "Ultra"], false, state => (int)state.Graphics.LodDistance, (state, index) => Graphics(state, state.Graphics with { LodDistance = (Gw2LodDistance)index })),
        Choice(Detail, "reflections", "Reflections", ["None", "Terrain and sky", "All"], true, state => (int)state.Graphics.Reflections, (state, index) => Graphics(state, state.Graphics with { Reflections = (GrReflections)index })),
        Choice(Lighting, "shadows", "Shadows", ["Off", "Low", "Medium", "High", "Ultra", "Custom"], false, state => (int)state.Graphics.Shadows, (state, index) => Graphics(state, state.Graphics with { Shadows = (Gw2ShadowsPreset)index })),
        Choice(Lighting, "shadowResolution", "Shadow resolution", ["None", "512", "1024", "2048"], false, state => state.Graphics.CustomShadowResolutionOption, (state, index) => Graphics(state, state.Graphics with { CustomShadowResolutionOption = index })),
        Choice(Lighting, "shadowCascades", "Shadow cascades", ["0", "1", "2", "3"], false, state => state.Graphics.CustomShadowCascadeCount, (state, index) => Graphics(state, state.Graphics with { CustomShadowCascadeCount = index })),
        Toggle(Lighting, "screenSpaceShadows", "Screen-space shadows", false, state => state.Graphics.ScreenSpaceShadows, (state, on) => Graphics(state, state.Graphics with { ScreenSpaceShadows = on })),
        Toggle(Lighting, "ambientOcclusion", "Ambient occlusion", true, state => Has(state, Gw2PostProcessingOptions.AmbientOcclusion), (state, on) => With(state, Gw2PostProcessingOptions.AmbientOcclusion, on)),
        Toggle(Lighting, "volumetricFog", "Volumetric fog", true, state => state.Graphics.VolumetricFog, (state, on) => Graphics(state, state.Graphics with { VolumetricFog = on })),
        Toggle(Lighting, "lightRays", "Light rays", true, state => Has(state, Gw2PostProcessingOptions.LightRays), (state, on) => With(state, Gw2PostProcessingOptions.LightRays, on)),
        Toggle(PostProcessing, "bloom", "Bloom", true, state => Has(state, Gw2PostProcessingOptions.Bloom), (state, on) => With(state, Gw2PostProcessingOptions.Bloom, on)),
        Toggle(PostProcessing, "physicalBloom", "Physical bloom", true, state => state.Graphics.PhysicalBloom, (state, on) => Graphics(state, state.Graphics with { PhysicalBloom = on })),
        Toggle(PostProcessing, "lightAdaptation", "Light adaptation", true, state => Has(state, Gw2PostProcessingOptions.LightAdaptation), (state, on) => With(state, Gw2PostProcessingOptions.LightAdaptation, on)),
        Toggle(PostProcessing, "colorGrading", "Color grading", true, state => Has(state, Gw2PostProcessingOptions.ColorGrading), (state, on) => With(state, Gw2PostProcessingOptions.ColorGrading, on)),
        Toggle(PostProcessing, "saturationAndTint", "Saturation and tint", true, state => Has(state, Gw2PostProcessingOptions.ColorTint), (state, on) => With(state, Gw2PostProcessingOptions.ColorTint, on)),
        Toggle(PostProcessing, "depthBlur", "Depth blur", true, state => Has(state, Gw2PostProcessingOptions.DepthBlur), (state, on) => With(state, Gw2PostProcessingOptions.DepthBlur, on)),
        Toggle(PostProcessing, "distortion", "Heat distortion", true, state => Has(state, Gw2PostProcessingOptions.Distortion), (state, on) => With(state, Gw2PostProcessingOptions.Distortion, on)),
        Toggle(Performance, "gpuDriven", "GPU-driven rendering", false, state => state.Graphics.GpuDriven, (state, on) => Graphics(state, state.Graphics with { GpuDriven = on })),
        Toggle(Performance, "hiZ", "Hi-Z occlusion culling", true, state => state.Graphics.HiZ, (state, on) => Graphics(state, state.Graphics with { HiZ = on })),
        Toggle(Performance, "parallelRecording", "Parallel command recording", true, state => state.Graphics.ParallelRecording, (state, on) => Graphics(state, state.Graphics with { ParallelRecording = on })),
    ];

    public static Gw2GraphicsState LoadOrSeed()
    {
        if (File.Exists(FilePath)) return Parse(File.ReadAllText(FilePath));
        var defaults = new Gw2GraphicsState(new Gw2GraphicsOptions(), Gw2FrameLimit.Unlimited);
        if (Gw2Installation.GraphicsSettingsPath() is not { } gamePath) return defaults;
        var (graphics, frameLimit) = Gw2SavedGraphicsSettings.Apply(defaults.Graphics, defaults.FrameLimit, GraphicsSettingsFile.Parse(File.ReadAllText(gamePath)), Gw2SavedGraphicsSettings.Keys);
        return new Gw2GraphicsState(graphics, frameLimit);
    }

    public static void Save(Gw2GraphicsState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, ToXml(state));
    }

    public static string ToXml(Gw2GraphicsState state) =>
        new XDocument(new XElement(RootElement, All.Select(setting => new XElement(OptionElement,
            new XAttribute("Name", setting.Key),
            new XAttribute("Value", StoredValueOf(setting, setting.ValueOf(state))))))).ToString();

    public static Gw2GraphicsState Parse(string xml)
    {
        var state = new Gw2GraphicsState(new Gw2GraphicsOptions(), Gw2FrameLimit.Unlimited);
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in XDocument.Parse(xml).Root?.Elements(OptionElement) ?? [])
            if (option.Attribute("Name")?.Value is { } name && option.Attribute("Value")?.Value is { } value) stored[name] = value;
        foreach (var setting in All)
            if (stored.TryGetValue(setting.Key, out var text) && ValueOfStored(setting, text) is { } value)
                state = setting.WithValue(state, value);
        return state;
    }

    private static string StoredValueOf(Gw2GraphicsSetting setting, float value) => setting.Kind switch
    {
        Gw2GraphicsSettingKind.Choice => setting.Choices[(int)value],
        Gw2GraphicsSettingKind.Toggle => value != 0f ? "true" : "false",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static float? ValueOfStored(Gw2GraphicsSetting setting, string text)
    {
        switch (setting.Kind)
        {
            case Gw2GraphicsSettingKind.Choice:
                var index = Array.IndexOf(setting.Choices, text);
                return index < 0 ? null : index;
            case Gw2GraphicsSettingKind.Toggle:
                return bool.TryParse(text, out var on) ? (on ? 1f : 0f) : null;
            default:
                return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ? amount : null;
        }
    }

    private static Gw2GraphicsSetting Choice(string section, string key, string label, string[] choices, bool appliesWhileRunning, Func<Gw2GraphicsState, int> indexOf, Func<Gw2GraphicsState, int, Gw2GraphicsState> withIndex) =>
        new(section, key, label, Gw2GraphicsSettingKind.Choice, choices, 0f, choices.Length - 1, appliesWhileRunning, state => indexOf(state), (state, value) => withIndex(state, Math.Clamp((int)value, 0, choices.Length - 1)));

    private static Gw2GraphicsSetting Toggle(string section, string key, string label, bool appliesWhileRunning, Func<Gw2GraphicsState, bool> isOn, Func<Gw2GraphicsState, bool, Gw2GraphicsState> withOn) =>
        new(section, key, label, Gw2GraphicsSettingKind.Toggle, [], 0f, 1f, appliesWhileRunning, state => isOn(state) ? 1f : 0f, (state, value) => withOn(state, value != 0f));

    private static Gw2GraphicsState Graphics(Gw2GraphicsState state, Gw2GraphicsOptions graphics) => state with { Graphics = graphics };

    private static bool Has(Gw2GraphicsState state, Gw2PostProcessingOptions flag) => state.Graphics.PostProcessing.HasFlag(flag);

    private static Gw2GraphicsState With(Gw2GraphicsState state, Gw2PostProcessingOptions flag, bool on) =>
        Graphics(state, state.Graphics with { PostProcessing = on ? state.Graphics.PostProcessing | flag : state.Graphics.PostProcessing & ~flag });
}
