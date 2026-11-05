using Gw2.Formats;
namespace Gw2.Dat.Archive;

public enum Gw2Language
{
    English = 0,
    Korean = 1,
    French = 2,
    German = 3,
    Spanish = 4,
    Chinese = 5,
}

public sealed record Gw2TextLanguage(IReadOnlyList<uint> FileIds);

public sealed class Gw2TextManifest
{
    public const uint ManifestFileId = 110865;

    public Gw2TextManifest(uint stringsPerFile, IReadOnlyList<Gw2TextLanguage> languages)
    {
        StringsPerFile = stringsPerFile;
        Languages = languages;
    }

    public uint StringsPerFile { get; }
    public IReadOnlyList<Gw2TextLanguage> Languages { get; }

    public static Gw2TextManifest? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "TextPackManifest");
        if (root is null) return null;

        var languages = new List<Gw2TextLanguage>();
        if (root.Fields.GetValueOrDefault("languages") is object?[] languageObjects)
            foreach (var languageObject in languageObjects)
                if (languageObject is PackObject language)
                {
                    var fileIds = new List<uint>();
                    if (language.Fields.GetValueOrDefault("filenames") is object?[] fileObjects)
                        foreach (var fileObject in fileObjects)
                            fileIds.Add(fileObject is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0);
                    languages.Add(new(fileIds));
                }

        return new(PackValue.AsU32(root.Fields.GetValueOrDefault("stringsPerFile")), languages);
    }

    public bool TryLocate(uint textId, Gw2Language language, out uint fileId, out int indexInFile)
    {
        fileId = 0;
        indexInFile = (int)(textId % StringsPerFile);
        if ((int)language >= Languages.Count) return false;
        var fileIds = Languages[(int)language].FileIds;
        var fileNumber = textId / StringsPerFile;
        if (fileNumber >= fileIds.Count) return false;
        fileId = fileIds[(int)fileNumber];
        return fileId != 0;
    }
}

public sealed record Gw2TextPassword(uint TextId, ulong Password);

public sealed class Gw2TextPasswords
{
    public Gw2TextPasswords(uint stringCount, IReadOnlyList<Gw2TextPassword> passwords)
    {
        StringCount = stringCount;
        Passwords = passwords;
    }

    public uint StringCount { get; }
    public IReadOnlyList<Gw2TextPassword> Passwords { get; }

    public static Gw2TextPasswords? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "TextPackPasswords");
        if (root is null) return null;

        var passwords = new List<Gw2TextPassword>();
        if (root.Fields.GetValueOrDefault("passwords") is object?[] passwordObjects)
            foreach (var passwordObject in passwordObjects)
                if (passwordObject is PackObject password)
                    passwords.Add(new(
                        PackValue.AsU32(password.Fields.GetValueOrDefault("textId")),
                        PackValue.AsU64(password.Fields.GetValueOrDefault("password"))));

        return new(PackValue.AsU32(root.Fields.GetValueOrDefault("stringCount")), passwords);
    }
}

public sealed record Gw2TextVoice(uint TextId, uint VoiceId);

public sealed class Gw2TextVoices
{
    public Gw2TextVoices(IReadOnlyList<Gw2TextVoice> voices)
    {
        Voices = voices;
    }

    public IReadOnlyList<Gw2TextVoice> Voices { get; }

    public static Gw2TextVoices? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "TextPackVoices");
        if (root is null) return null;

        var voices = new List<Gw2TextVoice>();
        if (root.Fields.GetValueOrDefault("voices") is object?[] voiceObjects)
            foreach (var voiceObject in voiceObjects)
                if (voiceObject is PackObject voice)
                    voices.Add(new(
                        PackValue.AsU32(voice.Fields.GetValueOrDefault("textId")),
                        PackValue.AsU32(voice.Fields.GetValueOrDefault("voiceId"))));

        return new(voices);
    }
}
