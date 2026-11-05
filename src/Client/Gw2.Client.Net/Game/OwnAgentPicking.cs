using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Net.Game;

public readonly record struct PickCandidate(uint AgentId, Vector3 PositionInGameUnits, float HeightInGameUnits, float RadiusInGameUnits);

public readonly record struct PickRay(Vector3 Near, Vector3 Far, Vector3 Origin, float CursorHitDistanceInGameUnits);

public static class OwnAgentPicking
{
    public const float CandidateRangeInGameUnits = 4000f;
    public const float CursorRadius = 0.2f;
    public const float Size = 1f;
    private const float DistanceAllowanceInGameUnits = 24f;
    private const float DepthFalloffInGameUnits = 4000f;
    private const float NarrowingSpan = 0.19f;
    private const float NarrowestAllowance = 0.01f;
    private const float SmallestHeight = 0.001f;
    private const float SmallestClipW = 0.000001f;

    public static (Vector2 Point, float Depth) Project(Vector3 world, Matrix4x4 worldToClip)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), worldToClip);
        var w = clip.W;
        if (MathF.Abs(w) < SmallestClipW) w = w <= 0f ? -SmallestClipW : SmallestClipW;
        var point = new Vector2(clip.X / w, clip.Y / w);
        if (w < 0f) point = -point;
        return (point, clip.Z);
    }

    public static float ScoreOf(PickCandidate candidate, Matrix4x4 worldToClip, Vector2 cursor, PickRay ray, bool isSelection)
    {
        var position = candidate.PositionInGameUnits;
        if (Vector3.Dot(position - ray.Near, ray.Far - ray.Near) < 0f) return 0f;
        var height = MathF.Max(SmallestHeight, candidate.HeightInGameUnits);
        var reach = candidate.RadiusInGameUnits + DistanceAllowanceInGameUnits + ray.CursorHitDistanceInGameUnits;
        if (!isSelection && Vector3.DistanceSquared(position, ray.Origin) > reach * reach) return 0f;
        var (feet, feetDepth) = Project(position, worldToClip);
        var (top, topDepth) = Project(position with { Z = position.Z - height }, worldToClip);
        if (feetDepth <= 0f && topDepth <= 0f) return 0f;
        var distanceSquared = SquaredDistanceToSegment(cursor, feet, top);
        var allowance = ((DepthFalloffInGameUnits - MathF.Min(DepthFalloffInGameUnits, (feetDepth + topDepth) * 0.5f)) / DepthFalloffInGameUnits * NarrowingSpan + NarrowestAllowance) * Size;
        if (distanceSquared >= allowance * allowance) return 0f;
        var radiusSquared = CursorRadius * Size * (CursorRadius * Size);
        var score = MathF.Max(0f, (radiusSquared - distanceSquared) / radiusSquared);
        return isSelection ? score : score * score * (score * score);
    }

    public static uint? HoveredAgent(IEnumerable<PickCandidate> candidates, Matrix4x4 worldToClip, Vector2 cursor, PickRay ray, Vector3 updatePoint, uint? selectedAgentId)
    {
        uint? best = null;
        var bestScore = 0f;
        foreach (var candidate in candidates)
        {
            if (Vector3.DistanceSquared(candidate.PositionInGameUnits, updatePoint) > CandidateRangeInGameUnits * CandidateRangeInGameUnits) continue;
            var score = ScoreOf(candidate, worldToClip, cursor, ray, candidate.AgentId == selectedAgentId);
            if (score != 0f && score > bestScore)
            {
                best = candidate.AgentId;
                bestScore = score;
            }
        }
        return best;
    }

    public static float SquaredDistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var along = end - start;
        var offset = point - start;
        var t = Vector2.Dot(along, offset) / along.LengthSquared();
        if (!(t > 0f)) t = 0f;
        if (t > 1f) t = 1f;
        return Vector2.DistanceSquared(offset, along * t);
    }
}

public sealed class OwnClickSelection
{
    public const long LongestClickMilliseconds = 250;
    public const float LargestMoveSquaredOverWidthSquared = 0.000023841858f;

    private (uint? HoveredAgentId, Vector2 Cursor, long Milliseconds)? _press;

    public void Press(uint? hoveredAgentId, Vector2 cursorInPixels, long milliseconds)
    {
        _press ??= (hoveredAgentId, cursorInPixels, milliseconds);
    }

    public bool TryRelease(uint? hoveredAgentId, Vector2 cursorInPixels, long milliseconds, float layoutWidthInPixels, out uint? selectedAgentId)
    {
        selectedAgentId = null;
        if (_press is not { } press) return false;
        _press = null;
        if (milliseconds - press.Milliseconds > LongestClickMilliseconds) return false;
        if (Vector2.DistanceSquared(press.Cursor, cursorInPixels) / (layoutWidthInPixels * layoutWidthInPixels) >= LargestMoveSquaredOverWidthSquared) return false;
        if (hoveredAgentId is null) return true;
        if (press.HoveredAgentId != hoveredAgentId) return false;
        selectedAgentId = hoveredAgentId;
        return true;
    }
}
