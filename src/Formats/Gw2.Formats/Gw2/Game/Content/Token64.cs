namespace Gw2.Formats;

public static class Token64
{
    public const int LetterCount = 12;
    public const ulong LetterMask = 0x0FFFFFFFFFFFFFFFUL;
    private const int NumericSuffixShift = 60;
    private const int LetterBitCount = 5;
    private const ulong LetterBits = 0x1F;

    public static int NumericSuffix(ulong token) => (int)(token >> NumericSuffixShift);

    public static ulong WithoutNumericSuffix(ulong token) => token & LetterMask;

    public static ulong Encode(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;

        var end = name.Length;
        ulong token = 0;
        if (name.Length > 1 && char.IsDigit(name[^1]) && char.IsDigit(name[^2]))
        {
            end = name.Length - 2;
            token = ((ulong)(byte)name[^1] + (ulong)(byte)name[^2] * 10) << 60;
        }

        var bit = 0;
        for (var i = 0; i < end && bit < 60; i++, bit += 5)
        {
            var c = name[i];
            ulong value = c is >= 'a' and <= 'z' ? (ulong)(c - 'a' + 1)
                : c is >= 'A' and <= 'Z' ? (ulong)(c - 'A' + 1)
                : 0;
            token |= value << bit;
        }

        return token;
    }

    public static ulong EncodeAfterColon(string name)
    {
        var colon = name.IndexOf(':');
        return Encode(colon >= 0 ? name[(colon + 1)..] : name);
    }

    public static ulong FillEmptyLetters(ulong token, char first, char second)
    {
        if ((token & LetterBits) == 0) token = (token & ~LetterBits) | LetterValue(first);
        if (((token >> LetterBitCount) & LetterBits) == 0) token = (token & ~(LetterBits << LetterBitCount)) | (LetterValue(second) << LetterBitCount);
        return token;
    }

    private static ulong LetterValue(char letter) =>
        letter is >= 'a' and <= 'z' ? (ulong)(letter - 'a' + 1)
        : letter is >= 'A' and <= 'Z' ? (ulong)(letter - 'A' + 1)
        : 0;

    public static string Decode(ulong token)
    {
        if (token == 0) return string.Empty;

        Span<char> letters = stackalloc char[LetterCount];
        for (var i = 0; i < LetterCount; i++)
        {
            var value = (int)((token >> (i * 5)) & 0x1F);
            letters[i] = value == 0 ? '_' : (char)('a' + value - 1);
        }

        return new string(letters).TrimEnd('_');
    }
}
