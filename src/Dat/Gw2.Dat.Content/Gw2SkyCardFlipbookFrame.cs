using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2SkyCardFlipbookFrame(Vector4 Cell, Vector4 NextCell, float Blend)
{
    public const uint FlipbookCardFlag = 0x40;
    public const uint CellToken = 0x3001F04E;
    public const uint NextCellToken = 0x3006356F;
    public const uint BlendToken = 0x57A954C0;

    public static bool Animates(uint cardFlags, Gw2MapEnvSkyCardFlipbook? flipbook) =>
        (cardFlags & FlipbookCardFlag) != 0 && flipbook is not null;

    public static Gw2SkyCardFlipbookFrame At(Gw2MapEnvSkyCardFlipbook flipbook, float seconds)
    {
        ArgumentNullException.ThrowIfNull(flipbook);
        var rows = (int)Math.Max(flipbook.Rows, 1u);
        var columns = (int)Math.Max(flipbook.Columns, 1u);
        var count = (int)Math.Max(flipbook.Count, 1u);
        var start = (int)flipbook.Start;
        var position = flipbook.Fps * seconds;
        var frame = (int)position;
        var cellWidth = 1f / columns;
        var cellHeight = 1f / rows;
        Vector4 CellOf(int frameIndex)
        {
            var cell = (start + frameIndex % count) % (rows * columns);
            var left = cell % columns * cellWidth;
            var top = cell / columns * cellHeight;
            return new(left, left + cellWidth, top, top + cellHeight);
        }

        return new(CellOf(frame), CellOf(frame + 1), position - frame);
    }
}
