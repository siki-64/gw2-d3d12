using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record SurveyDecode(string Decoder, string Result);

public static class SurveyDecoders
{
    public const string Decoded = "decoded";

    public static IReadOnlyList<SurveyDecode> Run(Gw2AssetDocument document, IReadOnlyList<Gw2PackDecoder> decoders,
        Action<string, IReadOnlyCollection<(string TypeName, string Field)>> reads)
    {
        var results = new List<SurveyDecode>();
        foreach (var decoder in decoders)
        {
            if (!decoder.Accepts(document)) continue;
            string? result = null;
            var access = PackFieldAccess.Record(() =>
            {
                try
                {
                    if (decoder.Decode(document) is not null) result = Decoded;
                }
                catch (Exception exception)
                {
                    result = $"{exception.GetType().Name}: {exception.Message}";
                }
            });
            if (access.Reads.Count > 0) reads(decoder.Name, access.Reads);
            if (result is not null) results.Add(new(decoder.Name, result));
        }
        return results;
    }
}
