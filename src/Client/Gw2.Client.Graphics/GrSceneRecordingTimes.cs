using System;

namespace Gw2.Client.Graphics;

public static class GrSceneRecordingTimes
{
    public static double PrepareMilliseconds { get; set; }

    public static double MainThreadPassesMilliseconds { get; set; }

    public static double WaitMilliseconds { get; set; }

    public static double SubmitMilliseconds { get; set; }

    public static double FogCompositeMilliseconds { get; set; }

    public static double PrepareGpuSceneMilliseconds { get; set; }

    public static double PrepareFarShadowMilliseconds { get; set; }

    public static double PrepareFadeColorsMilliseconds { get; set; }

    public static double PrepareLightBufferMilliseconds { get; set; }

    public static double PrepareWaterReflectionMilliseconds { get; set; }

    public static double OpaqueSortMilliseconds { get; set; }

    public static double OpaqueEarlyBucketsMilliseconds { get; set; }

    public static double OpaqueGpuGroupsMilliseconds { get; set; }

    public static double OpaqueDefaultBucketsMilliseconds { get; set; }

    public static double PrepareGpuSceneRebuildMilliseconds { get; set; }

    public static int PrepareGpuSceneRebuildCount { get; set; }

    public static int PrepareGpuSceneInstanceCount { get; set; }

    public static double RebuildGroupsMilliseconds { get; set; }

    public static double RebuildRowsMilliseconds { get; set; }

    public static double RebuildGroupSetsMilliseconds { get; set; }

    public static double RebuildUploadMilliseconds { get; set; }

    public static int[] OpaqueDefaultBucketDrawCounts { get; } = new int[Enum.GetValues<GrSortedDrawKind>().Length];

    public static void Clear()
    {
        Array.Clear(OpaqueDefaultBucketDrawCounts);
        PrepareMilliseconds = 0;
        MainThreadPassesMilliseconds = 0;
        WaitMilliseconds = 0;
        SubmitMilliseconds = 0;
        FogCompositeMilliseconds = 0;
        RebuildGroupsMilliseconds = 0;
        RebuildRowsMilliseconds = 0;
        RebuildGroupSetsMilliseconds = 0;
        RebuildUploadMilliseconds = 0;
        PrepareGpuSceneRebuildMilliseconds = 0;
        PrepareGpuSceneRebuildCount = 0;
        PrepareGpuSceneMilliseconds = 0;
        PrepareFarShadowMilliseconds = 0;
        PrepareFadeColorsMilliseconds = 0;
        PrepareLightBufferMilliseconds = 0;
        PrepareWaterReflectionMilliseconds = 0;
        OpaqueSortMilliseconds = 0;
        OpaqueEarlyBucketsMilliseconds = 0;
        OpaqueGpuGroupsMilliseconds = 0;
        OpaqueDefaultBucketsMilliseconds = 0;
    }
}
