using System.Security.Cryptography;
using System.Text;

namespace Gw2.Formats;

internal static class NameHash
{
    private const int MangledPartLength = 5;
    private const ulong FnvOffsetBasis = 0xCBF29CE484222325;
    private const ulong FnvPrime = 0x100000001B3;
    private const uint HashSeed = 844963502;
    private const int HashMixSeed = -490644323;
    private const uint HashAccumulatorSeed = 561029770;

    private static readonly uint[] Table =
    [
        0x92B9A528, 0x25D4FC88, 0xEDCBEFB8, 0x51063A80, 0x91341C61, 0x0261229D, 0x726F48ED, 0xCE1C088C,
        0x76253EB5, 0x31E3A0DE, 0xA2AAD215, 0xCA7D6D27, 0xA5F98970, 0x0541C365, 0x3C14FF04, 0x5056AF4F,
    ];

    internal static uint OfFullName(string fullName) => Compute(Mangle(fullName));

    internal static string Mangle(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        int lastDot = fullName.LastIndexOf('.');
        return lastDot < 0
            ? ManglePart(fullName)
            : ManglePart(fullName[..lastDot]) + "." + ManglePart(fullName[(lastDot + 1)..]);
    }

    internal static uint Compute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        unchecked
        {
            int mix = HashMixSeed;
            uint accumulator = HashAccumulatorSeed;
            uint hash = HashSeed;
            foreach (char character in name)
            {
                if (character == '.') continue;
                mix = character ^ (8 * mix);
                accumulator += Table[mix & 0xF];
                hash ^= accumulator + (uint)mix;
            }
            return hash != 0 ? hash : HashSeed;
        }
    }

    private static string ManglePart(string part)
    {
        byte[] digest = SHA256.HashData(Encoding.Unicode.GetBytes(part));
        ulong folded = FnvOffsetBasis;
        unchecked
        {
            for (int offset = 0; offset < digest.Length; offset += 4)
            {
                uint word = BitConverter.ToUInt32(digest, offset);
                for (int shift = 0; shift < 32; shift += 8)
                    folded = (folded ^ (word >> shift)) * FnvPrime;
            }
        }
        return Convert.ToBase64String(BitConverter.GetBytes(folded))[..MangledPartLength];
    }
}
