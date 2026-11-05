using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed class Gw2ConfigNumbers(Gw2ContentIndex index)
{
    public const uint ConfigContentType = 150;
    private const int ValueKindOffset = 48;
    private const int ValueOffset = 56;
    private const uint NumberValueKind = 437;

    public static readonly Guid HolosmithOverheatPercentKey = KeyOf(-220945571, 1122902247, -906932053, 1207391853);
    public static readonly Guid HolosmithHeatPenaltyKey = KeyOf(-2128638209, 1166524800, 688546748, 1290358315);
    public static readonly Guid HolosmithHeatBonusKey = KeyOf(148719413, 1291189523, 1557911710, 1699744838);

    public float? NumberOf(Guid contentKey) =>
        index.FindByContentKey(ConfigContentType, contentKey) is { } config && index.ReadUInt32(config, ValueKindOffset) == NumberValueKind
            ? index.ReadSingle(config, ValueOffset)
            : null;

    private static Guid KeyOf(int first, int second, int third, int fourth)
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), first);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), second);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), third);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), fourth);
        return new Guid(bytes);
    }
}
