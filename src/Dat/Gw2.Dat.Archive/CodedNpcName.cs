namespace Gw2.Dat.Archive;

public static class CodedNpcName
{
    public const int TextIdWordBase = 0x7F00;
    public const char TextIdWordOffset = (char)0x100;
    public const char MoreWordsFlag = (char)0x8000;
    public const int MaximumValueCount = 2;

    public static bool TryToTextId(string codedName, out uint textId, out ulong? key)
    {
        textId = 0;
        key = null;
        if (string.IsNullOrEmpty(codedName)) return false;
        var values = new ulong[MaximumValueCount];
        int valueCount = 0;
        ulong value = 0;
        for (int i = 0; i < codedName.Length; i++)
        {
            char word = codedName[i];
            int digit = (word & 0x7FFF) - TextIdWordOffset;
            if (digit < 0 || digit >= TextIdWordBase) return false;
            if (value > (ulong.MaxValue - (uint)digit) / TextIdWordBase) return false;
            value = value * TextIdWordBase + (uint)digit;
            if ((word & MoreWordsFlag) != 0) continue;
            if (valueCount == MaximumValueCount) return false;
            values[valueCount++] = value;
            value = 0;
        }
        if (valueCount == 0 || codedName[codedName.Length - 1] >= MoreWordsFlag || values[0] > uint.MaxValue) return false;
        textId = (uint)values[0];
        if (valueCount == MaximumValueCount) key = values[1];
        return true;
    }
}
