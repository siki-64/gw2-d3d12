using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2PropLodDistances(float Lod1, float Lod2)
{
    public const float EngineLod1 = 3500f;
    public const float EngineLod2 = 7500f;
    public const float MaximumDistance = 100000f;

    public static Gw2PropLodDistances For(Gw2MapPropPlacement placement, Gw2Model? model)
    {
        var lod1 = placement.Lod1 ?? 0f;
        var lod2 = placement.Lod2 ?? 0f;
        if ((placement.Flags & Gw2MapPropFlags.OverrideLod) != 0 && (lod1 != 0f || lod2 != 0f))
            return new Gw2PropLodDistances(Clamp(lod1), Clamp(lod2));
        var modelLod = model?.LodOverride ?? [];
        return new Gw2PropLodDistances(
            modelLod.Length > 0 && modelLod[0] > 0f ? Clamp(modelLod[0]) : EngineLod1,
            modelLod.Length > 1 && modelLod[1] > 0f ? Clamp(modelLod[1]) : EngineLod2);
    }

    private static float Clamp(float distance) => float.IsNaN(distance) ? 0f : Math.Clamp(distance, 0f, MaximumDistance);
}

public enum Gw2LodDistance { Low, Medium, High, Ultra }

public static class Gw2LodDistanceSetting
{
    public static float DistanceFactor(Gw2LodDistance level) => level switch
    {
        Gw2LodDistance.High => 1.5f,
        Gw2LodDistance.Ultra => 2f,
        _ => 1f,
    };

    public static int ScatterQualityMode(Gw2LodDistance level) => (int)level;
}

public static class Gw2PropLod
{
    public const float Hysteresis = 75f;
    public const int Hidden = -1;

    public static int SelectForPlacement(uint placementFlags, Gw2PropLodDistances distances, float scale, float distanceFactor, Vector3 camera, Vector3 origin, int currentLevel)
    {
        if ((placementFlags & Gw2MapPropFlags.DropLod2) != 0 && Vector3.Distance(camera, origin) > scale * distances.Lod2) return Hidden;
        if ((placementFlags & Gw2MapPropFlags.DisableLod) != 0) return 0;
        return Select(distances, scale, distanceFactor, camera, origin, currentLevel == Hidden ? 0 : currentLevel);
    }

    public static int Select(Gw2PropLodDistances distances, float scale, float distanceFactor, Vector3 camera, Vector3 origin, int currentLevel)
    {
        var near = scale * distances.Lod1 * distanceFactor;
        var far = scale * distances.Lod2 * distanceFactor;
        var distance = Vector3.Distance(camera, origin);
        if (distance < near) return 0;
        if (distance < far && distance > near + Hysteresis) return 1;
        if (distance > far + Hysteresis) return 2;
        return currentLevel;
    }
}
