using System.Numerics;

namespace Gw2.Formats;

public static class PackValue
{
    public static uint AsU32(object? value) => value switch
    {
        uint unsigned => unsigned,
        int signed => (uint)signed,
        ulong wide => (uint)wide,
        ushort shortValue => shortValue,
        byte byteValue => byteValue,
        _ => 0,
    };

    public static ulong AsU64(object? value) => value switch
    {
        ulong wide => wide,
        uint unsigned => unsigned,
        int signed => (ulong)signed,
        _ => 0,
    };

    public static float AsF32(object? value) => value is float single ? single : 0;

    public static byte AsU8(object? value) => (byte)AsU32(value);

    public static byte[] AsBytes(object? value) => value switch
    {
        byte[] bytes => bytes,
        ReadOnlyMemory<byte> memory => memory.ToArray(),
        object?[] items => items.Select(AsU8).ToArray(),
        _ => [],
    };

    public static float[] AsFloats(object? value) => value is float[] floats ? floats : [];

    public static IEnumerable<PackObject> AsObjects(object? value) =>
        value is object?[] items ? items.OfType<PackObject>() : [];

    public static int ArrayLength(object? value) => value is object?[] items ? items.Length : 0;

    public static IReadOnlyList<ushort> AsU16List(object? value) =>
        value is object?[] items ? items.Select(item => (ushort)AsU32(item)).ToArray() : [];

    public static IReadOnlyList<uint> AsU32List(object? value) =>
        value switch
        {
            uint[] values => values,
            object?[] items => items.Select(AsU32).ToArray(),
            _ => [],
        };

    public static IReadOnlyList<ulong> AsU64List(object? value) =>
        value switch
        {
            ulong[] values => values,
            object?[] items => items.Select(AsU64).ToArray(),
            _ => [],
        };

    public static IReadOnlyList<string> AsStrings(object? value) =>
        value is object?[] items ? items.OfType<string>().ToArray() : [];

    public static IReadOnlyList<Vector2> AsVec2List(object? value) =>
        value is object?[] items
            ? items.OfType<float[]>().Where(floats => floats.Length >= 2).Select(floats => new Vector2(floats[0], floats[1])).ToArray()
            : [];

    public static IReadOnlyList<Vector3> AsVec3List(object? value) =>
        value is object?[] items
            ? items.OfType<float[]>().Where(floats => floats.Length >= 3).Select(floats => new Vector3(floats[0], floats[1], floats[2])).ToArray()
            : [];

    public static uint RequireU32(PackObject owner, string name) =>
        ToU32(owner, name, Require(owner, name));

    public static ulong RequireU64(PackObject owner, string name) =>
        ToU64(owner, name, Require(owner, name));

    public static float RequireF32(PackObject owner, string name) =>
        ToF32(owner, name, Require(owner, name));

    public static uint? OptionalU32(PackObject owner, string name) =>
        owner.TryGetOptional(name, out var value) ? ToU32(owner, name, value) : null;

    public static ulong? OptionalU64(PackObject owner, string name) =>
        owner.TryGetOptional(name, out var value) ? ToU64(owner, name, value) : null;

    public static float? OptionalF32(PackObject owner, string name) =>
        owner.TryGetOptional(name, out var value) ? ToF32(owner, name, value) : null;

    private static object? Require(PackObject owner, string name) =>
        owner.Fields.TryGetValue(name, out var value)
            ? value
            : throw new PackFieldException($"{owner.TypeName} has no field '{name}'.");

    private static uint ToU32(PackObject owner, string name, object? value) => value switch
    {
        uint or int or ushort or byte => AsU32(value),
        _ => throw Mistyped(owner, name, value, "u32"),
    };

    private static ulong ToU64(PackObject owner, string name, object? value) => value switch
    {
        ulong or uint or int => AsU64(value),
        _ => throw Mistyped(owner, name, value, "u64"),
    };

    private static float ToF32(PackObject owner, string name, object? value) =>
        value is float single ? single : throw Mistyped(owner, name, value, "f32");

    private static PackFieldException Mistyped(PackObject owner, string name, object? value, string expected) =>
        new($"{owner.TypeName}.{name} is {value?.GetType().Name ?? "null"}, expected {expected}.");
}

public sealed class PackFieldException(string message) : IOException(message);
