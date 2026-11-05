using System;
using Crystal.Graphics;

namespace Gw2.Client.Debug;

public sealed class MemoryUsageSampler
{
    private const long SampleIntervalMilliseconds = 1000;

    private long _nextSampleTick;

    public void Sample(GraphicsDevice graphicsDevice, int cachedContentFiles)
    {
        var now = Environment.TickCount64;
        if (now < _nextSampleTick) return;
        _nextSampleTick = now + SampleIntervalMilliseconds;
        var videoMemory = graphicsDevice.GetVideoMemoryInfo();
        Gw2MemoryUsage.GpuLocalBytes = videoMemory.LocalUsage;
        Gw2MemoryUsage.GpuNonLocalBytes = videoMemory.NonLocalUsage;
        Gw2MemoryUsage.UploadRingPages = graphicsDevice.AllUploadRingPageCount;
        Gw2MemoryUsage.CommandLists = graphicsDevice.CommandListCount;
        Gw2MemoryUsage.CopyStagingPages = graphicsDevice.CopyStagingPageCount;
        Gw2MemoryUsage.CopyCommandLists = graphicsDevice.CopyCommandListCount;
        Gw2MemoryUsage.CachedContentFiles = cachedContentFiles;
    }
}
