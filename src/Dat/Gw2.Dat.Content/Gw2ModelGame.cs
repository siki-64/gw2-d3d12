using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2ModelGame
{
    public Gw2ModelGame(byte[] gameData)
    {
        GameData = gameData;
    }

    public byte[] GameData { get; }

    public static Gw2ModelGame? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "ModelFileGame");
        if (root is null) return null;

        return new(PackValue.AsBytes(root.Fields.GetValueOrDefault("gameData")));
    }
}
