namespace Gw2.Dat.Content;

public enum Gw2SortType
{
    Unsorted,
    FrontToBack,
    ByStateThenFrontToBack,
    BackToFront,
}

public static class Gw2SortBuckets
{
    public const int Default = 2;
    public const int FirstDecal = 7;
    public const int FirstNonOpaque = 9;
    public const int SplitPasses = 11;
    public const int SplitFirstPass = 12;
    public const int SplitLaterPasses = 13;
    public const int Distortion = 15;

    private static readonly int[] BucketBySortOrder = [1, 2, 3, 6, 10, 9, 9, 5, 13, 14, 15, 10, 4, 11, 7, 8];
    private static readonly Gw2SortType[] TypeByBucket =
    [
        Gw2SortType.Unsorted, Gw2SortType.FrontToBack, Gw2SortType.ByStateThenFrontToBack, Gw2SortType.ByStateThenFrontToBack,
        Gw2SortType.ByStateThenFrontToBack, Gw2SortType.ByStateThenFrontToBack, Gw2SortType.BackToFront, Gw2SortType.BackToFront,
        Gw2SortType.BackToFront, Gw2SortType.BackToFront, Gw2SortType.BackToFront, Gw2SortType.Unsorted,
        Gw2SortType.BackToFront, Gw2SortType.BackToFront, Gw2SortType.BackToFront, Gw2SortType.BackToFront,
    ];

    public static int Of(byte sortOrder) => sortOrder < BucketBySortOrder.Length ? BucketBySortOrder[sortOrder] : Default;

    public static Gw2SortType TypeOf(int bucket) => bucket >= 0 && bucket < TypeByBucket.Length ? TypeByBucket[bucket] : Gw2SortType.Unsorted;

    public static bool IsNonOpaque(int bucket) => bucket >= FirstNonOpaque;
}
