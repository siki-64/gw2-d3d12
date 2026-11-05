using System.Collections.Generic;

namespace Gw2.Client.Net.Login;

public static class MapTravelGameTypes
{
    public const uint Unknown = 22;
    public const uint CharacterCreate = 1;

    private static readonly Dictionary<uint, uint> GameTypeByMapType = new()
    {
        [4] = 4, [5] = 3, [16] = 18, [7] = 9, [8] = 5, [3] = 6, [1] = 1, [0] = 4, [2] = 11,
        [9] = 7, [12] = 12, [11] = 13, [10] = 14, [14] = 16, [15] = 17, [17] = 19, [19] = 20, [18] = 21,
    };

    public static uint ForMapType(uint mapType) => GameTypeByMapType.TryGetValue(mapType, out var gameType) ? gameType : Unknown;
}
