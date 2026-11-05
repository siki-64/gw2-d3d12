namespace Gw2.Client;

public static class Gw2MemoryUsage
{
    public static long GpuLocalBytes { get; set; }

    public static long GpuNonLocalBytes { get; set; }

    public static int UploadRingPages { get; set; }

    public static int CommandLists { get; set; }

    public static int CopyStagingPages { get; set; }

    public static int CopyCommandLists { get; set; }

    public static int CachedContentFiles { get; set; }
}
