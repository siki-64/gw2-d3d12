using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Client.Graphics;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client;

public sealed class Gw2ParticleFieldScene : IDisposable
{
    private static readonly Color MissingTextureColor = new(255, 0, 0, 255);

    private readonly GraphicsDevice _graphicsDevice;
    private readonly GrParticleFieldPass _pass;
    private readonly Func<PackFileReference, Texture2D?> _loadTexture;
    private readonly Texture2D _missingTexture;
    private readonly Dictionary<Gw2MapEnvironment, IReadOnlyList<(Gw2MapEnvParticleField Source, GrParticleField Field)>> _fieldsByEnvironment = new(ReferenceEqualityComparer.Instance);
    private readonly List<GrParticleFieldDraw> _draws = [];
    private float _seconds;

    public Gw2ParticleFieldScene(GrDevice device, Func<PackFileReference, Texture2D?> loadTexture)
    {
        ArgumentNullException.ThrowIfNull(device);
        _graphicsDevice = device.GraphicsDevice;
        _pass = new GrParticleFieldPass(device);
        _loadTexture = loadTexture;
        _missingTexture = new Texture2D(_graphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = "ParticleFieldMissingTexture" });
        _missingTexture.SetData<Color>([MissingTextureColor]);
    }

    public void Draw(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, Matrix4x4 view, Matrix4x4 projection, Vector3 eye, Vector3 forward, float waterHeight, float dayWeight, float elapsedSeconds, Texture2D depthCopy, float cameraNear, float cameraFar)
    {
        _seconds += elapsedSeconds;
        _draws.Clear();
        foreach (var weighted in Gw2ParticleFieldFrame.EnvironmentsFromZones(environment, zoneWeights))
        {
            var cutouts = Gw2ParticleFieldFrame.SelectCutouts(weighted.Environment.ParticleFieldCutouts, eye, forward)
                .Select(cutout => new GrParticleFieldCutout(Row(cutout.TransformRowX), Row(cutout.TransformRowY), Row(cutout.TransformRowZ)))
                .ToArray();
            foreach (var (source, field) in FieldsOf(weighted.Environment))
            {
                var weight = Gw2ParticleFieldFrame.Weight(source, eye, waterHeight, dayWeight, weighted.Intensity);
                if (weight <= 0f) continue;
                _draws.Add(new GrParticleFieldDraw(field, Gw2ParticleFieldFrame.Constants(source, eye, forward, weight, _seconds), cutouts));
            }
        }

        _pass.Draw(_draws, view, projection, eye, depthCopy, cameraNear, cameraFar);
    }

    private IReadOnlyList<(Gw2MapEnvParticleField Source, GrParticleField Field)> FieldsOf(Gw2MapEnvironment environment)
    {
        if (_fieldsByEnvironment.TryGetValue(environment, out var cached)) return cached;
        var fields = new List<(Gw2MapEnvParticleField, GrParticleField)>();
        foreach (var source in environment.ParticleFields)
            if (GrParticleField.Create(_graphicsDevice, source, _loadTexture(source.Texture) ?? _missingTexture) is { } field)
                fields.Add((source, field));
        _fieldsByEnvironment[environment] = fields;
        return fields;
    }

    private static Vector4 Row(float[] row) => row.Length < 4 ? Vector4.Zero : new(row[0], row[1], row[2], row[3]);

    public void Dispose()
    {
        foreach (var fields in _fieldsByEnvironment.Values)
            foreach (var (_, field) in fields)
                field.Dispose();
        _missingTexture.Dispose();
    }
}
