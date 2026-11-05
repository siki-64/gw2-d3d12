using Gw2.Formats;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public static class Gw2EquipmentGripBone
{
    private static readonly Dictionary<string, string> ItemBoneBySocketBone = new(StringComparer.Ordinal)
    {
        ["cholsterback"] = "cstowback",
        ["lholsterback"] = "lstowback",
        ["rholsterback"] = "rstowback",
        ["lholsterhip"] = "lstowhip",
        ["rholsterhip"] = "rstowhip",
        ["lefthand"] = "rgriphand",
        ["righthand"] = "rgriphand",
        ["shield"] = "shieldgrip",
    };

    public static ulong ItemBoneFor(ulong socketBone) =>
        ItemBoneBySocketBone.TryGetValue(Token64.Decode(socketBone), out var itemBone) ? Token64.Encode(itemBone) : 0;
}
