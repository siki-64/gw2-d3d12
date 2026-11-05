using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public static class GrDrawTextures
{
    private static readonly ConditionalWeakTable<GrArchiveShaderMaterial, GrSamplerBindings> BindingsByMaterial = new();

    public static void Bind(GrDevice device, GrArchiveShaderMaterial material, Func<GrArchiveSampler, Texture> textureOf, bool anisotropicFiltering)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(textureOf);
        var bindings = BindingsByMaterial.GetValue(material, static material => new GrSamplerBindings(material));
        var states = bindings.States(device, anisotropicFiltering);
        var samplers = material.Samplers;
        for (var index = 0; index < samplers.Count; index++)
        {
            if (bindings.Textures[index] is { } parameter) parameter.SetValue(MatchingDimension(device, parameter, textureOf(samplers[index])));
            bindings.SamplerStates[index]?.SetValue(states[index]);
        }
    }

    private static Texture MatchingDimension(GrDevice device, ShaderParameter parameter, Texture texture) => (parameter.Value, texture) switch
    {
        (TextureCube, not TextureCube) => device.BlackFallbackCube,
        (Texture2D, TextureCube) => device.BlackFallback,
        _ => texture,
    };

    private sealed class GrSamplerBindings
    {
        private readonly GrArchiveShaderMaterial _material;
        private SamplerState[]? _plainStates;
        private SamplerState[]? _anisotropicStates;

        public GrSamplerBindings(GrArchiveShaderMaterial material)
        {
            _material = material;
            Textures = material.Samplers.Select(sampler => material.Program.Parameters[sampler.ParameterName]).ToArray();
            SamplerStates = material.Samplers.Select(sampler => material.Program.Parameters[GrShaderTextureNames.SamplerOf(sampler.ParameterName)]).ToArray();
        }

        public ShaderParameter?[] Textures { get; }

        public ShaderParameter?[] SamplerStates { get; }

        public SamplerState[] States(GrDevice device, bool anisotropicFiltering) =>
            anisotropicFiltering ? _anisotropicStates ??= Resolve(device, true, GrSamplerStates.DeviceMaxAnisotropy) : _plainStates ??= Resolve(device, false, GrSamplerStates.DefaultMaxAnisotropy);

        private SamplerState[] Resolve(GrDevice device, bool anisotropicFiltering, int maxAnisotropy) =>
            _material.Samplers
                .Select(sampler => device.Sampler(GrSamplerStates.From(anisotropicFiltering ? sampler.State.WithAnisotropicFiltering() : sampler.State, maxAnisotropy)))
                .ToArray();
    }
}
