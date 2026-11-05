using System;
using System.Buffers.Binary;

namespace Gw2.Formats;

public sealed record CameraPreferences(
    float Distance,
    int ZoomSpeedOption,
    int FieldOfViewDegrees,
    int FieldOfViewFlare,
    float RotationSpeedPercent,
    int HorizontalShift,
    int VerticalShift,
    int VerticalShiftNear,
    int CollisionSensitivity,
    bool SnapsCloseToTheCharacter)
{
    public static CameraPreferences Defaults { get; } = new(
        CameraPreferenceChunk.MaximumDistance, CameraPreferenceChunk.DefaultZoomSpeedOption, CameraPreferenceChunk.DefaultFieldOfViewDegrees,
        CameraPreferenceChunk.DefaultFieldOfViewFlare, CameraPreferenceChunk.DefaultRotationSpeedPercent, 0,
        CameraPreferenceChunk.DefaultVerticalShift, CameraPreferenceChunk.DefaultVerticalShiftNear, CameraPreferenceChunk.DefaultCollisionSensitivity, false);
}

public static class CameraPreferenceChunk
{
    public const uint ChunkMagic = 0x63617875;
    public const int DefaultCollisionSensitivity = 1;
    public const int MaximumCollisionSensitivity = 9;
    public const float MinimumDistance = 100f;
    public const float MaximumDistance = 600f;
    public const int DefaultZoomSpeedOption = 6;
    public const int MaximumZoomSpeedOption = 6;
    public const int MinimumFieldOfViewDegrees = 25;
    public const int MaximumFieldOfViewDegrees = 70;
    public const int DefaultFieldOfViewDegrees = 50;
    public const int MaximumFieldOfViewFlare = 60;
    public const int DefaultFieldOfViewFlare = 30;
    public const float MinimumRotationSpeedPercent = 50f;
    public const float DefaultRotationSpeedPercent = 100f;
    public const int MaximumShift = 20;
    public const int MaximumVerticalShift = 40;
    public const int DefaultVerticalShift = 20;
    public const int DefaultVerticalShiftNear = 24;

    private const float DistancePerStep = 0.0076293945f;
    private const float RotationPercentPerStep = 0.5859375f;
    private const int ChunkHeaderSizeOffset = 10;
    private const int DistanceOffset = 13;
    private const int ZoomSpeedOffset = 15;
    private const int FieldOfViewOffset = 16;
    private const int FieldOfViewFlareOffset = 17;
    private const int RotationSpeedOffset = 18;
    private const int HorizontalShiftOffset = 19;
    private const int VerticalShiftOffset = 20;
    private const int VerticalShiftNearOffset = 21;
    private const int CollisionSensitivityOffset = 22;
    private const int PackedOptionByteCountOffset = 0;
    private const int PackedOptionBitsPointerOffset = 4;
    private const int CameraSnapsCloseToTheCharacterPackedBit = 48;

    public static CameraPreferences Read(ReadOnlySpan<byte> preferencePack)
    {
        var data = ChunkData(preferencePack, out _);
        if (data.Length <= CollisionSensitivityOffset) return CameraPreferences.Defaults;
        return new CameraPreferences(
            MinimumDistance + BinaryPrimitives.ReadUInt16LittleEndian(data[DistanceOffset..]) * DistancePerStep,
            MaximumZoomSpeedOption - Math.Min((int)data[ZoomSpeedOffset], MaximumZoomSpeedOption),
            Math.Clamp((int)data[FieldOfViewOffset], MinimumFieldOfViewDegrees, MaximumFieldOfViewDegrees),
            Math.Min((int)data[FieldOfViewFlareOffset], MaximumFieldOfViewFlare),
            MinimumRotationSpeedPercent + data[RotationSpeedOffset] * RotationPercentPerStep,
            Math.Min((int)data[HorizontalShiftOffset], 2 * MaximumShift) - MaximumShift,
            Math.Min((int)data[VerticalShiftOffset], MaximumVerticalShift),
            Math.Min((int)data[VerticalShiftNearOffset], MaximumVerticalShift),
            data[CollisionSensitivityOffset] <= MaximumCollisionSensitivity ? data[CollisionSensitivityOffset] : DefaultCollisionSensitivity,
            CameraSnapsCloseToTheCharacter(preferencePack));
    }

