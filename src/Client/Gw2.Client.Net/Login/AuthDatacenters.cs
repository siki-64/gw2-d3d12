using System;
using System.Collections.Generic;
using System.Linq;

namespace Gw2.Client.Net.Login;

public static class AuthDatacenters
{
    public static readonly IReadOnlyList<uint> All = [1, 2];

    public static IReadOnlyList<uint> TryOrder(uint? connectedDatacenter, Random random)
    {
        uint first = connectedDatacenter is { } connected && All.Contains(connected) ? connected : All[random.Next(All.Count)];
        return [first, .. All.Where(datacenter => datacenter != first)];
    }
}
