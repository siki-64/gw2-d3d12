using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2ParticleFieldConstants(Vector4 A, Vector4 B, Vector4 C, Vector4 D, Vector4 E, Vector4 F, Vector4 G);

public sealed record Gw2ParticleFieldEnvironmentWeight(Gw2MapEnvironment Environment, float Intensity);

public static class Gw2ParticleFieldFrame
{
    public const uint DayFlag = 0x2;
    public const uint NightFlag = 0x4;
    public const uint UnderwaterFlag = 0x8;
    public const uint SlabFlag = 0x10;
    public const uint AdditiveFlag = 0x20;
    public const uint AlphaBlendFlag = 0x40;
    public const uint SubtractiveFlag = 0x80;
    public const uint FrameBlendFlag = 0x200;
    public const int MaximumCutouts = 6;

    private const float EyeLead = 65.053825f;
    private const float EyeHeightFadeDistance = 10f;
    private const float InsideCutoutScore = 100f;
    private const float FacingScore = 65f;
    private const float NearnessScore = 35f;
    private const float NearnessDistance = 100f;
    private const float FullWeight = 1f;

    public static IReadOnlyList<Gw2ParticleFieldEnvironmentWeight> EnvironmentsFromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        var environments = new List<Gw2ParticleFieldEnvironmentWeight>();
        var zoneWeightTotal = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            zoneWeightTotal += zoneWeight.Weight;
            if (zoneWeight.Weight > 0f && zoneWeight.Zone.Data is { ParticleFields.Count: > 0 } zoneEnvironment)
                environments.Add(new(zoneEnvironment, Math.Min(zoneWeight.Weight, FullWeight)));
        }

        if (zoneWeightTotal < FullWeight && environment.ParticleFields.Count > 0) environments.Insert(0, new(environment, FullWeight - zoneWeightTotal));
        return environments;
    }

    public static uint BlendFlags(Gw2MapEnvParticleField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var blendFlags = field.Flags & (AdditiveFlag | AlphaBlendFlag | SubtractiveFlag);
        if (blendFlags != 0) return blendFlags;
        return field.Type is 2 or 3 ? AdditiveFlag : AlphaBlendFlag;
    }

    public static float Weight(Gw2MapEnvParticleField field, Vector3 eye, float waterHeight, float dayWeight, float intensity)
    {
        ArgumentNullException.ThrowIfNull(field);
        var dayNight = ((field.Flags & DayFlag) != 0 ? dayWeight : 0f) + ((field.Flags & NightFlag) != 0 ? 1f - dayWeight : 0f);
        var eyeHeightFade = 1f;
        if ((field.Flags & SlabFlag) == 0)
        {
            var height = Math.Min(MathF.Abs(eye.Z) / EyeHeightFadeDistance, 1f);
            eyeHeightFade = height * height;
        }

        if (waterHeight <= eye.Z) return (field.Flags & UnderwaterFlag) == 0 ? 0f : Math.Min(eyeHeightFade, intensity);
        return Math.Min(eyeHeightFade * dayNight, intensity);
    }

    public static Gw2ParticleFieldConstants Constants(Gw2MapEnvParticleField field, Vector3 eye, Vector3 forward, float weight, float seconds)
    {
        ArgumentNullException.ThrowIfNull(field);
        var extent = (float)field.Extent;
        var halfExtent = extent * 0.5f;
        var fieldOrigin = (eye - new Vector3(halfExtent) + (halfExtent + EyeLead) * forward) / extent;
        var direction = field.FieldDirection.LengthSquared() >= 1e-12f ? Vector3.Normalize(field.FieldDirection) : Vector3.Zero;
        var framesElapsed = field.TextureFps == 0f ? 0f : seconds / (1f / field.TextureFps);
        var frameFraction = (field.Flags & FrameBlendFlag) != 0 && field.TextureFps != 0f ? SignedFraction(framesElapsed) : 0f;
        return new(
            new(fieldOrigin, seconds),
            new(extent, weight, field.Deviation / extent, -field.Altitude),
            new(direction, field.Depth),
            new(MathF.Floor(framesElapsed), field.TextureColumns, field.TextureRows, field.Fade),
            new(field.Speed.X, field.Speed.Y, field.DeviationSpeed.X, field.DeviationSpeed.Y),
            new(field.Period.X, field.Period.Y, field.Lifetime.X, field.Lifetime.Y),
            new(0f, 0f, 0f, frameFraction));
    }

    public static IReadOnlyList<Gw2MapEnvParticleFieldCutout> SelectCutouts(IReadOnlyList<Gw2MapEnvParticleFieldCutout> cutouts, Vector3 eye, Vector3 forward)
    {
        ArgumentNullException.ThrowIfNull(cutouts);
        if (cutouts.Count <= MaximumCutouts) return cutouts;
        return cutouts.Select(cutout => (Cutout: cutout, Score: CutoutScore(cutout, eye, forward)))
            .OrderByDescending(scored => scored.Score)
            .Take(MaximumCutouts)
            .Select(scored => scored.Cutout)
            .ToArray();
    }

    private static float CutoutScore(Gw2MapEnvParticleFieldCutout cutout, Vector3 eye, Vector3 forward)
    {
        var boxEye = new Vector3(Row(cutout.TransformRowX, eye, 1f), Row(cutout.TransformRowY, eye, 1f), Row(cutout.TransformRowZ, eye, 1f));
        if (MathF.Abs(boxEye.X) <= 1f && MathF.Abs(boxEye.Y) <= 1f && MathF.Abs(boxEye.Z) <= 1f) return InsideCutoutScore;
        var distance = boxEye.Length();
        if (distance != 0f) boxEye /= distance;
        var nearness = Math.Clamp(distance / NearnessDistance * (distance / NearnessDistance), 0f, 1f);
        var boxForward = new Vector3(Row(cutout.TransformRowX, forward, 0f), Row(cutout.TransformRowY, forward, 0f), Row(cutout.TransformRowZ, forward, 0f));
        boxForward = boxForward.LengthSquared() >= 1e-12f ? Vector3.Normalize(boxForward) : Vector3.Zero;
        var facing = Math.Clamp(-Vector3.Dot(boxEye, boxForward), 0f, 1f);
        return facing * facing * FacingScore + (1f - nearness) * NearnessScore;
    }

    private static float Row(float[] row, Vector3 point, float translationWeight) =>
        row.Length < 4 ? 0f : row[0] * point.X + row[1] * point.Y + row[2] * point.Z + row[3] * translationWeight;

    private static float SignedFraction(float value) => value >= 0f ? value - MathF.Truncate(value) : -(-value - MathF.Truncate(-value));
}
