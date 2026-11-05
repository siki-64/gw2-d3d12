namespace Gw2.Formats;

internal static class CmpDict
{
    private const int MatchLengthBits = 4;
    private const int BlockSymbolBits = 4;
    private const int BlockSymbolShift = 12;
    private const uint FirstMatchSymbol = 0x100;
    private const uint FirstSourceCopySymbol = 0x11D;
    private const uint SourceDistanceFarSymbol = 0x3C;
    private const uint SourceDistanceExtraBitsEnd = 0x7C;
    private const uint SourceJumpSlack = 0xFF;

    internal static bool Decompress(Span<byte> output, ref CmpBitReader reader)
    {
        uint minimumMatch = reader.Read(MatchLengthBits) + 1;
        var literals = new CmpHuff();
        var distances = new CmpHuff();
        int written = 0;
        while (written != output.Length)
        {
            literals.BuildTables(ref reader);
            distances.BuildTables(ref reader);
            uint blockSymbols = (reader.Read(BlockSymbolBits) + 1) << BlockSymbolShift;
            for (; blockSymbols != 0 && written != output.Length; blockSymbols--)
            {
                uint symbol = literals.ReadSymbol(ref reader);
                if (symbol < FirstMatchSymbol)
                {
                    output[written++] = (byte)symbol;
                    continue;
                }
                if (!TryReadLength(ref reader, symbol - FirstMatchSymbol, minimumMatch, out uint length)) return false;
                if (!TryReadDistance(ref reader, distances.ReadSymbol(ref reader), out uint distance)) return false;
                if (!CopyBack(output, ref written, length, distance)) return false;
            }
        }
        return true;
    }

    internal static bool Patch(Span<byte> output, ref CmpBitReader reader, ReadOnlySpan<byte> oldSource)
    {
        uint minimumMatch = reader.Read(MatchLengthBits) + 1;
        uint minimumSourceCopy = reader.Read(MatchLengthBits) + 1;
        var literals = new CmpHuff();
        var distances = new CmpHuff();
        var sourceDistances = new CmpHuff();
        uint sourcePredictor = 0;
        uint previousSourcePosition = 0;
        int written = 0;
        while (written != output.Length)
        {
            literals.BuildTables(ref reader);
            distances.BuildTables(ref reader);
            sourceDistances.BuildTables(ref reader);
            uint blockSymbols = (reader.Read(BlockSymbolBits) + 1) << BlockSymbolShift;
            for (; blockSymbols != 0 && written != output.Length; blockSymbols--)
            {
                uint symbol = literals.ReadSymbol(ref reader);
                if (symbol < FirstMatchSymbol)
                {
                    output[written++] = (byte)symbol;
                    sourcePredictor = unchecked(sourcePredictor + 1);
                    continue;
                }
                if (symbol < FirstSourceCopySymbol)
                {
                    if (!TryReadLength(ref reader, symbol - FirstMatchSymbol, minimumMatch, out uint length)) return false;
                    if (!TryReadDistance(ref reader, distances.ReadSymbol(ref reader), out uint distance)) return false;
                    if (!CopyBack(output, ref written, length, distance)) return false;
                    sourcePredictor = unchecked(sourcePredictor + length);
                    continue;
                }

                if (!TryReadLength(ref reader, symbol - FirstSourceCopySymbol, minimumSourceCopy, out uint sourceLength)) return false;
                uint sourceDistanceSymbol = sourceDistances.ReadSymbol(ref reader);
                if (sourceDistanceSymbol >= CmpTables.SourceDistanceSymbolCount) return false;
                uint sourceDistance = ReadSourceDistance(ref reader, sourceDistanceSymbol);
                uint sourcePosition = unchecked((sourceDistanceSymbol & 1) == 0
                    ? sourcePredictor + sourceDistance
                    : sourcePredictor - sourceDistance);
                uint sourceEnd = unchecked(sourcePosition + sourceLength);
                if (sourceLength > (uint)(output.Length - written)) return false;
                if (sourceEnd > (uint)oldSource.Length || sourceEnd < sourcePosition) return false;

                uint jumpThreshold = minimumSourceCopy + SourceJumpSlack;
                bool jumpedFar = unchecked(sourcePosition - previousSourcePosition) > jumpThreshold
                    && unchecked(previousSourcePosition - sourcePosition) > jumpThreshold;
                sourcePredictor = jumpedFar ? unchecked(sourcePredictor + sourceLength) : sourceEnd;
                previousSourcePosition = sourcePosition;

                oldSource.Slice((int)sourcePosition, (int)sourceLength).CopyTo(output[written..]);
                written += (int)sourceLength;
            }
        }
        return true;
    }

    private static bool TryReadLength(ref CmpBitReader reader, uint lengthSymbol, uint minimum, out uint length)
    {
        length = 0;
        if (lengthSymbol >= (uint)CmpTables.LengthBases.Length) return false;
        length = (CmpTables.LengthBases[(int)lengthSymbol] | reader.Read(CmpTables.LengthExtraBits[(int)lengthSymbol])) + minimum;
        return true;
    }

    private static bool TryReadDistance(ref CmpBitReader reader, uint distanceSymbol, out uint distance)
    {
        distance = 0;
        if (distanceSymbol >= (uint)CmpTables.DistanceBases.Length) return false;
        distance = CmpTables.DistanceBases[(int)distanceSymbol] | reader.Read(CmpTables.DistanceExtraBits[(int)distanceSymbol]);
        return true;
    }

    private static uint ReadSourceDistance(ref CmpBitReader reader, uint symbol)
    {
        uint distance = symbol < SourceDistanceFarSymbol
            ? CmpTables.DistanceBases[(int)(symbol >> 1)]
            : unchecked(((symbol & 2) + 4) << (int)((symbol >> 2) - 2));
        int extraBits = symbol < 8 || symbol >= SourceDistanceExtraBitsEnd ? 0 : (int)(symbol / 4 - 1);
        return unchecked(distance + reader.Read(extraBits));
    }

    private static bool CopyBack(Span<byte> output, ref int written, uint length, uint distance)
    {
        if (length > (uint)(output.Length - written) || distance >= (uint)written) return false;
        int from = written - (int)distance - 1;
        for (uint index = 0; index < length; index++) output[written++] = output[from++];
        return true;
    }
}
