using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gw2.Client.Net.Transport;

namespace Gw2.Client.Net.Login;

public sealed record LiveBuild(uint Build, uint ExecutableFileId, uint ExecutableSize, uint ManifestFileId, uint ManifestSize)
{
    public const string AssetHostPrefix = "assetcdn";
    public const string LatestPathPrefix = "latest64";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static Uri LatestUri(uint programId) => new($"http://{AssetHostPrefix}.{programId}.{AuthServerAddress.DnsSuffix}/{LatestPathPrefix}/{programId}");

    public static async Task<LiveBuild> FetchAsync(uint programId, CancellationToken cancellation)
    {
        using var http = new HttpClient { Timeout = RequestTimeout };
        var reply = await http.GetStringAsync(LatestUri(programId), cancellation).ConfigureAwait(false);
        return Parse(reply);
    }

    public static LiveBuild Parse(string reply)
    {
        var numbers = reply.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        uint At(int index) => index < numbers.Length && uint.TryParse(numbers[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        if (At(0) == 0) throw new FormatException($"the asset server sent no build ('{reply.Trim()}')");
        return new LiveBuild(At(0), At(1), At(2), At(3), At(4));
    }
}
