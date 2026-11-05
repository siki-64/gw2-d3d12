using System.Buffers.Binary;

namespace Gw2.Formats;

internal ref struct CmpBitReader
{
    private const int WordBits = 32;

    private readonly ReadOnlySpan<byte> _words;
    private int _cursor;
    private uint _bitsLeft;
    private uint _rack0;
    private uint _rack1;

    internal CmpBitReader(ReadOnlySpan<byte> data)
    {
        _words = data[..(data.Length / sizeof(uint) * sizeof(uint))];
        _rack0 = TryNextWord(out uint first) ? first : 0;
        if (TryNextWord(out uint second))
        {
            _rack1 = second;
            _bitsLeft = WordBits;
        }
    }

    internal readonly uint Rack => _rack0;

    internal readonly uint Peek(int bitCount) => bitCount == 0 ? 0 : _rack0 >> (WordBits - bitCount);

    internal uint Read(int bitCount)
    {
        uint value = Peek(bitCount);
        Skip(bitCount);
        return value;
    }

    internal void Skip(int bitCount)
    {
        if (bitCount == 0) return;
        if (bitCount >= WordBits) throw new InvalidDataException("bitCount < 8 * sizeof(m_rackData0)");
        _rack0 = (_rack0 << bitCount) | (_rack1 >> (WordBits - bitCount));
        if ((uint)bitCount <= _bitsLeft)
        {
            _rack1 <<= bitCount;
            _bitsLeft -= (uint)bitCount;
            return;
        }
        if (!TryNextWord(out uint word))
        {
            _rack1 = 0;
            _bitsLeft = 0;
            return;
        }
        int missing = bitCount - (int)_bitsLeft;
        _rack0 |= word >> (WordBits - missing);
        _rack1 = word << missing;
        _bitsLeft = _bitsLeft + WordBits - (uint)bitCount;
    }

    private bool TryNextWord(out uint word)
    {
        if (_cursor == _words.Length)
        {
            word = 0;
            return false;
        }
        word = BinaryPrimitives.ReadUInt32LittleEndian(_words[_cursor..]);
        _cursor += sizeof(uint);
        return true;
    }
}
