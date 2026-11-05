namespace Gw2.Dat.Archive;

public readonly record struct Gw2CntcObjectTrailer(ulong NameStringIndex, uint NameHash);

public readonly record struct Gw2CntcObjectHeader(
    Gw2CntcContentKey Key,
    uint ContentType,
    uint DataId,
    uint TrailerOffset,
    Gw2CntcObjectTrailer? Trailer)
{
    public const int Size = 0x28;
    public const int TrailerSize = 0x20;
    private const int KeyOffset = 0x00;
    private const int ContentTypeOffset = 0x10;
    private const int DataIdOffset = 0x14;
    private const int TrailerPointerOffset = 0x18;
    private const int FullNameTrailerPointerOffset = 0x20;
    private const int TrailerNameStringIndexOffset = 0x00;
    private const int TrailerNameHashOffset = 0x08;

    public static Gw2CntcObjectHeader Read(Gw2CntcPack pack, Gw2CntcObject obj)
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (obj.Length < Size) throw new ArgumentOutOfRangeException(nameof(obj));

        var row = pack.ReadRow(obj.Offset, Size);
        var trailerOffset = row.U32(TrailerPointerOffset);
        row.U32(FullNameTrailerPointerOffset);
        return new Gw2CntcObjectHeader(
            Gw2CntcContentKey.Read(row.Bytes(KeyOffset, 16)),
            row.U32(ContentTypeOffset),
            row.U32(DataIdOffset),
            trailerOffset,
            ReadTrailer(pack, trailerOffset));
    }

    private static Gw2CntcObjectTrailer? ReadTrailer(Gw2CntcPack pack, uint trailerOffset)
    {
        if ((ulong)trailerOffset + TrailerSize > pack.ContentByteCount) return null;
        var trailer = pack.ReadRow(trailerOffset, TrailerSize);
        return new Gw2CntcObjectTrailer(trailer.U64(TrailerNameStringIndexOffset), trailer.U32(TrailerNameHashOffset));
    }
}
