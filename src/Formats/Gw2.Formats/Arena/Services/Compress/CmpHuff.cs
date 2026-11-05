namespace Gw2.Formats;

internal sealed class CmpHuff
{
    private const int HashBits = 8;
    private const int HashSize = 1 << HashBits;
    private const int MaximumBitCount = 31;
    private const int MaximumRows = MaximumBitCount - HashBits;
    private const uint NoSymbol = uint.MaxValue;
    private const uint LongCode = uint.MaxValue;
    private const int SymbolCountBits = 16;

    private readonly uint[] _hashBitCounts = new uint[HashSize];
    private readonly uint[] _hashSymbols = new uint[HashSize];
    private readonly uint[] _rowCodes = new uint[MaximumRows];
    private readonly uint[] _rowLastSymbolIndexes = new uint[MaximumRows];
    private readonly uint[] _rowBitCounts = new uint[MaximumRows];
    private readonly List<uint> _longSymbols = [];
    private int _rowCount;

    internal void BuildTables(ref CmpBitReader reader)
    {
        Clear();
        uint symbolCount = reader.Read(SymbolCountBits);
        Span<uint> heads = stackalloc uint[MaximumBitCount + 1];
        heads.Fill(NoSymbol);
        var next = new uint[symbolCount];
        uint usedSymbols = 0;

        long symbol = (long)symbolCount - 1;
        while (symbol >= 0)
        {
            uint meta = ReadMeta(ref reader);
            uint repeat = meta >> 5;
            uint bitCount = meta & 0x1F;
            if (repeat > symbol)
            {
                Clear();
                return;
            }
            if (bitCount == 0 && symbolCount >= 2)
            {
                symbol -= repeat + 1;
                continue;
            }
            usedSymbols += repeat + 1;
            for (uint index = 0; index <= repeat; index++)
            {
                next[symbol] = heads[(int)bitCount];
                heads[(int)bitCount] = (uint)symbol;
                symbol--;
            }
        }

        if (symbolCount != 0 && usedSymbols == 0)
        {
            next[symbolCount - 1] = heads[0];
            heads[0] = symbolCount - 1;
            usedSymbols = 1;
        }

        uint code = 0;
        uint processed = 0;
        int length = 0;
        for (; length <= HashBits; length++)
        {
            for (uint current = heads[length]; current != NoSymbol; current = next[current])
            {
                if (code >= 1u << length || current >= symbolCount)
                {
                    Clear();
                    return;
                }
                int fill = 1 << (HashBits - length);
                for (int low = 0; low < fill; low++)
                {
                    uint slot = (code << (HashBits - length)) | (uint)low;
                    _hashBitCounts[slot] = (uint)length;
                    _hashSymbols[slot] = current;
                }
                processed++;
                code = unchecked(code - 1);
            }
            code = unchecked(code * 2 + 1);
        }

        if (processed == usedSymbols) return;

        for (; length <= MaximumBitCount; length++)
        {
            uint current = heads[length];
            if (current != NoSymbol)
            {
                while (true)
                {
                    if (code >= 1u << length || current >= symbolCount)
                    {
                        Clear();
                        return;
                    }
                    _hashBitCounts[code >> (length - HashBits)] = LongCode;
                    uint lastSymbolIndex = (uint)_longSymbols.Count;
                    uint lastCode = code;
                    _longSymbols.Add(current);
                    code = unchecked(code - 1);
                    current = next[current];
                    if (current == NoSymbol)
                    {
                        _rowCodes[_rowCount] = lastCode << (32 - length);
                        _rowLastSymbolIndexes[_rowCount] = lastSymbolIndex;
                        _rowBitCounts[_rowCount] = (uint)length;
                        _rowCount++;
                        break;
                    }
                }
            }
            code = unchecked(code * 2 + 1);
        }
    }

    internal uint ReadSymbol(ref CmpBitReader reader)
    {
        uint rack = reader.Rack;
        uint slot = rack >> (32 - HashBits);
        uint bitCount = _hashBitCounts[slot];
        uint symbol = _hashSymbols[slot];
        if (bitCount == LongCode)
        {
            int row = 0;
            while (row < _rowCount && rack < _rowCodes[row]) row++;
            if (row == _rowCount) throw new InvalidDataException("CmpHuff: code has no row");
            bitCount = _rowBitCounts[row];
            long index = _rowLastSymbolIndexes[row] - (long)((rack - _rowCodes[row]) >> (32 - (int)bitCount));
            if ((ulong)index >= (ulong)_longSymbols.Count) throw new InvalidDataException("CmpHuff: symbol index out of range");
            symbol = _longSymbols[(int)index];
        }
        reader.Skip((int)bitCount);
        return symbol;
    }

    private static uint ReadMeta(ref CmpBitReader reader)
    {
        uint rack = reader.Rack;
        int row = 0;
        while (rack < CmpTables.MetaRowCodes[row]) row++;
        int bitCount = row + CmpTables.MetaRowFirstBitCount;
        int index = CmpTables.MetaRowLastIndexes[row] - (int)((rack - CmpTables.MetaRowCodes[row]) >> (32 - bitCount));
        reader.Skip(bitCount);
        return CmpTables.MetaValues[index];
    }

    private void Clear()
    {
        Array.Clear(_hashBitCounts);
        Array.Clear(_hashSymbols);
        _longSymbols.Clear();
        _rowCount = 0;
    }
}
