namespace Gw2.Formats;

internal static class CmpApi
{
    private const int MethodBits = 4;
    private const uint DictionaryMethod = 0;
    private const uint PatchMethod = 1;

    internal static bool Decompress(Span<byte> output, ReadOnlySpan<byte> compressed) => Decompress(output, compressed, []);

    internal static bool Decompress(Span<byte> output, ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> oldSource)
    {
        if (compressed.Length > output.Length) return false;
        if (compressed.Length == output.Length)
        {
            compressed.CopyTo(output);
            return true;
        }
        var reader = new CmpBitReader(compressed);
        return reader.Read(MethodBits) switch
        {
            DictionaryMethod => CmpDict.Decompress(output, ref reader),
            PatchMethod => CmpDict.Patch(output, ref reader, oldSource),
            _ => false,
        };
    }

    internal static bool DecompressPrefix(Span<byte> output, ReadOnlySpan<byte> compressed)
    {
        var reader = new CmpBitReader(compressed);
        return reader.Read(MethodBits) == DictionaryMethod && CmpDict.Decompress(output, ref reader);
    }
}
