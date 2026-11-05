using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Gw2.Dat.Archive;

public sealed class Gw2KeyStore
{
    public const string TextKind = "text";
    public const string AudioKind = "audio";

    private readonly Dictionary<string, Dictionary<uint, ulong>> _keysByKind;

    public Gw2KeyStore(string build, Dictionary<string, Dictionary<uint, ulong>> keysByKind)
    {
        Build = build;
        _keysByKind = keysByKind;
    }

    public string Build { get; }

    public static Gw2KeyStore Empty() => new(string.Empty, new Dictionary<string, Dictionary<uint, ulong>>());

    public void Store(string kind, uint id, ulong key)
    {
        if (id == 0 || key == 0) return;
        lock (_keysByKind)
        {
            if (!_keysByKind.TryGetValue(kind, out var keys)) _keysByKind[kind] = keys = [];
            keys[id] = key;
        }
    }

    public void Clear(string kind)
    {
        lock (_keysByKind)
            if (_keysByKind.TryGetValue(kind, out var keys)) keys.Clear();
    }

    public int Count(string kind)
    {
        lock (_keysByKind) return _keysByKind.TryGetValue(kind, out var keys) ? keys.Count : 0;
    }

    public IEnumerable<uint> Ids(string kind)
    {
        lock (_keysByKind) return _keysByKind.TryGetValue(kind, out var keys) ? [.. keys.Keys] : [];
    }

    public ulong? Find(string kind, uint id)
    {
        lock (_keysByKind) return _keysByKind.TryGetValue(kind, out var keys) && keys.TryGetValue(id, out var key) ? key : null;
    }

    public ulong? FindTextKey(uint textId) => Find(TextKind, textId);

    public static Gw2KeyStore Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static Gw2KeyStore Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var build = root.TryGetProperty("build", out var buildElement) ? buildElement.GetString() ?? string.Empty : string.Empty;
        var keysByKind = new Dictionary<string, Dictionary<uint, ulong>>();
        if (root.TryGetProperty("keys", out var kinds))
        {
            foreach (var kind in kinds.EnumerateObject())
            {
                var keys = new Dictionary<uint, ulong>();
                foreach (var entry in kind.Value.EnumerateObject())
                    keys[uint.Parse(entry.Name, CultureInfo.InvariantCulture)] =
                        ulong.Parse(entry.Value.GetString() ?? throw new InvalidDataException($"Key for {entry.Name} is not a string"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                keysByKind[kind.Name] = keys;
            }
        }
        return new Gw2KeyStore(build, keysByKind);
    }
}
