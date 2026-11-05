using System.Globalization;
using System.Text;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public static class KeyCoverageReport
{
    public static void Write(string path, Gw2TextKeyCoverage coverage)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, Render(coverage));
    }

    public static string Render(Gw2TextKeyCoverage coverage)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Text key coverage").AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Text keys: {coverage.Keys}; keys without an encrypted string: {coverage.KeysWithoutEncryptedString}.").AppendLine();
        builder.AppendLine("| Language | Files | Strings | Plain | Encrypted | With key | Coverage |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var language in coverage.Languages)
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {language.Language} | {language.Files} | {language.Strings} | {language.Plain} | {language.Encrypted} | {language.EncryptedWithKey} | {Percent(language.EncryptedWithKey, language.Encrypted)} |");
        builder.AppendLine().AppendLine("## English files with encrypted strings").AppendLine();
        builder.AppendLine("| Text ids | Encrypted | With key | Coverage |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var file in coverage.EnglishFiles.Where(file => file.Encrypted > 0))
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| `0x{file.FirstTextId:X}`–`0x{file.LastTextId:X}` | {file.Encrypted} | {file.EncryptedWithKey} | {Percent(file.EncryptedWithKey, file.Encrypted)} |");
        return builder.ToString();
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "–" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
