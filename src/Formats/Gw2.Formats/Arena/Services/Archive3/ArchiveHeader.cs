namespace Gw2.Formats;

public readonly record struct ArchiveHeader(
    byte Version,
    uint ChunkSize,
    ulong MftOffset,
    uint MftSize,
    uint MftEntryCount);
