using System;
using Gw2.Dat.Content;

namespace Gw2.Client;

internal static class EquipmentSockets
{
    internal const uint WeaponSkinType = 24;
    internal const uint BackSkinType = 2;
    internal const uint GatheringSkinType = 9;
    internal const int HighestSocket = 10;

    private static readonly string?[] BoneBySocket =
        [null, "root", "cholsterback", "lholsterback", "rholsterback", "bccloth", "lefthand", "righthand", "shield", "lholsterhip", "rholsterhip"];

    internal static string HeldBone(Gw2Skin skin, uint weaponType)
    {
        if (skin.SkinType == WeaponSkinType)
        {
            switch (skin.WeaponKind)
            {
                case 2 or 3 or 13 or 14 or 15: return "lefthand";
                case 16: return "shield";
            }
        }
        return weaponType is 28 or 30 or 32 ? "lefthand" : "righthand";
    }

    internal static string? StowedBone(Gw2Skin skin, uint weaponType)
    {
        if (skin.Socket != 0)
        {
            if (skin.Socket > HighestSocket) throw new ArgumentOutOfRangeException(nameof(skin), skin.Socket, "socket");
            return BoneBySocket[skin.Socket];
        }
        switch (skin.SkinType)
        {
            case BackSkinType:
                return "cholsterback";
            case GatheringSkinType when skin.FirstTypeDataValue is 0 or 1 or 2:
                return "righthand";
            case WeaponSkinType:
                switch (skin.WeaponKind)
                {
                    case 1 or 9 or 12 or 19 or 21 or 23: return "lholsterback";
                    case 2 or 3 or 6 or 10 or 20: return "rholsterback";
                    case 16: return "cholsterback";
                    case 22: return "lholsterhip";
                }
                break;
        }
        return weaponType switch
        {
            24 or 25 or 26 or 27 or 29 or 31 => "lholsterhip",
            28 or 30 or 32 => "rholsterhip",
            _ => "lholsterback",
        };
    }

    internal static string? GripBone(string socketBone) => socketBone switch
    {
        "cholsterback" => "cstowback",
        "lholsterback" => "lstowback",
        "rholsterback" => "rstowback",
        "lholsterhip" => "lstowhip",
        "rholsterhip" => "rstowhip",
        "lefthand" or "righthand" => "rgriphand",
        "shield" => "shieldgrip",
        _ => null,
    };
}
