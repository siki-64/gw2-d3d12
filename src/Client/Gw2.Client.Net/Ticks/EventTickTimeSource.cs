using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gw2.Client.Net.Ticks;

public interface IEventTickTimeSource
{
    bool TryRead(out EventTickTime time);
}

public sealed partial class SystemEventTickTimeSource : IEventTickTimeSource
{
    public bool TryRead(out EventTickTime time)
    {
        var milliseconds = TimeGetTime();
        time = new EventTickTime(milliseconds, (ulong)DateTime.UtcNow.ToFileTimeUtc());
        return true;
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeGetTime")]
    private static partial uint TimeGetTime();
}

public sealed class RecordedEventTickTimeSource : IEventTickTimeSource
{
    private readonly IReadOnlyList<EventTickTime> _times;
    private int _next;

    public RecordedEventTickTimeSource(IReadOnlyList<EventTickTime> times) => _times = times ?? throw new ArgumentNullException(nameof(times));

    public int Remaining => _times.Count - _next;

    public bool TryRead(out EventTickTime time)
    {
        if (_next == _times.Count)
        {
            time = default;
            return false;
        }
        time = _times[_next++];
        return true;
    }
}
