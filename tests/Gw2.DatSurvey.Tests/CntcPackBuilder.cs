using System.Buffers.Binary;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey.Tests;

internal static class CntcPackBuilder
{
    private const int MainOffset = 12;
    public const int FlagsOffset = MainOffset + 16;
    private const int DescriptorsOffset = FlagsOffset + 4;
    private const int IndexOffset = DescriptorsOffset + 11 * 12;
    private const int ContentOffset = IndexOffset + 16;

    public static byte[] Build(uint flags, byte[] content)
    {
        var data = new byte[ContentOffset + content.Length];
        "PF"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), MainOffset);
        "cntc"u8.CopyTo(data.AsSpan(8));
        "Main"u8.CopyTo(data.AsSpan(MainOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(MainOffset + 4), (uint)(data.Length - MainOffset - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(FlagsOffset), flags);
        for (var i = 0; i < 11; i++)
        {
            var kind = (Gw2CntcArrayKind)i;
            var (count, target) = kind switch
            {
                Gw2CntcArrayKind.IndexEntries => (1u, IndexOffset),
                Gw2CntcArrayKind.Content => ((uint)content.Length, ContentOffset),
                _ => (0u, ContentOffset),
            };
            var descriptor = DescriptorsOffset + i * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(descriptor), count);
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(descriptor + 4), target - (descriptor + 4));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(IndexOffset), 0x10);
        content.CopyTo(data.AsSpan(ContentOffset));
        return data;
    }
}
