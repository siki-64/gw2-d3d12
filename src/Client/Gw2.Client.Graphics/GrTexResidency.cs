using System;
using System.Collections.Generic;

namespace Gw2.Client.Graphics;

public enum GrTextureQuality
{
    High = 0,
    Medium = 1,
    Low = 2,
}

public sealed class GrTexResidency
{
    public const float HighPromoteDistance = 1080f;
    public const float DemoteHysteresis = 1.2f;
    private const float BackBufferAreaScale = 5.6266765e-7f;
    private const float MinimumBackBufferScale = 0.5f;
    private const float MaximumBackBufferScale = 4f;

    private readonly Dictionary<uint, GrTexResidencyEntry> _entries = new();

    public GrTexResidency(GrTextureQuality quality) => Quality = quality;

    public GrTextureQuality Quality { get; }

    public int Count => _entries.Count;

    public int HighResolutionCount { get; private set; }

    public static float PromoteDistance(GrTextureQuality quality, int backBufferWidth, int backBufferHeight)
    {
        var area = (float)backBufferWidth * backBufferHeight * BackBufferAreaScale;
        return HighPromoteDistance * (1 << (int)quality) * Math.Clamp(area, MinimumBackBufferScale, MaximumBackBufferScale);
    }

    public static int BaseTopMipsDropped(GrTextureQuality quality, bool hasHighResolution) =>
        (int)quality + (hasHighResolution ? 0 : 1);

    public static int HighResolutionTopMipsDropped(GrTextureQuality quality) => (int)quality;

    public void Track(uint fileId) => _entries.TryAdd(fileId, new GrTexResidencyEntry());

    public void Untrack(uint fileId)
    {
        if (_entries.Remove(fileId, out var entry) && entry.IsHighResolution) HighResolutionCount--;
    }

    public void BeginRound()
    {
        foreach (var entry in _entries.Values) entry.ReportedThisRound = false;
    }

    public void Report(uint fileId, float distance)
    {
        if (!_entries.TryGetValue(fileId, out var entry)) return;
        entry.Distance = entry.ReportedThisRound ? MathF.Min(entry.Distance, distance) : distance;
        entry.ReportedThisRound = true;
    }

    public float DistanceOf(uint fileId) => _entries.TryGetValue(fileId, out var entry) ? entry.Distance : float.PositiveInfinity;

    public bool IsHighResolution(uint fileId) => _entries.TryGetValue(fileId, out var entry) && entry.IsHighResolution;

    public void Evaluate(float promoteDistance, List<uint> promote, List<uint> demote)
    {
        var demoteDistance = promoteDistance * DemoteHysteresis;
        foreach (var (fileId, entry) in _entries)
        {
            if (entry.IsLoading || entry.UpResFailed) continue;
            if (!entry.IsHighResolution && entry.Distance <= promoteDistance)
            {
                entry.IsLoading = true;
                promote.Add(fileId);
            }
            else if (entry.IsHighResolution && entry.Distance > demoteDistance)
            {
                entry.IsHighResolution = false;
                HighResolutionCount--;
                demote.Add(fileId);
            }
        }
    }

    public void CompletePromotion(uint fileId, bool promoted)
    {
        if (!_entries.TryGetValue(fileId, out var entry) || !entry.IsLoading) return;
        entry.IsLoading = false;
        if (!promoted)
        {
            entry.UpResFailed = true;
            return;
        }
        entry.IsHighResolution = true;
        HighResolutionCount++;
    }

    private sealed class GrTexResidencyEntry
    {
        public float Distance { get; set; } = float.PositiveInfinity;

        public bool ReportedThisRound { get; set; }

        public bool IsHighResolution { get; set; }

        public bool IsLoading { get; set; }

        public bool UpResFailed { get; set; }
    }
}
