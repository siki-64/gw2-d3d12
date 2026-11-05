using Gw2.Client.Graphics;

namespace Gw2.Client.Settings;

public sealed record Gw2DebugOptions(GrDebugView DebugView, bool ArchiveShaders, bool VanillaWaterGate, bool LowQualityWater, string? ScreenshotPath, float ScreenshotAtSeconds, string? DecompiledShaderDirectory, bool StillScene, bool PerfGraphs);
