namespace Gw2.Dat.Content;

public static class Gw2BoneScaleFile
{
    public static Gw2CompositeBoneScale? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Chunks
            .Where(chunk => chunk.Chunk.Type == "scal")
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName.StartsWith("PackCompositeBoneScale", StringComparison.Ordinal));
        return root is null ? null : Gw2Composite.BoneScale(root);
    }
}
