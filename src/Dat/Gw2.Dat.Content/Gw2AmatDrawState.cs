namespace Gw2.Dat.Content;

public enum Gw2DepthTest
{
    Disabled,
    LessEqual,
    Equal,
    Greater,
    Always,
}

[Flags]
public enum Gw2ColorWrite
{
    None = 0,
    Red = 0x1,
    Green = 0x2,
    Blue = 0x4,
    Alpha = 0x8,
    Rgb = Red | Green | Blue,
    All = Rgb | Alpha,
}

public static class Gw2AmatShaderPassFlags
{
    public const uint NoColorWrite = 0x4;
    public const uint NoRgbWrite = 0x8;
    public const uint LessEqualOnLaterPasses = 0x10;
    public const uint NoDepthTest = 0x20;
    public const uint NoDepthWrite = 0x40;
    public const uint ReverseSubtract = 0x2000;
    public const uint NoAlphaWrite = 0x4000;
    public const uint NegativeDepthBias = 0x8000;
}

public static class Gw2DrawFlags
{
    public const uint NegativeDepthBias = 0x80;
    public const uint EqualDepthTest = 0x100;
    public const uint GreaterDepthTest = 0x200;
    public const uint NoAlphaWrite = 0x400;
    public const uint NoRgbWrite = 0x800;
    public const uint NoDepthTest = 0x1000;
    public const uint LessEqualWithoutDepthWrite = 0x2000;
    public const uint SkipEffectCull = 0x4000;
    public const uint KeepsStageDepthWrite = 0x3800000;

    private static readonly (uint GrFlag, uint DrawFlag)[] DrawFlagByGrFlag =
    [
        (0x200, 0x10000), (0x800, 0x40000), (0x100, 0x80000), (0x10000, GreaterDepthTest), (0x40000, 0x400000), (0x80000, 0x10000000),
    ];

    private static readonly uint[] DrawFlagBySortTriangles = [0, 0x800000, 0x1000000, 0x2000000];

    private static readonly uint[] DrawFlagByProceduralAnimation = [0, 0x4, 0x2, 0x10, 0x20];

    public static uint OfGr(uint grFlags, byte sortTriangles, byte proceduralAnimation)
    {
        var drawFlags = 0u;
        if (sortTriangles < DrawFlagBySortTriangles.Length) drawFlags |= DrawFlagBySortTriangles[sortTriangles];
        if (proceduralAnimation < DrawFlagByProceduralAnimation.Length) drawFlags |= DrawFlagByProceduralAnimation[proceduralAnimation];
        foreach (var (grFlag, drawFlag) in DrawFlagByGrFlag)
            if ((grFlags & grFlag) != 0) drawFlags |= drawFlag;
        return drawFlags;
    }
}

public static class Gw2DrawStageFlags
{
    public const uint KeepsDepthWriteForMarkedItems = 0x2000;
    public const uint NoDepthWrite = 0x4000;

    private static readonly (uint GrFlag, uint StageFlag)[] StageFlagByGrFlag =
    [
        (0x1, 0x80), (0x2, 0x200), (0x4, 0x400), (0x8, 0x1800), (0x10, 0x8000), (0x40, 0x800), (0x80, NoDepthWrite), (0x4000, 0x10000),
    ];

    private static readonly (uint DrawFlag, uint StageFlag)[] StageFlagByDrawFlag =
    [
        (Gw2DrawFlags.SkipEffectCull, 0x10), (0x200000, 0x10000), (0x1, 0x2), (Gw2DrawFlags.NoDepthTest, 0x20), (Gw2DrawFlags.LessEqualWithoutDepthWrite, 0x40),
    ];

    public static uint Of(uint grFlags, uint drawFlags)
    {
        var stageFlags = 0u;
        foreach (var (grFlag, stageFlag) in StageFlagByGrFlag)
            if ((grFlags & grFlag) != 0) stageFlags |= stageFlag;
        foreach (var (drawFlag, stageFlag) in StageFlagByDrawFlag)
            if ((drawFlags & drawFlag) != 0) stageFlags |= stageFlag;
        return stageFlags;
    }
}

