using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapObstacle(
    uint Token,
    uint Flags,
    IReadOnlyList<Vector3> BottomVertices,
    IReadOnlyList<Vector3> TopVertices,
    string Name,
    ulong Surface);

public sealed class Gw2MapObstacles
{
    public Gw2MapObstacles(IReadOnlyList<Gw2MapObstacle> obstacles)
    {
        Obstacles = obstacles;
    }

    public IReadOnlyList<Gw2MapObstacle> Obstacles { get; }

    public static Gw2MapObstacles? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapObstacles");
        if (root is null) return null;

        var obstacles = new List<Gw2MapObstacle>();
        if (root.Fields.GetValueOrDefault("obstacles") is object?[] obstacleObjects)
            foreach (var obstacleObject in obstacleObjects)
                if (obstacleObject is PackObject obstacle)
                    obstacles.Add(new(
                        PackValue.AsU32(obstacle.Fields.GetValueOrDefault("token")),
                        PackValue.AsU32(obstacle.Fields.GetValueOrDefault("flags")),
                        PackValue.AsVec3List(obstacle.Fields.GetValueOrDefault("bottmVertices")),
                        PackValue.AsVec3List(obstacle.Fields.GetValueOrDefault("topVertices")),
                        obstacle.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        PackValue.AsU64(obstacle.Fields.GetValueOrDefault("surface"))));

        return new(obstacles);
    }
}
