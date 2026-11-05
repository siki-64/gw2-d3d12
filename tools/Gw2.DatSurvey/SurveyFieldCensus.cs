using Gw2.Formats;
using System.Collections.Concurrent;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record SurveyFieldValues(string TypeName, string Field, long Objects, long Populated);

public sealed class SurveyFieldCensus
{
    private sealed class Counts
    {
        public long Objects;
        public long Populated;
    }

    private readonly ConcurrentDictionary<(string TypeName, string Field), Counts> _counts = new();

    public void Add(PackObject root)
    {
        var pending = new Stack<PackObject>();
        pending.Push(root);
        while (pending.TryPop(out var current))
            foreach (var (field, value) in current.Members)
            {
                var counts = _counts.GetOrAdd((current.TypeName, field), static _ => new());
                Interlocked.Increment(ref counts.Objects);
                if (IsPopulated(value)) Interlocked.Increment(ref counts.Populated);
                PushObjects(value, pending);
            }
    }

    public IReadOnlyCollection<SurveyFieldValues> Values =>
        _counts.Select(pair => new SurveyFieldValues(pair.Key.TypeName, pair.Key.Field, pair.Value.Objects, pair.Value.Populated))
            .ToArray();

    public static bool IsPopulated(object? value) => value switch
    {
        null => false,
        PackObject => true,
        string text => text.Length > 0,
        object?[] items => items.Length > 0,
        byte[] bytes => bytes.AsSpan().ContainsAnyExcept((byte)0),
        ReadOnlyMemory<byte> bytes => bytes.Span.ContainsAnyExcept((byte)0),
        Array array => array.Cast<object?>().Any(IsPopulated),
        PackFileReference reference => reference != default,
        byte number => number != 0,
        sbyte number => number != 0,
        ushort number => number != 0,
        short number => number != 0,
        uint number => number != 0,
        int number => number != 0,
        ulong number => number != 0,
        long number => number != 0,
        float number => number != 0,
        double number => number != 0,
        _ => true,
    };

    private static void PushObjects(object? value, Stack<PackObject> pending)
    {
        if (value is PackObject child) pending.Push(child);
        else if (value is object?[] items)
            foreach (var item in items)
                PushObjects(item, pending);
    }
}
