using System;
using System.Collections.Generic;
using System.Linq;

namespace Gw2.Dat.Archive;

public sealed record Gw2TextKeyCoverageLanguage(
    Gw2Language Language,
    int Files,
    int Strings,
    int Plain,
    int Encrypted,
    int EncryptedWithKey);

public sealed record Gw2TextKeyCoverage(
    IReadOnlyList<Gw2TextKeyCoverageLanguage> Languages,
    int Keys,
    int KeysWithoutEncryptedString,
    IReadOnlyList<(uint FirstTextId, uint LastTextId, int Encrypted, int EncryptedWithKey)> EnglishFiles)
{
    public static Gw2TextKeyCoverage Measure(Gw2TextManifest manifest, Func<uint, byte[]?> readFile, Gw2KeyStore keys)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(keys);

        var languages = new List<Gw2TextKeyCoverageLanguage>();
        var englishFiles = new List<(uint, uint, int, int)>();
        var encryptedTextIds = new HashSet<uint>();
        for (var languageIndex = 0; languageIndex < manifest.Languages.Count; languageIndex++)
        {
            var language = (Gw2Language)languageIndex;
            var fileIds = manifest.Languages[languageIndex].FileIds;
            int files = 0, strings = 0, plain = 0, encrypted = 0, encryptedWithKey = 0;
            for (var fileIndex = 0; fileIndex < fileIds.Count; fileIndex++)
            {
                if (fileIds[fileIndex] == 0 || readFile(fileIds[fileIndex]) is not { } bytes)
                    continue;
                files++;
                var firstTextId = (uint)fileIndex * manifest.StringsPerFile;
                var entries = Gw2TextEntry.ReadFile(bytes);
                int fileEncrypted = 0, fileEncryptedWithKey = 0;
                for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    if (entry.Payload.IsEmpty)
                        continue;
                    strings++;
                    if (!entry.IsEncrypted)
                    {
                        plain++;
                        continue;
                    }
                    var textId = firstTextId + (uint)entryIndex;
                    encryptedTextIds.Add(textId);
                    fileEncrypted++;
                    if (keys.FindTextKey(textId) is not null)
                        fileEncryptedWithKey++;
                }
                encrypted += fileEncrypted;
                encryptedWithKey += fileEncryptedWithKey;
                if (language == Gw2Language.English)
                    englishFiles.Add((firstTextId, firstTextId + manifest.StringsPerFile - 1, fileEncrypted, fileEncryptedWithKey));
            }
            languages.Add(new(language, files, strings, plain, encrypted, encryptedWithKey));
        }

        var keyCount = keys.Count(Gw2KeyStore.TextKind);
        var keysWithoutEncryptedString = keys.Ids(Gw2KeyStore.TextKind).Count(textId => !encryptedTextIds.Contains(textId));
        return new(languages, keyCount, keysWithoutEncryptedString, englishFiles);
    }
}
