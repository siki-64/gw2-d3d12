using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2EulaLanguage(uint Language, string Text);

public sealed class Gw2Eula
{
    public Gw2Eula(IReadOnlyList<Gw2EulaLanguage> languages, uint version)
    {
        Languages = languages;
        Version = version;
    }

    public IReadOnlyList<Gw2EulaLanguage> Languages { get; }
    public uint Version { get; }

    public static Gw2Eula? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackEula");
        if (root is null) return null;

        var languages = new List<Gw2EulaLanguage>();
        if (root.Fields.GetValueOrDefault("Language") is object?[] languageObjects)
            foreach (var languageObject in languageObjects)
                if (languageObject is PackObject language)
                    languages.Add(new(
                        PackValue.AsU32(language.Fields.GetValueOrDefault("Language")),
                        language.Fields.GetValueOrDefault("Text") as string ?? string.Empty));

        return new(languages, PackValue.AsU32(root.Fields.GetValueOrDefault("Version")));
    }
}
