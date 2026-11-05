using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Client.World;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Client.Graphics;
using Gw2.Client.Net.Transport;

namespace Gw2.Client.Settings;

public sealed record Gw2ClientOptions(Gw2ContentOptions Content, Gw2SceneOptions Scene, Gw2GraphicsOptions Graphics, Gw2DebugOptions Debug, Gw2BenchmarkOptions Benchmark)
{
    private const float DefaultFlightSpeed = 1000f;
    private static readonly string[] ReproducibleRunArguments = ["--benchmark", "--flight", "--screenshot"];

    public string? LoginExecutablePath { get; init; }

    public string? InputBindingLoadoutPath { get; init; }

    public LoginServerSettings LoginServers { get; init; } = LoginServerSettings.Retail;

    public static Gw2ClientOptions Parse(string[] arguments)
    {
        uint? environmentMapFileId = null;
        uint? mapId = null;
        var raceSexName = "HumanFemale";
        var standalone = false;
        var attachments = new List<Gw2AttachmentOption>();
        var skinAttachments = new List<Gw2SkinAttachmentOption>();
        var timeOfDay = new Gw2TimeOfDay(12, 0, 0);
        var timeOfDayRate = 1f;
        var position = Vector3.Zero;
        Vector3? cameraEye = null;
        Vector3? cameraFront = null;
        float? fieldOfView = null;
        var saved = new Gw2GraphicsOptions();
        var frameLimit = Gw2FrameLimit.Unlimited;
        if (!arguments.Any(ReproducibleRunArguments.Contains)) (saved, frameLimit) = Gw2GraphicsSettings.LoadOrSeed();
        var shadows = saved.Shadows;
        var customShadowResolutionOption = saved.CustomShadowResolutionOption;
        var customShadowCascadeCount = saved.CustomShadowCascadeCount;
        var verticalSync = saved.VerticalSync;
        var screenSpaceShadows = saved.ScreenSpaceShadows;
        var anisotropicFiltering = saved.AnisotropicFiltering;
        var shaderQuality = saved.ShaderQuality;
        var renderSampling = saved.RenderSampling;
        var reflections = saved.Reflections;
        var gpuDriven = saved.GpuDriven;
        var hiZ = saved.HiZ;
        var parallelRecording = saved.ParallelRecording;
        var volumetricFog = saved.VolumetricFog;
        var physicalBloom = saved.PhysicalBloom;
        var gamma = saved.Gamma;
        var lodDistance = saved.LodDistance;
        var textureQuality = saved.TextureQuality;
        var lowQualityWater = false;
        var postProcessing = saved.PostProcessing;
        var debugView = GrDebugView.None;
        var archiveShaders = true;
        var vanillaWaterGate = false;
        string? screenshotPath = null;
        var screenshotAtSeconds = 0f;
        string? decompiledShaderDirectory = null;
        var stillScene = false;
        var perfGraphs = false;
        var logsSlowFrames = false;
        (int Width, int Height)? resolution = null;
        float? benchmarkSeconds = null;
        var benchmarkWarmupSeconds = 4f;
        string? benchmarkName = null;
        string? benchmarkReportPath = null;
        Vector3[]? flightWaypoints = null;
        var flightSpeed = DefaultFlightSpeed;
        string? inputBindingLoadoutPath = null;
        var loginServers = LoginServerSettings.Retail;
        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--portal": loginServers = loginServers with { Portal = arguments[++index] }; break;
                case "--auth-server": loginServers = loginServers with { AuthServer = arguments[++index] }; break;
                case "--portal-certificate": loginServers = loginServers with { PortalCertificatePath = Path.GetFullPath(arguments[++index]) }; break;
                case "--auth-server-public-value": loginServers = loginServers with { AuthServerPublicValue = arguments[++index] }; break;
                case "--input-bindings": inputBindingLoadoutPath = Path.GetFullPath(arguments[++index]); break;
                case "--environment": environmentMapFileId = uint.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--map": mapId = uint.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--race": raceSexName = arguments[++index]; break;
                case "--attach": attachments.Add(Gw2AttachmentOption.Parse(arguments[++index])); break;
                case "--attach-skin": skinAttachments.Add(Gw2SkinAttachmentOption.Parse(arguments[++index])); break;
                case "--time-of-day": timeOfDay = ParseTimeOfDay(arguments[++index]); break;
                case "--time-rate": timeOfDayRate = float.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--position": position = ParsePosition(arguments[++index]); break;
                case "--camera-eye": cameraEye = ParsePosition(arguments[++index]); break;
                case "--camera-front": cameraFront = ParsePosition(arguments[++index]); break;
                case "--field-of-view": fieldOfView = float.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--lod-distance": lodDistance = Enum.Parse<Gw2LodDistance>(arguments[++index], ignoreCase: true); break;
                case "--textures": textureQuality = Enum.Parse<Gw2.Client.Graphics.GrTextureQuality>(arguments[++index], ignoreCase: true); break;
                case "--shadows": shadows = Enum.Parse<Gw2ShadowsPreset>(arguments[++index], ignoreCase: true); break;
                case "--no-screen-space-shadows": screenSpaceShadows = false; break;
                case "--no-anisotropic-filtering": anisotropicFiltering = false; break;
                case "--shader-quality": shaderQuality = Enum.Parse<Gw2ShaderQuality>(arguments[++index], ignoreCase: true); break;
                case "--render-sampling": renderSampling = Enum.Parse<Gw2RenderSampling>(arguments[++index], ignoreCase: true); break;
                case "--reflections": reflections = Enum.Parse<Gw2.Client.Graphics.GrReflections>(arguments[++index].Replace("-", ""), ignoreCase: true); break;
                case "--gpu-driven": gpuDriven = ParseOnOff(arguments[++index]); break;
                case "--hi-z": hiZ = ParseOnOff(arguments[++index]); break;
                case "--parallel-recording": parallelRecording = ParseOnOff(arguments[++index]); break;
                case "--volumetric-fog": volumetricFog = ParseOnOff(arguments[++index]); break;
                case "--physical-bloom": physicalBloom = ParseOnOff(arguments[++index]); break;
                case "--gamma": gamma = Gw2.Client.Graphics.GrGamma.Clamp(float.Parse(arguments[++index], CultureInfo.InvariantCulture)); break;
                case "--low-quality-water": lowQualityWater = true; break;
                case "--saturation-and-tint": postProcessing |= Gw2PostProcessingOptions.ColorTint; break;
                case "--depth-blur": postProcessing |= Gw2PostProcessingOptions.DepthBlur; break;
                case "--light-adaptation": postProcessing |= Gw2PostProcessingOptions.LightAdaptation; break;
                case "--light-rays": postProcessing |= Gw2PostProcessingOptions.LightRays; break;
                case "--distortion": postProcessing |= Gw2PostProcessingOptions.Distortion; break;
                case "--no-bloom": postProcessing &= ~Gw2PostProcessingOptions.Bloom; break;
                case "--no-color-grading": postProcessing &= ~Gw2PostProcessingOptions.ColorGrading; break;
                case "--no-ambient-occlusion": postProcessing &= ~Gw2PostProcessingOptions.AmbientOcclusion; break;
                case "--debug-view": debugView = ParseDebugView(arguments[++index]); break;
                case "--hand-shaders": archiveShaders = false; break;
                case "--vanilla-water-gate": vanillaWaterGate = true; break;
                case "--screenshot": screenshotPath = arguments[++index]; break;
                case "--screenshot-at": screenshotAtSeconds = float.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--decompiled-shaders": decompiledShaderDirectory = Path.GetFullPath(arguments[++index]); break;
                case "--still-scene": stillScene = true; break;
                case "--perf-graphs": perfGraphs = true; break;
                case "--slow-frames": logsSlowFrames = true; break;
                case "--standalone": standalone = true; break;
                case "--frame-limit": frameLimit = Gw2FrameLimit.Parse(arguments[++index]); break;
                case "--resolution": resolution = ParseResolution(arguments[++index]); break;
                case "--benchmark": benchmarkSeconds = float.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--benchmark-warmup": benchmarkWarmupSeconds = float.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                case "--benchmark-name": benchmarkName = arguments[++index]; break;
                case "--benchmark-report": benchmarkReportPath = Path.GetFullPath(arguments[++index]); break;
                case "--flight": flightWaypoints = arguments[++index].Split(';').Select(ParsePosition).ToArray(); break;
                case "--flight-speed": flightSpeed = float.Parse(arguments[++index], CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown argument {arguments[index]}.");
            }
        }

