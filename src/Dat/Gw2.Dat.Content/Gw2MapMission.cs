using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapInterestPoint(Vector3 Position, Vector3 Forward, string Description);

public sealed class Gw2MapMission
{
    public Gw2MapMission(IReadOnlyList<Gw2MapInterestPoint> interestPoints)
    {
        InterestPoints = interestPoints;
    }

    public IReadOnlyList<Gw2MapInterestPoint> InterestPoints { get; }

    public static Gw2MapMission? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapMission");
        if (root is null) return null;

        var points = new List<Gw2MapInterestPoint>();
        if (root.Fields.GetValueOrDefault("interestPoint") is object?[] pointObjects)
            foreach (var pointObject in pointObjects)
                if (pointObject is PackObject point)
                    points.Add(new(
                        Vec3(point, "position"),
                        Vec3(point, "forward"),
                        point.Fields.GetValueOrDefault("description") as string ?? string.Empty));

        return new(points);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
