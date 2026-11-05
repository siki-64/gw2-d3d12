using System;
using System.Numerics;

namespace Gw2.Client.Graphics;

public sealed record GrShadowCascade(
    Matrix4x4 LightViewProjection,
    Matrix4x4 WorldToShadow,
    Vector3 BoundSphereCenter,
    float BoundSphereRadius,
    float SplitDistance,
    float Bias);

public static class GrShadowCascades
{
    private const float EyeOffsetFraction = 0.75f;
    private const float VerticalSunThreshold = 0.95f;
    private const float RadiusStepsPerDoubling = 16f;

    public static GrShadowCascade[] Fit(GrShadowSettings settings, Matrix4x4 cameraView, Matrix4x4 cameraProjection, float cameraNear, float cameraFar, Vector3 towardSun)
    {
        Matrix4x4.Invert(cameraView, out var cameraWorld);
        var apex = cameraWorld.Translation;
        var farCorners = new[]
        {
            FarCorner(cameraWorld, cameraProjection, cameraFar, -1f, 1f),
            FarCorner(cameraWorld, cameraProjection, cameraFar, 1f, 1f),
            FarCorner(cameraWorld, cameraProjection, cameraFar, 1f, -1f),
            FarCorner(cameraWorld, cameraProjection, cameraFar, -1f, -1f),
        };
        var lightDirection = -Vector3.Normalize(towardSun);
        var cascades = new GrShadowCascade[settings.CascadeCount];
        var splitDistances = new float[settings.CascadeCount];
        for (var index = 0; index < settings.CascadeCount; index++)
        {
            var nearFraction = SplitFraction(settings, index, cameraNear, cameraFar);
            var farFraction = SplitFraction(settings, index + 1, cameraNear, cameraFar);
            splitDistances[index] = cameraFar * farFraction;
            var slice = new Vector3[8];
            for (var corner = 0; corner < 4; corner++)
            {
                slice[corner] = apex + (farCorners[corner] - apex) * nearFraction;
                slice[corner + 4] = apex + (farCorners[corner] - apex) * farFraction;
            }

            var (center, radius) = BoundSphere(apex, slice);
            var (view, projection) = LightMatrices(settings, center, radius, lightDirection);
            var lightViewProjection = view * projection;
            cascades[index] = new GrShadowCascade(lightViewProjection, lightViewProjection * AtlasColumn(index, settings.CascadeCount), center, radius, splitDistances[index], 0f);
        }

        for (var index = 0; index < cascades.Length; index++)
            cascades[index] = cascades[index] with { Bias = settings.SampleBias * splitDistances[index] / splitDistances[0] };
        return cascades;
    }

    public static float SplitFraction(GrShadowSettings settings, int split, float cameraNear, float cameraFar)
    {
        var distance = Math.Min(settings.MaximumShadowDistance, cameraFar);
        var position = (float)split / settings.CascadeCount;
        var logarithmic = cameraNear * MathF.Pow(distance / cameraNear, position);
        var linear = cameraNear + (distance - cameraNear) * position;
        return ((linear - logarithmic) * settings.CascadeSplitBlend + logarithmic) / cameraFar;
    }

    public static (Vector3 Center, float Radius) BoundSphere(Vector3 apex, Vector3[] slice)
    {
        var farCenter = (slice[4] + slice[6]) * 0.5f;
        var edgeMidpoint = (slice[0] + slice[4]) * 0.5f;
        var viewRay = Vector3.Normalize(farCenter - apex);
        var edgeDirection = Vector3.Normalize(edgeMidpoint - apex);
        var alongRay = Vector3.Dot(edgeDirection, edgeMidpoint - apex) / Vector3.Dot(edgeDirection, viewRay);
        var center = apex + viewRay * alongRay;
        if (Vector3.DistanceSquared(center, apex) > Vector3.DistanceSquared(farCenter, apex))
            center = farCenter;
        var radius = 0f;
        foreach (var point in slice) radius = Math.Max(radius, Vector3.Distance(center, point));
        return (center, radius);
    }

    public static float SteppedRadius(float radius) =>
        MathF.Pow(2f, MathF.Ceiling(MathF.Log2(radius) * RadiusStepsPerDoubling) / RadiusStepsPerDoubling);

    private static (Matrix4x4 View, Matrix4x4 Projection) LightMatrices(GrShadowSettings settings, Vector3 center, float radius, Vector3 lightDirection)
    {
        var up = MathF.Abs(lightDirection.Z) >= VerticalSunThreshold ? Vector3.UnitY : -Vector3.UnitZ;
        if (settings.PixelSnapCascades)
        {
            radius = SteppedRadius(radius);
            var rotation = Matrix4x4.CreateLookAtLeftHanded(Vector3.Zero, lightDirection, up);
            Matrix4x4.Invert(rotation, out var inverseRotation);
            var texelsPerUnit = settings.CascadeResolution / (2f * radius);
            var lightSpaceCenter = Vector3.Transform(center, rotation);
            lightSpaceCenter.X = MathF.Floor(lightSpaceCenter.X * texelsPerUnit) / texelsPerUnit;
            lightSpaceCenter.Y = MathF.Floor(lightSpaceCenter.Y * texelsPerUnit) / texelsPerUnit;
            center = Vector3.Transform(lightSpaceCenter, inverseRotation);
        }

        var eye = center - lightDirection * settings.LightRange * EyeOffsetFraction;
        var view = Matrix4x4.CreateLookAtLeftHanded(eye, center, up);
        var projection = new Matrix4x4(
            1f / radius, 0f, 0f, 0f,
            0f, 1f / radius, 0f, 0f,
            0f, 0f, 1f / settings.LightRange, 0f,
            0f, 0f, 0f, 1f);
        return (view, projection);
    }

    private static Matrix4x4 AtlasColumn(int index, int cascadeCount)
    {
        var halfColumn = 0.5f / cascadeCount;
        return new Matrix4x4(
            halfColumn, 0f, 0f, 0f,
            0f, -0.5f, 0f, 0f,
            0f, 0f, 1f, 0f,
            (2 * index + 1) * halfColumn, 0.5f, 0f, 1f);
    }

    private static Vector3 FarCorner(Matrix4x4 cameraWorld, Matrix4x4 cameraProjection, float cameraFar, float x, float y)
    {
        var viewDirection = new Vector3(x * cameraFar / cameraProjection.M11, y * cameraFar / cameraProjection.M22, cameraFar * cameraProjection.M34);
        return cameraWorld.Translation + Vector3.TransformNormal(viewDirection, cameraWorld);
    }
}
