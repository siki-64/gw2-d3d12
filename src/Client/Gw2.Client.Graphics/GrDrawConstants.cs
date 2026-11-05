using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Dat.Content;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrDrawFrame
{
    private Matrix4x4 _view = Matrix4x4.Identity;
    private Matrix4x4 _viewProjection = Matrix4x4.Identity;
    private Matrix4x4? _projection;
    private Vector4 _windGust;
    private Vector4 _wind;
    private Gw2EnvironmentFog? _fog;
    private Gw2EnvironmentShLighting? _shLighting;
    private float _clockSeconds;
    private float _far;
    private float _near;
    private int _viewportHeight = 1;
    private int _viewportWidth = 1;
    private Vector3 _cameraPosition;

    public Matrix4x4 View
    {
        get => _view;
        set
        {
            if (_view == value) return;
            _view = value;
            _projection = null;
            Version++;
        }
    }

    public Matrix4x4 ViewProjection
    {
        get => _viewProjection;
        set
        {
            if (_viewProjection == value) return;
            _viewProjection = value;
            _projection = null;
            Version++;
        }
    }

    public long Version { get; private set; }

    public Matrix4x4 Projection => _projection ??= (Matrix4x4.Invert(_view, out var inverseView) ? inverseView : Matrix4x4.Identity) * _viewProjection;

    public Vector3 CameraPosition
    {
        get => _cameraPosition;
        set
        {
            if (EqualityComparer<Vector3>.Default.Equals(_cameraPosition, value)) return;
            _cameraPosition = value;
            Version++;
        }
    }

    public int ViewportWidth
    {
        get => _viewportWidth;
        set
        {
            if (EqualityComparer<int>.Default.Equals(_viewportWidth, value)) return;
            _viewportWidth = value;
            Version++;
        }
    }

    public int ViewportHeight
    {
        get => _viewportHeight;
        set
        {
            if (EqualityComparer<int>.Default.Equals(_viewportHeight, value)) return;
            _viewportHeight = value;
            Version++;
        }
    }

    public float Near
    {
        get => _near;
        set
        {
            if (EqualityComparer<float>.Default.Equals(_near, value)) return;
            _near = value;
            Version++;
        }
    }

    public float Far
    {
        get => _far;
        set
        {
            if (EqualityComparer<float>.Default.Equals(_far, value)) return;
            _far = value;
            Version++;
        }
    }

    public float ClockSeconds
    {
        get => _clockSeconds;
        set
        {
            if (EqualityComparer<float>.Default.Equals(_clockSeconds, value)) return;
            _clockSeconds = value;
            Version++;
        }
    }

    public Gw2EnvironmentShLighting? ShLighting
    {
        get => _shLighting;
        set
        {
            if (EqualityComparer<Gw2EnvironmentShLighting?>.Default.Equals(_shLighting, value)) return;
            _shLighting = value;
            Version++;
        }
    }

    public Gw2EnvironmentFog? Fog
    {
        get => _fog;
        set
        {
            if (EqualityComparer<Gw2EnvironmentFog?>.Default.Equals(_fog, value)) return;
            _fog = value;
            Version++;
        }
    }

    public Vector4 Wind
    {
        get => _wind;
        set
        {
            if (EqualityComparer<Vector4>.Default.Equals(_wind, value)) return;
            _wind = value;
            Version++;
        }
    }

    public Vector4 WindGust
    {
        get => _windGust;
        set
        {
            if (EqualityComparer<Vector4>.Default.Equals(_windGust, value)) return;
            _windGust = value;
            Version++;
        }
    }

    public Dictionary<uint, Vector4[]> Globals { get; } = new();

    public void SetGlobal(uint token, Vector4 value)
    {
        if (Globals.TryGetValue(token, out var values) && values.Length == 1)
        {
            if (values[0] == value) return;
            values[0] = value;
        }
        else Globals[token] = [value];
        Version++;
    }

    public void SetGlobal(uint token, Vector4[] values)
    {
        Globals[token] = values;
        Version++;
    }
}

public readonly record struct GrDrawItem(Matrix4x4 World, Vector4 ModelColor, int StencilFlags, IReadOnlyDictionary<uint, Vector4> Inputs, GrTextureTransformAnimation? TextureTransforms = null)
{
    public bool TryGetInput(uint token, float clockSeconds, out Vector4 value) =>
        (TextureTransforms is not null && TextureTransforms.TryGetInput(token, clockSeconds, out value)) || Inputs.TryGetValue(token, out value);
}

