namespace Gw2.Dat.Content;

public static class Gw2ModelMaterialDraw
{
    public const uint TwoSidedMaterialFlag = 0x1;
    public const uint NoDepthWriteMaterialFlag = 0x2;

    private static readonly (uint MaterialFlag, uint DrawFlag)[] DrawFlagByMaterialFlag =
    [
        (TwoSidedMaterialFlag, Gw2DrawFlags.SkipEffectCull),
        (NoDepthWriteMaterialFlag, Gw2DrawFlags.LessEqualWithoutDepthWrite),
        (0x10, 0x8),
        (0x80, 0x80000),
        (0x2000, 0x200000),
    ];

    private static readonly int[] SortBucketByModelSortOrder = [0, 2, 2, 3, 6, 9, 9, 10, 10, 10, 5, 13, 13, 13, 13, 14, 15];

    public static uint DrawFlagsOf(uint materialFlags)
    {
        var drawFlags = 0u;
        foreach (var (materialFlag, drawFlag) in DrawFlagByMaterialFlag)
            if ((materialFlags & materialFlag) != 0) drawFlags |= drawFlag;
        return drawFlags;
    }

    public static int SortBucketOverrideOf(uint modelSortOrder) =>
        modelSortOrder < SortBucketByModelSortOrder.Length ? SortBucketByModelSortOrder[modelSortOrder] : 0;
}
