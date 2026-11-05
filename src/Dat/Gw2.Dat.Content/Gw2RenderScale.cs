namespace Gw2.Dat.Content;

public static class Gw2RenderScale
{
    public const float Subsample = 0.7f;
    public const float Native = 1f;
    public const float Supersample = 1.5f;
    public const float SubsampleMinimumWidth = 800f;
    public const float SupersampleMaximumWidth = 2880f;
    private const int SubsampleQuality = 0;
    private const int SupersampleQuality = 2;
    private const int MinimumTargetSize = 4;

    public static float OfQuality(int renderSamplingQuality) => renderSamplingQuality switch
    {
        SubsampleQuality => Subsample,
        SupersampleQuality => Supersample,
        _ => Native,
    };

    public static float Factor(float scale, int backBufferWidth, int maximumTextureSize)
    {
        var minimumWidth = MathF.Min(SubsampleMinimumWidth, maximumTextureSize);
        var maximumWidth = MathF.Min(SupersampleMaximumWidth, maximumTextureSize);
        float width = backBufferWidth;
        if (scale < 1f) return width < minimumWidth ? 1f : MathF.Max(minimumWidth / width, scale);
        if (scale > 1f) return maximumWidth < width ? 1f : MathF.Min(maximumWidth / width, scale);
        return 1f;
    }

    public static (int Width, int Height) TargetSize(float scale, int backBufferWidth, int backBufferHeight, int maximumTextureSize)
    {
        var factor = Factor(scale, backBufferWidth, maximumTextureSize);
        return (Math.Max((int)MathF.Floor(backBufferWidth * factor), MinimumTargetSize), Math.Max((int)MathF.Floor(backBufferHeight * factor), MinimumTargetSize));
    }
}
