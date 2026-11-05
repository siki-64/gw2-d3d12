using System.Numerics;

namespace Gw2.Client.Graphics;

public sealed record GrContactShadowSettings(
    bool Enabled,
    float TotalMarchDistance,
    float ThicknessMinimumNear,
    float ThicknessMaximumNear,
    float ThicknessMinimumFar,
    float ThicknessMaximumFar,
    float NearFarChangeDistance,
    float DistanceCutoff,
    int DebugMode,
    int JitterType,
    float JitterAmount,
    int FalloffStart)
{
    public static GrContactShadowSettings Defaults { get; } = new(
        Enabled: true,
        TotalMarchDistance: 8.75f,
        ThicknessMinimumNear: 0.07f,
        ThicknessMaximumNear: 1f,
        ThicknessMinimumFar: 0.6f,
        ThicknessMaximumFar: 10f,
        NearFarChangeDistance: 1000f,
        DistanceCutoff: 1000f,
        DebugMode: 0,
        JitterType: 2,
        JitterAmount: 0.2f,
        FalloffStart: 0);

    public Vector4 SettingsConstant => Enabled ? new(TotalMarchDistance, ThicknessMinimumNear, ThicknessMaximumNear, DistanceCutoff) : Vector4.Zero;

    public Vector4 DebugSettingsConstant => new(DebugMode, JitterType + JitterAmount, FalloffStart, 0f);

    public Vector4 Settings2Constant => new(TotalMarchDistance, ThicknessMinimumFar, ThicknessMaximumFar, NearFarChangeDistance);
}