    public static byte[] ReplaceIn(ReadOnlySpan<byte> preferencePack, CameraPreferences preferences)
    {
        var replaced = preferencePack.ToArray();
        var data = ChunkData(replaced, out var dataStart);
        if (data.Length <= CollisionSensitivityOffset) return replaced;
        var target = replaced.AsSpan(dataStart, data.Length);
        var distanceSteps = Math.Round((Math.Clamp(preferences.Distance, MinimumDistance, MaximumDistance) - MinimumDistance) / DistancePerStep);
        BinaryPrimitives.WriteUInt16LittleEndian(target[DistanceOffset..], (ushort)Math.Min(distanceSteps, ushort.MaxValue));
        target[ZoomSpeedOffset] = (byte)(MaximumZoomSpeedOption - Math.Clamp(preferences.ZoomSpeedOption, 0, MaximumZoomSpeedOption));
        target[FieldOfViewOffset] = (byte)Math.Clamp(preferences.FieldOfViewDegrees, MinimumFieldOfViewDegrees, MaximumFieldOfViewDegrees);
        target[FieldOfViewFlareOffset] = (byte)Math.Clamp(preferences.FieldOfViewFlare, 0, MaximumFieldOfViewFlare);
        target[RotationSpeedOffset] = (byte)Math.Clamp(Math.Round((preferences.RotationSpeedPercent - MinimumRotationSpeedPercent) / RotationPercentPerStep), 0, byte.MaxValue);
        target[HorizontalShiftOffset] = (byte)(Math.Clamp(preferences.HorizontalShift, -MaximumShift, MaximumShift) + MaximumShift);
        target[VerticalShiftOffset] = (byte)Math.Clamp(preferences.VerticalShift, 0, MaximumVerticalShift);
        target[VerticalShiftNearOffset] = (byte)Math.Clamp(preferences.VerticalShiftNear, 0, MaximumVerticalShift);
        target[CollisionSensitivityOffset] = (byte)Math.Clamp(preferences.CollisionSensitivity, 0, MaximumCollisionSensitivity);
        if (SnapBitIndex(target) is { } snapByte)
        {
            var mask = (byte)(1 << (CameraSnapsCloseToTheCharacterPackedBit % 8));
            target[snapByte] = preferences.SnapsCloseToTheCharacter ? (byte)(target[snapByte] | mask) : (byte)(target[snapByte] & ~mask);
        }
        return replaced;
    }

    public static bool CameraSnapsCloseToTheCharacter(ReadOnlySpan<byte> preferencePack)
    {
        var data = ChunkData(preferencePack, out _);
        return SnapBitIndex(data) is { } snapByte && (data[snapByte] & (1 << (CameraSnapsCloseToTheCharacterPackedBit % 8))) != 0;
    }

    public static int CollisionSensitivityOf(ReadOnlySpan<byte> preferencePack) => Read(preferencePack).CollisionSensitivity;

    private static int? SnapBitIndex(ReadOnlySpan<byte> data)
    {
        if (data.Length < PackedOptionBitsPointerOffset + sizeof(ulong)) return null;
        var byteCount = BinaryPrimitives.ReadUInt32LittleEndian(data[PackedOptionByteCountOffset..]);
        var bitsStart = PackedOptionBitsPointerOffset + (long)BinaryPrimitives.ReadUInt64LittleEndian(data[PackedOptionBitsPointerOffset..]);
        var byteIndex = CameraSnapsCloseToTheCharacterPackedBit / 8;
        if (byteIndex >= byteCount || bitsStart + byteIndex >= data.Length) return null;
        return (int)(bitsStart + byteIndex);
    }

    private static ReadOnlySpan<byte> ChunkData(ReadOnlySpan<byte> preferencePack, out int dataStart)
    {
        dataStart = 0;
        if (AccountPreferencePack.FindChunk(preferencePack, ChunkMagic) is not var (offset, length)) return default;
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(preferencePack[(offset + ChunkHeaderSizeOffset)..]);
        dataStart = offset + headerSize;
        return preferencePack.Slice(dataStart, length - headerSize);
    }
}
