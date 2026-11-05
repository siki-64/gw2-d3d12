using System.Buffers.Binary;
using System.Collections;

namespace Gw2.Formats;

public sealed class PackObject
{
    private readonly Dictionary<string, object?> _fields = [];
    private readonly List<KeyValuePair<string, object?>> _members = [];

    internal PackObject(string typeName)
        => (TypeName, Fields) = (typeName, new PackRecordedFields(typeName, _fields));

    public string TypeName { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }
    public IReadOnlyList<KeyValuePair<string, object?>> Members => _members.AsReadOnly();

    public bool TryGetOptional(string name, out object? value)
    {
        if (!_fields.TryGetValue(name, out value)) return false;
        PackFieldAccess.Note(TypeName, name);
        return true;
    }

    public object? Optional(string name) => TryGetOptional(name, out var value) ? value : null;

    internal void Add(string name, object? value)
    {
        _members.Add(new(name, value));
        string key = name;
        for (int occurrence = 2; _fields.ContainsKey(key); occurrence++) key = $"{name}#{occurrence}";
        _fields.Add(key, value);
    }
}

public sealed record PackVariant(int Case, string TypeId, PackObject? Payload);

public readonly record struct PackFileReference(ushort Low, ushort High, ushort Terminator)
{
    public uint FileId => TryGetFileId(out var fileId)
        ? fileId
        : throw new InvalidDataException("Invalid GW2 encoded file reference");

    public bool TryGetFileId(out uint fileId)
    {
        if (Low >= 0x100 && High >= 0x100 && Terminator == 0)
        {
            fileId = checked(0xFF00u * (uint)(High - 0x100) + Low - 0x100u + 1);
            return true;
        }
        fileId = 0;
        return false;
    }

    public static PackFileReference Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6) throw new InvalidDataException("Truncated GW2 file reference");
        return new(BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]));
    }
}

public sealed class PackFieldAccess
{
    [ThreadStatic] private static PackFieldAccess? _current;
    private readonly HashSet<(string TypeName, string Field)> _reads = [];

    public IReadOnlyCollection<(string TypeName, string Field)> Reads => _reads;

    public static PackFieldAccess Record(Action action)
    {
        var previous = _current;
        var access = new PackFieldAccess();
        _current = access;
        try { action(); }
        finally { _current = previous; }
        return access;
    }

    internal static void Note(string typeName, string key)
    {
        if (_current is null) return;
        int occurrence = key.IndexOf('#');
        _current._reads.Add((typeName, occurrence < 0 ? key : key[..occurrence]));
    }
}

internal sealed class PackRecordedFields(string typeName, Dictionary<string, object?> fields) : IReadOnlyDictionary<string, object?>
{
    public object? this[string key]
    {
        get
        {
            PackFieldAccess.Note(typeName, key);
            return fields[key];
        }
    }

    public IEnumerable<string> Keys => fields.Keys;
    public IEnumerable<object?> Values => fields.Values;
    public int Count => fields.Count;

    public bool ContainsKey(string key)
    {
        PackFieldAccess.Note(typeName, key);
        return fields.ContainsKey(key);
    }

    public bool TryGetValue(string key, out object? value)
    {
        PackFieldAccess.Note(typeName, key);
        return fields.TryGetValue(key, out value);
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => fields.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
