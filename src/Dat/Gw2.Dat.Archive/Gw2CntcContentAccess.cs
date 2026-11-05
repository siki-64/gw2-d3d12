namespace Gw2.Dat.Archive;

public sealed class Gw2CntcContentAccess
{
    public const int PointerSize = 8;

    [ThreadStatic] private static Gw2CntcContentAccess? _current;
    private readonly Dictionary<uint, HashSet<uint>> _bytesByContentType = [];

    public IReadOnlyDictionary<uint, HashSet<uint>> BytesByContentType => _bytesByContentType;

    public static Gw2CntcContentAccess Record(Action action)
    {
        var previous = _current;
        var access = new Gw2CntcContentAccess();
        _current = access;
        try { action(); }
        finally { _current = previous; }
        return access;
    }

    internal static void Note(Gw2CntcPack pack, uint offset, int length)
    {
        if (_current is null || length <= 0 || !pack.TryFindOwningObject(offset, out var owner)) return;
        if (!_current._bytesByContentType.TryGetValue(owner.Type, out var bytes))
            _current._bytesByContentType[owner.Type] = bytes = [];
        var end = Math.Min((ulong)offset + (uint)length, owner.EndOffset);
        for (var position = offset; position < end; position++) bytes.Add(position - owner.Offset);
    }
}
