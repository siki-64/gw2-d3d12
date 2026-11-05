using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gw2.Client.Settings;

public sealed record Gw2ContentOptions(string DatPath, string? LocalDatPath, uint? EnvironmentMapFileId, string RaceSexName, uint? MapId)
{
    public IReadOnlyList<Gw2AttachmentOption> Attachments { get; init; } = [];
    public IReadOnlyList<Gw2SkinAttachmentOption> SkinAttachments { get; init; } = [];
}

public sealed record Gw2SkinAttachmentOption(uint SkinId, bool Stowed)
{
    private const string StowedSuffix = "stowed";

    public static Gw2SkinAttachmentOption Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length > 2 || (parts.Length == 2 && parts[1] != StowedSuffix)) throw new FormatException($"Skin attachment '{text}' is not skin[:stowed].");
        return new Gw2SkinAttachmentOption(uint.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length == 2);
    }
}

public sealed record Gw2AttachmentOption(uint ModelFileId, string SocketBoneName, string? GripBoneName)
{
    public static Gw2AttachmentOption Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3) throw new FormatException($"Attachment '{text}' is not model:socket[:grip].");
        return new Gw2AttachmentOption(uint.Parse(parts[0], CultureInfo.InvariantCulture), parts[1], parts.Length == 3 ? parts[2] : null);
    }
}
