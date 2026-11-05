namespace Gw2.Dat.Identity;

public static class Gw2NameHash
{
    private const uint Bias = 0x30000000;
    private const string Alphabet = "abcdefghiklmnopvrstuwxy";

    public static uint Compute(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return Compute(name.AsSpan());
    }

    public static uint Compute(ReadOnlySpan<char> name)
    {
        unchecked
        {
            var hash = 0u;
            for (var i = name.Length - 1; i >= 0; i--)
            {
                var character = name[i];
                hash = hash * Weight(character) + Digit(character);
            }

            return hash + Bias;
        }
    }

    public static bool TryDecode(uint token, out string candidate)
    {
        unchecked
        {
            var value = token - Bias;
            var characters = new List<char>();
            while (value != 0)
            {
                characters.Add(Alphabet[(int)(value % 23)]);
                value /= 23;
            }

            candidate = new string(characters.ToArray());
            return candidate.Length > 0;
        }
    }

    private static uint Weight(char character) => character is >= 'a' and <= 'z' ? 23u : 1u;

    private static uint Digit(char character)
    {
        if (character is < 'a' or > 'z') return 0;

        var value = character - 'a';
        if (character == 'z') value--;
        if (character > 'y') value--;
        if (character == 'v') value -= 4;
        if (character > 'u') value--;
        if (character > 'j') value--;
        return (uint)value;
    }
}