        var flight = flightWaypoints is null ? null : new Gw2FlightPath(flightWaypoints, flightSpeed);
        var (backBufferWidth, backBufferHeight) = resolution ?? (benchmarkSeconds is null && flight is null ? (1600, 900) : (1920, 1080));
        var localDatPath = Gw2Installation.LocalDatPath();
        var datPath = Gw2Installation.DatPath()
            ?? throw new InvalidOperationException("No Gw2.dat found; place the client in the Guild Wars 2 folder or install the game.");
        return new(
            new(datPath, localDatPath, environmentMapFileId, raceSexName, mapId) { Attachments = attachments, SkinAttachments = skinAttachments },
            new(timeOfDay, timeOfDayRate, position, cameraEye, cameraFront, fieldOfView),
            new() { Shadows = shadows, CustomShadowResolutionOption = customShadowResolutionOption, CustomShadowCascadeCount = customShadowCascadeCount, VerticalSync = verticalSync, ScreenSpaceShadows = screenSpaceShadows, AnisotropicFiltering = anisotropicFiltering, ShaderQuality = shaderQuality, PostProcessing = postProcessing, RenderSampling = renderSampling, Reflections = reflections, GpuDriven = gpuDriven, HiZ = hiZ, ParallelRecording = parallelRecording, VolumetricFog = volumetricFog, PhysicalBloom = physicalBloom, Gamma = gamma, LodDistance = lodDistance, TextureQuality = textureQuality },
            new(debugView, archiveShaders, vanillaWaterGate, lowQualityWater, screenshotPath, screenshotAtSeconds, decompiledShaderDirectory, stillScene, perfGraphs),
            new(frameLimit, backBufferWidth, backBufferHeight, flight is null ? benchmarkSeconds : benchmarkSeconds ?? flight.DurationSeconds, benchmarkWarmupSeconds, benchmarkName, benchmarkReportPath, flight, logsSlowFrames))
        {
            LoginServers = loginServers,
            LoginExecutablePath = standalone || environmentMapFileId is not null || benchmarkSeconds is not null || flight is not null ? null : GameExecutablePath(),
            InputBindingLoadoutPath = inputBindingLoadoutPath,
        };
    }

    private static string GameExecutablePath() =>
        Gw2Installation.ExecutablePath()
            ?? throw new InvalidOperationException("Logging in needs Gw2-64.exe; place the client in the Guild Wars 2 folder or pass --standalone.");

    private static bool ParseOnOff(string value) => value switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException($"Expected on or off, got {value}."),
    };

    private static Vector3 ParsePosition(string value)
    {
        var components = value.Split(',').Select(component => float.Parse(component, CultureInfo.InvariantCulture)).ToArray();
        if (components.Length != 3) throw new ArgumentException($"Position {value} needs x,y,z.");
        return new(components[0], components[1], components[2]);
    }

    private static (int Width, int Height) ParseResolution(string value)
    {
        var components = value.Split('x');
        if (components.Length != 2) throw new ArgumentException($"Resolution {value} needs WIDTHxHEIGHT.");
        return (int.Parse(components[0], CultureInfo.InvariantCulture), int.Parse(components[1], CultureInfo.InvariantCulture));
    }

    private static GrDebugView ParseDebugView(string value) => value switch
    {
        "shadow-cascades" => GrDebugView.ShadowCascades,
        "normals" => GrDebugView.Normals,
        "lighting" => GrDebugView.Lighting,
        "overdraw" => GrDebugView.Overdraw,
        "clay" => GrDebugView.Clay,
        "texel-density" => GrDebugView.TexelDensity,
        "albedo" => GrDebugView.Albedo,
        "wireframe" => GrDebugView.Wireframe,
        "ambient-occlusion" => GrDebugView.AmbientOcclusion,
        _ => throw new ArgumentException($"Debug view {value} is not one of shadow-cascades, normals, lighting, overdraw, clay, texel-density, albedo, wireframe, ambient-occlusion."),
    };

    private static Gw2TimeOfDay ParseTimeOfDay(string value)
    {
        var time = TimeSpan.ParseExact(value, [@"hh\:mm", @"hh\:mm\:ss"], CultureInfo.InvariantCulture);
        return new(time.Hours, time.Minutes, time.Seconds);
    }

    private static string? ExistingOrNull(string path) => File.Exists(path) ? path : null;
}
