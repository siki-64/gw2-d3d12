namespace Gw2.Dat.Content;

public interface IGw2NamedContent
{
    uint NameTextId { get; }
}

public static class Gw2NamedContentExtensions
{
    public static (Gw2TextStatus Status, string Text) ReadName(this IGw2NamedContent content, Gw2ContentReader reader, Gw2Language language) =>
        reader.ReadKnownText(content.NameTextId, language);
}
