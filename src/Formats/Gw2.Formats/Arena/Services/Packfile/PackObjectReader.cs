using System.Runtime.InteropServices;
using System.Text;

namespace Gw2.Formats;

internal sealed unsafe class PackObjectReader
{
    private readonly Dictionary<(PackType, nint), PackObject> _objects = [];

    private PackObjectReader() { }

    internal static PackObject Read(byte* data, PackType type) => new PackObjectReader().ReadObject(data, type);

    private PackObject ReadObject(byte* address, PackType type)
    {
        if (_objects.TryGetValue((type, (nint)address), out var existing)) return existing;
        var result = new PackObject(type.Name);
        _objects[(type, (nint)address)] = result;
        byte* field = address;
        foreach (PackField packField in type.Fields)
        {
            result.Add(packField.Name, ReadField(field, packField));
            field += packField.Size;
        }
        return result;
    }

    private object? Element(byte* address, PackType type) =>
        type.Fields.Length == 1 && type.Fields[0].Name.Length == 0
            ? ReadField(address, type.Fields[0])
            : ReadObject(address, type);

    private object Elements(byte* elements, PackType type, int count, bool pointers)
    {
        if (count == 0) return Array.Empty<object>();
        if (elements == null) throw new InvalidDataException("Nonempty pack array has null pointer");
        if (!pointers && type.Fields.Length == 1 && type.Fields[0].Kind == PackFieldKind.Byte && type.Fields[0].Name.Length == 0)
            return new ReadOnlyMemory<byte>(new ReadOnlySpan<byte>(elements, count).ToArray());
        var result = new object?[count];
        for (int index = 0; index < count; index++)
        {
            byte* element = pointers ? ((byte**)elements)[index] : elements + index * type.Size;
            result[index] = element == null ? null : Element(element, type);
        }
        return result;
    }

    private object? ReadField(byte* address, PackField field)
    {
        switch (field.Kind)
        {
            case PackFieldKind.FixedArray:
                return Elements(address, field.Reference!, field.Count, pointers: false);
            case PackFieldKind.Array:
            case PackFieldKind.PointerArray:
                return Elements(*(byte**)(address + 4), field.Reference!, checked((int)*(uint*)address), field.Kind == PackFieldKind.PointerArray);
            case PackFieldKind.WordArray:
            case PackFieldKind.WordPointerArray:
                return Elements(*(byte**)(address + 2), field.Reference!, *(ushort*)address, field.Kind == PackFieldKind.WordPointerArray);
            case PackFieldKind.ByteArray:
            case PackFieldKind.BytePointerArray:
                return Elements(*(byte**)(address + 1), field.Reference!, *address, field.Kind == PackFieldKind.BytePointerArray);
            case PackFieldKind.Byte:
                return *address;
            case PackFieldKind.Byte4:
            case PackFieldKind.Uuid:
            case PackFieldKind.Byte3:
                return new ReadOnlyMemory<byte>(new ReadOnlySpan<byte>(address, field.Size).ToArray());
            case PackFieldKind.Double:
                return *(double*)address;
            case PackFieldKind.Double2:
            case PackFieldKind.Double3:
                return new ReadOnlySpan<double>(address, field.Size / sizeof(double)).ToArray();
            case PackFieldKind.Dword:
            case PackFieldKind.DwordToken:
                return *(uint*)address;
            case PackFieldKind.Float:
                return *(float*)address;
            case PackFieldKind.Float2:
            case PackFieldKind.Float3:
            case PackFieldKind.Float4:
                return new ReadOnlySpan<float>(address, field.Size / sizeof(float)).ToArray();
            case PackFieldKind.Qword:
            case PackFieldKind.QwordToken:
                return *(ulong*)address;
            case PackFieldKind.Word:
                return *(ushort*)address;
            case PackFieldKind.Dword2:
            case PackFieldKind.Dword4:
                return new ReadOnlySpan<uint>(address, field.Size / sizeof(uint)).ToArray();
            case PackFieldKind.Word3:
                return new ReadOnlySpan<ushort>(address, 3).ToArray();
            case PackFieldKind.FileName:
            case PackFieldKind.FileReference:
            {
                byte* target = *(byte**)address;
                return target == null ? null : PackFileReference.Parse(new ReadOnlySpan<byte>(target, 6));
            }
            case PackFieldKind.Pointer:
            {
                byte* target = *(byte**)address;
                return target == null ? null : Element(target, field.Reference!);
            }
            case PackFieldKind.WideString:
            {
                char* target = *(char**)address;
                return target == null ? null : new string(target);
            }
            case PackFieldKind.String:
            {
                byte* target = *(byte**)address;
                return target == null ? null : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(target));
            }
            case PackFieldKind.Struct:
            case PackFieldKind.StructBase:
                return Element(address, field.Reference!);
            case PackFieldKind.Variant:
            {
                uint variant = *(uint*)address;
                if (variant >= field.Variants.Length) throw new InvalidDataException($"Variant {field.Name} case {variant} is outside {field.Variants.Length} cases");
                PackType payloadType = field.Variants[variant];
                byte* payload = *(byte**)(address + 4);
                return new PackVariant((int)variant, payloadType.Id, payload == null ? null : ReadObject(payload, payloadType));
            }
            default:
                throw new NotSupportedException($"Pack field {field.Name} kind {field.Kind} is unsupported");
        }
    }
}
