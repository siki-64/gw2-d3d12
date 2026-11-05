using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record SurveyRootType(string Chunk, ushort Version, string Root, int Chunks, int Parsed, string? FirstFailure);

public sealed record SurveyFieldCoverage(string Root, IReadOnlyList<string> Decoders, int FieldsRead, int Fields,
    IReadOnlyList<string> UnreadFields);

public sealed record SurveyFamilyCoverage(string Family, IReadOnlyList<string> Roots, IReadOnlyList<string> Decoders,
    int Fields, int FieldsRead, IReadOnlyList<string> UnreadPopulated, IReadOnlyList<string> UnreadEmpty);

public sealed record SurveyAbsentRead(string TypeName, string Field, IReadOnlyList<string> Decoders,
    IReadOnlyList<string> TypesWithField);

public static partial class CoverageReport
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<SurveyRootType> RootTypes(IEnumerable<SurveyEntry> entries) =>
        entries.Where(entry => entry.Chunks is not null)
            .SelectMany(entry => entry.Chunks!)
            .GroupBy(chunk => (chunk.Type, chunk.Version, Root: chunk.Root ?? CoverageSurvey.NoSchema))
            .Select(group => new SurveyRootType(group.Key.Type, group.Key.Version, group.Key.Root, group.Count(),
                group.Count(chunk => chunk.Parse == CoverageSurvey.ParseOk),
                group.FirstOrDefault(chunk => chunk.Parse is not (CoverageSurvey.ParseOk or CoverageSurvey.NoSchema))?.Parse))
            .OrderBy(type => type.Chunk, StringComparer.Ordinal).ThenBy(type => type.Version)
            .ToArray();

    public static SurveyFieldCoverage FieldCoverage(string root, Gw2PackSchemaCatalog catalog,
        IReadOnlyCollection<SurveyFieldRead> reads)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(catalog.Types.Where(type => type.Name == root).Select(type => type.Id));
        while (pending.TryPop(out var id))
        {
            if (!reachable.Add(id)) continue;
            foreach (var field in catalog.GetType(id).Fields)
                if (field.Type is not null) pending.Push(field.Type);
        }
        var fields = reachable.Select(catalog.GetType)
            .SelectMany(type => type.Fields.Where(field => field.Name.Length > 0).Select(field => (type.Name, field.Name)))
            .Distinct().ToArray();
        var typeNames = fields.Select(field => field.Item1).ToHashSet(StringComparer.Ordinal);
        var relevant = reads.Where(read => typeNames.Contains(read.TypeName)).ToArray();
        var read = relevant.Select(item => (item.TypeName, item.Field)).ToHashSet();
        var decoders = reads.Where(item => item.TypeName == root).Select(item => item.Decoder)
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        var unread = fields.Where(field => !read.Contains(field)).Select(field => $"{field.Item1}.{field.Item2}")
            .Order(StringComparer.Ordinal).ToArray();
        return new(root, decoders, fields.Length - unread.Length, fields.Length, unread);
    }

    public static string Family(string typeName) => VersionSuffix().Replace(typeName, "");

    [GeneratedRegex(@"V\d+$")]
    private static partial Regex VersionSuffix();

    public static IReadOnlyList<SurveyFamilyCoverage> FamilyCoverage(IEnumerable<SurveyEntry> entries,
        Gw2PackSchemaCatalog catalog, IReadOnlyCollection<SurveyFieldRead> reads,
        IReadOnlyCollection<SurveyFieldValues> values)
    {
        var read = reads.Select(item => (Family(item.TypeName), item.Field)).ToHashSet();
        var populated = values.Where(item => item.Populated > 0).Select(item => (Family(item.TypeName), item.Field)).ToHashSet();
        var result = new List<SurveyFamilyCoverage>();
        foreach (var family in RootTypes(entries).Where(type => type.Root != CoverageSurvey.NoSchema)
                     .GroupBy(type => Family(type.Root)).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var pending = new Stack<string>(family
                .SelectMany(type => catalog.FindRoots(type.Chunk, type.Version).Where(root => root.Name == type.Root))
                .Select(root => root.Id));
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            while (pending.TryPop(out var id))
            {
                if (!reachable.Add(id)) continue;
                foreach (var field in catalog.GetType(id).Fields)
                    if (field.Type is not null) pending.Push(field.Type);
            }
            var fields = reachable.Select(catalog.GetType)
                .SelectMany(type => type.Fields.Where(field => field.Name.Length > 0).Select(field => (Family(type.Name), field.Name)))
                .Distinct().ToArray();
            var unread = fields.Where(field => !read.Contains(field)).ToArray();
            string Name((string Type, string Field) field) => $"{field.Type}.{field.Field}";
            var roots = family.Select(type => type.Root).Distinct().Order(StringComparer.Ordinal).ToArray();
            var decoders = reads.Where(item => roots.Contains(item.TypeName)).Select(item => item.Decoder)
                .Distinct().Order(StringComparer.Ordinal).ToArray();
            result.Add(new(family.Key, roots, decoders, fields.Length, fields.Length - unread.Length,
                unread.Where(populated.Contains).Select(Name).Order(StringComparer.Ordinal).ToArray(),
                unread.Where(field => !populated.Contains(field)).Select(Name).Order(StringComparer.Ordinal).ToArray()));
        }
        return result;
    }

    public static string Families(IReadOnlyList<SurveyFamilyCoverage> families)
    {
        var builder = new StringBuilder();
        var fields = families.Sum(family => family.Fields);
        var read = families.Sum(family => family.FieldsRead);
        var empty = families.Sum(family => family.UnreadEmpty.Count);
        builder.AppendLine($"- Families: {families.Count}");
        builder.AppendLine($"- Fields: {fields}; read {read}; unread and never populated {empty}; open {fields - read - empty}");
        builder.AppendLine($"- Coverage of populated fields: {read} of {fields - empty} ({Percent(read, fields - empty)})");
        builder.AppendLine();
        builder.AppendLine("| Family | Fields | Read | Never populated | Open | Decoders |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var family in families.OrderByDescending(family => family.UnreadPopulated.Count).ThenBy(family => family.Family, StringComparer.Ordinal))
            builder.AppendLine($"| `{family.Family}` | {family.Fields} | {family.FieldsRead} | {family.UnreadEmpty.Count} | {family.UnreadPopulated.Count} | {string.Join(", ", family.Decoders)} |");
        builder.AppendLine();
        foreach (var family in families)
        {
            builder.AppendLine($"## {family.Family}");
            builder.AppendLine();
            builder.AppendLine($"- Roots: {string.Join(", ", family.Roots.Select(root => $"`{root}`"))}");
            if (family.UnreadPopulated.Count > 0)
                builder.AppendLine($"- Open: {string.Join(", ", family.UnreadPopulated.Select(field => $"`{field}`"))}");
            if (family.UnreadEmpty.Count > 0)
                builder.AppendLine($"- Never populated: {string.Join(", ", family.UnreadEmpty.Select(field => $"`{field}`"))}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    public static IReadOnlyList<SurveyAbsentRead> AbsentReads(Gw2PackSchemaCatalog catalog,
        IReadOnlyCollection<SurveyFieldRead> reads)
    {
        var fieldsByType = catalog.Types.GroupBy(type => type.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.SelectMany(type => type.Fields).Select(field => field.Name).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        return reads.Where(read => fieldsByType.TryGetValue(read.TypeName, out var fields) && !fields.Contains(read.Field))
            .GroupBy(read => (read.TypeName, read.Field))
            .Select(group => new SurveyAbsentRead(group.Key.TypeName, group.Key.Field,
                group.Select(read => read.Decoder).Distinct().Order(StringComparer.Ordinal).ToArray(),
                fieldsByType.Where(type => Family(type.Key) == Family(group.Key.TypeName) && type.Value.Contains(group.Key.Field))
                    .Select(type => type.Key).Order(StringComparer.Ordinal).ToArray()))
            .OrderBy(read => read.TypeName, StringComparer.Ordinal).ThenBy(read => read.Field, StringComparer.Ordinal)
            .ToArray();
    }

    public static string Absent(IReadOnlyList<SurveyAbsentRead> absent)
    {
        var builder = new StringBuilder();
        var unknown = absent.Where(read => read.TypesWithField.Count == 0).ToArray();
        builder.AppendLine($"- Lookups of a field the object's type does not have: {absent.Count}");
        builder.AppendLine($"- Of those, names no version of the family has: {unknown.Length}");
        builder.AppendLine();
        builder.AppendLine("| Type | Field | Decoders | Versions with the field |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var read in absent)
            builder.AppendLine($"| `{read.TypeName}` | `{Cell(read.Field)}` | {string.Join(", ", read.Decoders)} | {(read.TypesWithField.Count == 0 ? "none" : string.Join(", ", read.TypesWithField.Select(type => $"`{type}`")))} |");
        return builder.ToString();
    }

    private static string Percent(int part, int whole) => whole == 0 ? "0%" : $"{100.0 * part / whole:0.0}%";

    public static string Types(IEnumerable<SurveyEntry> entries) =>
        Types(entries, Gw2PackSchemaCatalog.Default, []);

    public static string Types(IEnumerable<SurveyEntry> entries, Gw2PackSchemaCatalog catalog,
        IReadOnlyCollection<SurveyFieldRead> reads)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Chunk | Version | Root type | Chunks | Parsed | First failure | Decoders | Fields read |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var type in RootTypes(entries))
        {
            var coverage = FieldCoverage(type.Root, catalog, reads);
            var fieldsRead = coverage.Fields == 0 ? "" : $"{coverage.FieldsRead} of {coverage.Fields}";
            builder.AppendLine($"| `{Cell(type.Chunk)}` | {type.Version} | `{type.Root}` | {type.Chunks} | {type.Parsed} | {Cell(type.FirstFailure ?? "")} | {string.Join(", ", coverage.Decoders)} | {fieldsRead} |");
        }
        return builder.ToString();
    }

    public static string Fields(IEnumerable<SurveyEntry> entries, Gw2PackSchemaCatalog catalog,
        IReadOnlyCollection<SurveyFieldRead> reads)
    {
        var builder = new StringBuilder();
        foreach (var root in RootTypes(entries).Select(type => type.Root).Where(root => root != CoverageSurvey.NoSchema).Distinct().Order(StringComparer.Ordinal))
        {
            var coverage = FieldCoverage(root, catalog, reads);
            builder.AppendLine($"## {root}");
            builder.AppendLine();
            builder.AppendLine($"- Decoders: {(coverage.Decoders.Count == 0 ? "none" : string.Join(", ", coverage.Decoders))}");
            builder.AppendLine($"- Fields read: {coverage.FieldsRead} of {coverage.Fields}");
            if (coverage.UnreadFields.Count > 0)
                builder.AppendLine($"- Unread: {string.Join(", ", coverage.UnreadFields.Select(field => $"`{field}`"))}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    public static string Summary(IReadOnlyList<SurveyEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Entries by kind");
        builder.AppendLine();
        Table(builder, "Kind", entries.GroupBy(entry => entry.Kind).Select(group => (group.Key, group.Count())));
        builder.AppendLine("## Compression and flags");
        builder.AppendLine();
        Table(builder, "Compression / flags", entries.Where(entry => entry.Kind is not ("unused" or "invalid"))
            .GroupBy(entry => $"{entry.Compression} / 0x{entry.Flags:X}").Select(group => (group.Key, group.Count())));
        builder.AppendLine("## Decode failures");
        builder.AppendLine();
        Table(builder, "Record", entries.Where(entry => entry.Kind == "decode-failed")
            .Select(entry => ($"{entry.Record} ({string.Join(", ", entry.FileIds)}): {Cell(entry.Decode)}", 1)));
        builder.AppendLine("## Unknown magics");
        builder.AppendLine();
        Table(builder, "Magic", entries.Where(entry => entry.Kind == "unknown")
            .GroupBy(entry => $"`{Cell(entry.Magic ?? "(short)")}`").Select(group => (group.Key, group.Count())));
        builder.AppendLine("## Pack types");
        builder.AppendLine();
        Table(builder, "Pack type", entries.Where(entry => entry.Kind == "pack")
            .GroupBy(entry => $"`{Cell(entry.PackType ?? "")}`").Select(group => (group.Key, group.Count())));
        builder.AppendLine("## Decoder results");
        builder.AppendLine();
        Table(builder, "Decoder / result", entries.Where(entry => entry.Decoders is not null).SelectMany(entry => entry.Decoders!)
            .GroupBy(decode => $"{decode.Decoder}: {Cell(decode.Result)}").Select(group => (group.Key, group.Count())));
        var roots = RootTypes(entries);
        builder.AppendLine("## Chunk parse");
        builder.AppendLine();
        builder.AppendLine($"- Root types seen: {roots.Count(type => type.Root != CoverageSurvey.NoSchema)}");
        builder.AppendLine($"- Chunk versions without schema: {roots.Count(type => type.Root == CoverageSurvey.NoSchema)}");
        builder.AppendLine($"- Root types with parse failures: {roots.Count(type => type.FirstFailure is not null)}");
        builder.AppendLine($"- Chunks parsed: {roots.Sum(type => type.Parsed)} of {roots.Sum(type => type.Chunks)}");
        return builder.ToString();
    }

    public static void Write(string directory, SurveyRun run, Gw2PackSchemaCatalog catalog)
    {
        var entries = run.Entries;
        Directory.CreateDirectory(directory);
        using (var stream = File.Create(Path.Combine(directory, "entries.json")))
            JsonSerializer.Serialize(stream, entries, Options);
        File.WriteAllText(Path.Combine(directory, "types.md"), Types(entries, catalog, run.FieldReads));
        File.WriteAllText(Path.Combine(directory, "fields.md"), Fields(entries, catalog, run.FieldReads));
        File.WriteAllText(Path.Combine(directory, "summary.md"), Summary(entries));
        File.WriteAllText(Path.Combine(directory, "absent.md"), Absent(AbsentReads(catalog, run.FieldReads)));
        if (run.FieldValues is { } values)
        {
            File.WriteAllText(Path.Combine(directory, "families.md"),
                Families(FamilyCoverage(entries, catalog, run.FieldReads, values)));
            using var stream = File.Create(Path.Combine(directory, "values.json"));
            JsonSerializer.Serialize(stream, values.OrderBy(value => value.TypeName, StringComparer.Ordinal)
                .ThenBy(value => value.Field, StringComparer.Ordinal), Options);
        }
    }

    private const int MaxTableRows = 40;

    private static void Table(StringBuilder builder, string heading, IEnumerable<(string Key, int Count)> rows)
    {
        builder.AppendLine($"| {heading} | Entries |");
        builder.AppendLine("| --- | --- |");
        var ordered = rows.OrderByDescending(row => row.Count).ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
        foreach (var (key, count) in ordered.Take(MaxTableRows))
            builder.AppendLine($"| {key} | {count} |");
        if (ordered.Length > MaxTableRows)
            builder.AppendLine($"| {ordered.Length - MaxTableRows} more | {ordered.Skip(MaxTableRows).Sum(row => row.Count)} |");
        builder.AppendLine();
    }

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\0", "\\0").Replace("\r", " ").Replace("\n", " ");
}
