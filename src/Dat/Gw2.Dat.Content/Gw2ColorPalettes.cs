using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public readonly record struct Gw2PaletteColor(float Brightness, float Contrast, float Hue, float Saturation, float Lightness);

public sealed class Gw2ColorPalettes
{
    private const float ChannelCenter = 128f;
    private const float ChannelScale = 1f / 128f;
    private const float DegreesPerHalfTurn = 180f;
    private const int BrightnessOffset = 0x0;
    private const int ContrastOffset = 0x4;
    private const int HueOffset = 0x8;
    private const int SaturationOffset = 0xC;
    private const int LightnessOffset = 0x10;

    private readonly Gw2ContentIndex _index;
    private readonly ColorPaletteContentLayout _layout;

    public Gw2ColorPalettes(Gw2ContentIndex index, ColorPaletteContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public Gw2PaletteColor? FindColor(string paletteName, uint index, uint materialKey)
    {
        if (_index.FindByName(_layout.PaletteContentType, paletteName) is not { } palette) return null;
        uint count = _index.ReadUInt32(palette, _layout.ColorCountOffset);
        if (count == 0) return null;
        if (_index.ReadPointer(palette, _layout.ColorsOffset) is not { } colors) return null;
        int entryOffset = checked((int)(index % count) * _layout.ColorEntrySize);
        return _index.ReadPointer(colors, entryOffset) is { } color ? Resolve(color, materialKey) : null;
    }

    public Gw2PaletteColor? FindColorByContentKey(uint colorContentType, Guid contentKey, uint materialKey) =>
        contentKey != Guid.Empty && _index.FindByContentKey(colorContentType, contentKey) is { } color ? Resolve(color, materialKey) : null;

    public Gw2PaletteColor? FindColorByDataId(uint colorContentType, uint colorDataId, uint materialKey) =>
        colorDataId != 0 && _index.FindTyped(colorContentType, colorDataId) is { } color ? Resolve(color, materialKey) : null;

    public uint? ColorCount(string paletteName) =>
        _index.FindByName(_layout.PaletteContentType, paletteName) is { } palette ? _index.ReadUInt32(palette, _layout.ColorCountOffset) : null;

    private Gw2PaletteColor Resolve(Gw2ContentLocation color, uint materialKey)
    {
        var variant = new Gw2PaletteColor(_layout.DefaultBrightness, _layout.DefaultContrast, _layout.DefaultHue, _layout.DefaultSaturation, _layout.DefaultLightness);
        uint count = _index.ReadUInt32(color, _layout.VariantCountOffset);
        if (count > 0 && _index.ReadPointer(color, _layout.VariantsOffset) is { } variants)
            for (int index = 0; index < count; index++)
            {
                int offset = checked(index * _layout.VariantSize);
                uint key = _index.ReadUInt32(variants, offset + _layout.VariantMaterialKeyOffset);
                if (key == materialKey) { variant = ReadVariant(variants, offset); break; }
                if (key == 0) variant = ReadVariant(variants, offset);
            }
        return new Gw2PaletteColor(
            (variant.Brightness - ChannelCenter) * ChannelScale,
            variant.Contrast * ChannelScale,
            variant.Hue * MathF.PI / DegreesPerHalfTurn,
            variant.Saturation * ChannelScale,
            variant.Lightness * ChannelScale);
    }

    private Gw2PaletteColor ReadVariant(Gw2ContentLocation variants, int offset) => new(
        _index.ReadSingle(variants, offset + BrightnessOffset),
        _index.ReadSingle(variants, offset + ContrastOffset),
        _index.ReadSingle(variants, offset + HueOffset),
        _index.ReadSingle(variants, offset + SaturationOffset),
        _index.ReadSingle(variants, offset + LightnessOffset));
}
