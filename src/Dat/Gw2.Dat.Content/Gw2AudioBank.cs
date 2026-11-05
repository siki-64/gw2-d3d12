using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2BankAsndFile(uint VoiceId, uint Flags, float Length, float Offset, byte[] AudioData);

public sealed class Gw2AudioBank
{
    public Gw2AudioBank(IReadOnlyList<Gw2BankAsndFile> files)
    {
        Files = files;
    }

    public IReadOnlyList<Gw2BankAsndFile> Files { get; }

    public static Gw2AudioBank? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("BankFileData");
        if (root is null) return null;

        var files = new List<Gw2BankAsndFile>();
        if (root.Fields.GetValueOrDefault("asndFile") is object?[] fileObjects)
            foreach (var fileObject in fileObjects)
                if (fileObject is PackObject file)
                    files.Add(new(
                        PackValue.AsU32(file.Fields.GetValueOrDefault("voiceId")),
                        PackValue.AsU32(file.Fields.GetValueOrDefault("flags")),
                        PackValue.AsF32(file.Fields.GetValueOrDefault("length")),
                        PackValue.AsF32(file.Fields.GetValueOrDefault("offset")),
                        PackValue.AsBytes(file.Fields.GetValueOrDefault("audioData"))));

        return new(files);
    }
}

public sealed class Gw2BankIndex
{
    public Gw2BankIndex(IReadOnlyList<IReadOnlyList<string>> languages)
    {
        Languages = languages;
    }

    public IReadOnlyList<IReadOnlyList<string>> Languages { get; }

    public static Gw2BankIndex? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("BankIndexData");
        if (root is null) return null;

        var languages = new List<IReadOnlyList<string>>();
        if (root.Fields.GetValueOrDefault("bankLanguage") is object?[] languageObjects)
            foreach (var languageObject in languageObjects)
            {
                var names = new List<string>();
                if (languageObject is PackObject language &&
                    language.Fields.GetValueOrDefault("bankFileName") is object?[] nameObjects)
                    foreach (var nameObject in nameObjects)
                        if (nameObject is PackObject entry && entry.Fields.GetValueOrDefault("fileName") is string name)
                            names.Add(name);
                languages.Add(names);
            }

        return new(languages);
    }
}