public static class GrDrawConstants
{
    private const float WorldViewProjectionInverseDepthScale = -1f / 65535f;
    private const int MatrixRegisterCount = 4;
    private static readonly Vector4 TexTransformA = Vector4.UnitX;
    private static readonly Vector4 TexTransformB = Vector4.UnitY;

    [ThreadStatic]
    private static Dictionary<ShaderProgram, (GrDrawFrame Frame, long Version)>? _appliedFrameByProgram;

    [ThreadStatic]
    private static Dictionary<int, Vector4[]>? _registerScratchByCount;

    public static void ForgetAppliedFrames() => _appliedFrameByProgram?.Clear();

    public static void Apply(GrArchiveShaderMaterial material, GrDrawFrame frame, GrDrawItem item)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(frame);
        Span<Vector4> single = stackalloc Vector4[MatrixRegisterCount];
        var appliedFrames = _appliedFrameByProgram ??= new Dictionary<ShaderProgram, (GrDrawFrame Frame, long Version)>(ReferenceEqualityComparer.Instance);
        var frameUniformsCurrent = appliedFrames.TryGetValue(material.Program, out var applied) && applied.Frame == frame && applied.Version == frame.Version;
        appliedFrames[material.Program] = (frame, frame.Version);
        foreach (var uniform in material.Uniforms)
        {
            if (frameUniformsCurrent && IsFrameLevel(uniform.Id)) continue;
            var parameter = uniform.Parameter;
            var elementCount = parameter.Elements;
            if (elementCount == 0)
            {
                single.Clear();
                Write(uniform.Id, uniform.Token, material, frame, item, single);
                SetSingle(parameter, single[0]);
                continue;
            }

            var registers = RegisterScratch(elementCount);
            Array.Clear(registers);
            Write(uniform.Id, uniform.Token, material, frame, item, registers);
            parameter.SetValue((ReadOnlySpan<Vector4>)registers);
        }
    }

    private static bool IsFrameLevel(GrDrawConstantId id) => id is GrDrawConstantId.Projection or GrDrawConstantId.View or GrDrawConstantId.ViewProjection
        or GrDrawConstantId.ShRed or GrDrawConstantId.ShGreen or GrDrawConstantId.ShBlue or GrDrawConstantId.ShSun or GrDrawConstantId.ShSunData or GrDrawConstantId.ShSunColor
        or GrDrawConstantId.CameraPosition or GrDrawConstantId.ScreenDims or GrDrawConstantId.Time or GrDrawConstantId.Wind or GrDrawConstantId.WindGust
        or GrDrawConstantId.TexelOffset or GrDrawConstantId.LightCount
        or GrDrawConstantId.FogColorNearMinusFar or GrDrawConstantId.FogColorFar or GrDrawConstantId.FogColorHeight or GrDrawConstantId.FogDepthCue
        or GrDrawConstantId.FogParam0 or GrDrawConstantId.FogParam1;

    private static void SetSingle(ShaderParameter parameter, Vector4 value)
    {
        if (parameter.Columns == 4) parameter.SetValue(value);
        else parameter.SetValue(stackalloc float[] { value.X, value.Y, value.Z, value.W }[..parameter.Columns]);
    }

    private static Vector4[] RegisterScratch(int count)
    {
        var scratchByCount = _registerScratchByCount ??= new Dictionary<int, Vector4[]>();
        if (!scratchByCount.TryGetValue(count, out var registers)) scratchByCount[count] = registers = new Vector4[count];
        return registers;
    }

    private static int Write(GrDrawConstantId id, uint token, GrArchiveShaderMaterial material, GrDrawFrame frame, GrDrawItem item, Span<Vector4> registers)
    {
        var sh = frame.ShLighting;
        var fog = frame.Fog;
        switch (id)
        {
            case GrDrawConstantId.Projection: return Rows(frame.Projection, registers);
            case GrDrawConstantId.View: return Rows(frame.View, registers);
            case GrDrawConstantId.ViewProjection: return Rows(frame.ViewProjection, registers);
            case GrDrawConstantId.World: return Rows(item.World, registers);
            case GrDrawConstantId.WorldInverse: return Rows(Inverse(item.World), registers);
            case GrDrawConstantId.WorldView: return Rows(item.World * frame.View, registers);
            case GrDrawConstantId.WorldViewProjection: return Rows(item.World * frame.ViewProjection, registers);
            case GrDrawConstantId.WorldViewProjectionInverse: return WorldViewProjectionInverse(item.World * frame.ViewProjection, registers);
            case GrDrawConstantId.ShRed: return One(sh?.ShRed, registers);
            case GrDrawConstantId.ShGreen: return One(sh?.ShGreen, registers);
            case GrDrawConstantId.ShBlue: return One(sh?.ShBlue, registers);
            case GrDrawConstantId.ShSun: return One(Vector4.Zero, registers);
            case GrDrawConstantId.ShSunData: return One(sh?.ShSunData, registers);
            case GrDrawConstantId.ShSunColor: return One(sh?.ShSunColor, registers);
            case >= GrDrawConstantId.TexTransform0A and <= GrDrawConstantId.TexTransform3B:
                return One(item.Inputs.TryGetValue(token, out var transform) || item.TryGetInput(token, frame.ClockSeconds, out transform) ? transform : ((int)id - (int)GrDrawConstantId.TexTransform0A) % 2 == 0 ? TexTransformA : TexTransformB, registers);
            case GrDrawConstantId.CameraPosition: return One(new Vector4(frame.CameraPosition.X, frame.CameraPosition.Y, frame.CameraPosition.Z, 1f), registers);
            case GrDrawConstantId.ModelColor: return One(item.ModelColor, registers);
            case GrDrawConstantId.ScreenDims: return One(GrEngineConstants.ScreenDims(frame.ViewportWidth, frame.ViewportHeight, frame.Near, frame.Far), registers);
            case GrDrawConstantId.StencilId: return One(GrEngineConstants.StencilId(item.StencilFlags), registers);
            case GrDrawConstantId.Time: return One(GrEngineConstants.Time(frame.ClockSeconds), registers);
            case GrDrawConstantId.Wind: return One(frame.Wind, registers);
            case GrDrawConstantId.WindGust: return One(frame.WindGust, registers);
            case GrDrawConstantId.TexelOffset: return One(GrEngineConstants.TexelOffset, registers);
            case GrDrawConstantId.AlphaRef: return One(new Vector4(material.RenderState.AlphaReference, 0f, 0f, 0f), registers);
            case GrDrawConstantId.LightCount: return One(Vector4.Zero, registers);
            case GrDrawConstantId.FogColorNearMinusFar: return One(fog?.FogColorNearMinusFar, registers);
            case GrDrawConstantId.FogColorFar: return One(fog?.FogColorFar, registers);
            case GrDrawConstantId.FogColorHeight: return One(fog?.FogColorHeight, registers);
            case GrDrawConstantId.FogDepthCue: return One(fog?.FogDepthCue, registers);
            case GrDrawConstantId.FogParam0: return One(fog?.FogParam0, registers);
            case GrDrawConstantId.FogParam1: return One(fog?.FogParam1, registers);
            case GrDrawConstantId.Named:
                if (item.TryGetInput(token, frame.ClockSeconds, out var input)) return One(input, registers);
                if (!frame.Globals.TryGetValue(token, out var global)) return 0;
                var count = Math.Min(global.Length, registers.Length);
                global.AsSpan(0, count).CopyTo(registers);
                return count;
            default:
                return 0;
        }
    }

    private static int One(Vector4 value, Span<Vector4> registers)
    {
        registers[0] = value;
        return 1;
    }

    private static int One(System.Numerics.Vector4? value, Span<Vector4> registers) =>
        One(value is { } vector ? new Vector4(vector.X, vector.Y, vector.Z, vector.W) : Vector4.Zero, registers);

    private static int Rows(Matrix4x4 matrix, Span<Vector4> registers)
    {
        Span<Vector4> rows =
        [
            new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41),
            new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42),
            new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43),
            new Vector4(matrix.M14, matrix.M24, matrix.M34, matrix.M44),
        ];
        var count = Math.Min(MatrixRegisterCount, registers.Length);
        rows[..count].CopyTo(registers);
        return count;
    }

    private static Matrix4x4 Inverse(Matrix4x4 matrix) =>
        Matrix4x4.Invert(matrix, out var inverse) ? inverse : Matrix4x4.Identity;

    private static int WorldViewProjectionInverse(Matrix4x4 worldViewProjection, Span<Vector4> registers)
    {
        Span<Vector4> rows = stackalloc Vector4[MatrixRegisterCount];
        Rows(Inverse(worldViewProjection), rows);
        rows[3] += rows[2] * WorldViewProjectionInverseDepthScale;
        var count = Math.Min(MatrixRegisterCount, registers.Length);
        rows[..count].CopyTo(registers);
        return count;
    }
}
