namespace Gw2.Formats;

internal static class Token
{
    private const uint Bias = 0x30000000;
    private const uint Radix = 23;
    private const string Alphabet = "abcdefghiklmnopvrstuwxy";

    internal static uint Encode(ReadOnlySpan<char> name)
    {
        unchecked
        {
            uint token = 0;
            for (int index = name.Length - 1; index >= 0; index--)
            {
                char character = name[index];
                token = token * Weight(character) + Digit(character);
            }
            return token + Bias;
        }
    }

    internal static bool TryDecode(uint token, out string name)
    {
        unchecked
        {
            uint value = token - Bias;
            var characters = new List<char>();
            while (value != 0)
            {
                characters.Add(Alphabet[(int)(value % Radix)]);
                value /= Radix;
            }
            name = new string(characters.ToArray());
            return name.Length > 0;
        }
    }

    private static uint Weight(char character) => character is >= 'a' and <= 'z' ? Radix : 1u;

    private static uint Digit(char character)
    {
        if (character is < 'a' or > 'z') return 0;
        int value = character - 'a';
        if (character == 'z') value--;
        if (character > 'y') value--;
        if (character == 'v') value -= 4;
        if (character > 'u') value--;
        if (character > 'j') value--;
        return (uint)value;
    }
}
