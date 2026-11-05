namespace Gw2.Dat.Content;

public static class Gw2SkyCardMaterialUvSets
{
    public const int SetCount = 8;
    private static readonly float[] FullTextureRect = [0f, 1f, 0f, 1f];

    public static float[][] Of(IReadOnlyList<float[]> textureRects)
    {
        ArgumentNullException.ThrowIfNull(textureRects);
        return Enumerable.Range(0, SetCount).Select(set => set < textureRects.Count ? textureRects[set] : FullTextureRect).ToArray();
    }
}