public readonly record struct Gw2AmatDrawState(Gw2DepthTest DepthTest, bool DepthWrite, Gw2ColorWrite ColorWrite, float DepthBias, bool SlopeScaledDepthBias)
{
    public const float DepthOnlyEffectBias = 65f;
    public const float NegativeBias = -65f;

    public static Gw2AmatDrawState Of(uint shaderPassFlags, int passIndex, ulong effectKey, uint drawFlags = 0, float viewDepthBias = 0f, bool viewDepthTest = true, uint stageFlags = 0) =>
        new(DepthTestOf(shaderPassFlags, passIndex, drawFlags, viewDepthTest), DepthWriteOf(shaderPassFlags, drawFlags, viewDepthTest, stageFlags), ColorWriteOf(shaderPassFlags, drawFlags), DepthBiasOf(shaderPassFlags, effectKey, drawFlags, viewDepthBias), UsesViewDepthBias(shaderPassFlags, effectKey, drawFlags, viewDepthBias));

    private static Gw2DepthTest DepthTestOf(uint shaderPassFlags, int passIndex, uint drawFlags, bool viewDepthTest)
    {
        if ((drawFlags & Gw2DrawFlags.GreaterDepthTest) != 0) return Gw2DepthTest.Greater;
        if ((drawFlags & Gw2DrawFlags.EqualDepthTest) != 0) return Gw2DepthTest.Equal;
        if ((shaderPassFlags & Gw2AmatShaderPassFlags.NoDepthTest) != 0 || (drawFlags & Gw2DrawFlags.NoDepthTest) != 0) return Gw2DepthTest.Disabled;
        if (!viewDepthTest) return Gw2DepthTest.Always;
        return passIndex == 0 || (shaderPassFlags & Gw2AmatShaderPassFlags.LessEqualOnLaterPasses) != 0 || (drawFlags & Gw2DrawFlags.LessEqualWithoutDepthWrite) != 0
            ? Gw2DepthTest.LessEqual
            : Gw2DepthTest.Equal;
    }

    private static bool DepthWriteOf(uint shaderPassFlags, uint drawFlags, bool viewDepthTest, uint stageFlags) =>
        viewDepthTest
        && StageWritesDepth(drawFlags, stageFlags)
        && (shaderPassFlags & Gw2AmatShaderPassFlags.NoDepthWrite) == 0
        && (drawFlags & Gw2DrawFlags.LessEqualWithoutDepthWrite) == 0;

    private static bool StageWritesDepth(uint drawFlags, uint stageFlags) =>
        (stageFlags & Gw2DrawStageFlags.NoDepthWrite) == 0
        || ((stageFlags & Gw2DrawStageFlags.KeepsDepthWriteForMarkedItems) != 0 && (drawFlags & Gw2DrawFlags.KeepsStageDepthWrite) == 0);

    private static Gw2ColorWrite ColorWriteOf(uint shaderPassFlags, uint drawFlags)
    {
        if ((shaderPassFlags & Gw2AmatShaderPassFlags.NoColorWrite) != 0) return Gw2ColorWrite.None;
        var rgb = (shaderPassFlags & Gw2AmatShaderPassFlags.NoRgbWrite) == 0 && (drawFlags & Gw2DrawFlags.NoRgbWrite) == 0 ? Gw2ColorWrite.Rgb : Gw2ColorWrite.None;
        var alpha = (shaderPassFlags & Gw2AmatShaderPassFlags.NoAlphaWrite) == 0 && (drawFlags & Gw2DrawFlags.NoAlphaWrite) == 0 ? Gw2ColorWrite.Alpha : Gw2ColorWrite.None;
        return rgb | alpha;
    }

    private static float DepthBiasOf(uint shaderPassFlags, ulong effectKey, uint drawFlags, float viewDepthBias)
    {
        if (IsDepthOnlyEffect(effectKey)) return DepthOnlyEffectBias;
        if (HasNegativeDepthBias(shaderPassFlags, drawFlags)) return NegativeBias;
        return viewDepthBias;
    }

    private static bool UsesViewDepthBias(uint shaderPassFlags, ulong effectKey, uint drawFlags, float viewDepthBias) =>
        !IsDepthOnlyEffect(effectKey) && !HasNegativeDepthBias(shaderPassFlags, drawFlags) && viewDepthBias != 0f;

    private static bool IsDepthOnlyEffect(ulong effectKey) =>
        effectKey is Gw2AmatEffectKeys.NormalSpecDepth or Gw2AmatEffectKeys.NormalSpecDepthAlias or Gw2AmatEffectKeys.DepthAlias;

    private static bool HasNegativeDepthBias(uint shaderPassFlags, uint drawFlags) =>
        (shaderPassFlags & Gw2AmatShaderPassFlags.NegativeDepthBias) != 0 || (drawFlags & Gw2DrawFlags.NegativeDepthBias) != 0;
}
