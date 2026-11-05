using Gw2.Formats;

namespace Gw2.Dat.Content;

public static class Gw2ContentName
{
    public static string Mangle(string fullName) => NameHash.Mangle(fullName);

    public static uint Hash(string name) => NameHash.Compute(name);
}
