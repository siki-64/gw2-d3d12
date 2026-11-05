using System;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrWaterReflection : IDisposable
{
    private static readonly Vector3 WorldUp = new(0f, 0f, -1f);

    private readonly GrDevice _device;
    private Texture2D? _target;
    private Texture2D? _depth;

    public GrWaterReflection(GrDevice device) => _device = device;

    public Texture2D EnsureTargets(int width, int height)
    {
        if (_target is null || _target.Width != width || _target.Height != height)
        {
            _target?.Dispose();
            _depth?.Dispose();
            _target = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba8Unorm, "WaterReflection");
            _depth = _device.CreateDepthTarget(width, height, "WaterReflectionDepth");
        }

        return _target;
    }

    public void Bind() => _device.SetRenderTarget(_target!, _depth);

    public static (Matrix4x4 View, Vector3 Eye) MirroredView(Matrix4x4 view, float planeZ)
    {
        Matrix4x4.Invert(view, out var camera);
        var eye = new Vector3(camera.M41, camera.M42, 2f * planeZ - camera.M43);
        var forward = new Vector3(camera.M31, camera.M32, -camera.M33);
        return (Matrix4x4.CreateLookAtLeftHanded(eye, eye + forward, WorldUp), eye);
    }

    public static Matrix4x4 ObliqueProjection(Matrix4x4 projection, Matrix4x4 mirroredView, float planeZ)
    {
        Matrix4x4.Invert(mirroredView, out var inverseView);
        var clipPlane = Vector4.Transform(new Vector4(0f, 0f, -1f, planeZ), Matrix4x4.Transpose(inverseView));
        Matrix4x4.Invert(projection, out var inverseProjection);
        var corner = Vector4.Transform(new Vector4(MathF.Sign(clipPlane.X), MathF.Sign(clipPlane.Y), 1f, 1f), inverseProjection);
        var scaled = clipPlane / Vector4.Dot(clipPlane, corner);
        var oblique = projection;
        oblique.M13 = scaled.X;
        oblique.M23 = scaled.Y;
        oblique.M33 = scaled.Z;
        oblique.M43 = scaled.W;
        return oblique;
    }

    public void Dispose()
    {
        _target?.Dispose();
        _depth?.Dispose();
    }
}
