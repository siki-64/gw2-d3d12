using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2AmatDx9Sampler(uint TextureIndex, IReadOnlyList<uint> State, bool UsesBindTexture);

public sealed record Gw2AmatDx9Shader(uint InstructionCount, IReadOnlyList<uint> ConstantTokens);

public sealed class Gw2AmatDx9Material
{
    public Gw2AmatDx9Material(
        IReadOnlyList<Gw2AmatDx9Sampler> samplers,
        IReadOnlyList<Gw2AmatDx9Shader> shaders,
        int techniqueCount,
        bool useLegacyBindTextures)
    {
        Samplers = samplers;
        Shaders = shaders;
        TechniqueCount = techniqueCount;
        UseLegacyBindTextures = useLegacyBindTextures;
    }

    public IReadOnlyList<Gw2AmatDx9Sampler> Samplers { get; }
    public IReadOnlyList<Gw2AmatDx9Shader> Shaders { get; }
    public int TechniqueCount { get; }
    public bool UseLegacyBindTextures { get; }

    public static Gw2AmatDx9Material? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("AmatDx9Material");
        if (root is null) return null;

        var samplers = new List<Gw2AmatDx9Sampler>();
        if (root.Fields.GetValueOrDefault("samplers") is object?[] samplerObjects)
            foreach (var samplerObject in samplerObjects)
                if (samplerObject is PackObject sampler)
                    samplers.Add(new(
                        PackValue.AsU32(sampler.Fields.GetValueOrDefault("textureIndex")),
                        PackValue.AsU32List(sampler.Fields.GetValueOrDefault("state")),
                        PackValue.AsU32(sampler.Fields.GetValueOrDefault("usesBindTexture")) != 0));

        var shaders = new List<Gw2AmatDx9Shader>();
        if (root.Fields.GetValueOrDefault("shaders") is object?[] shaderObjects)
            foreach (var shaderObject in shaderObjects)
                if (shaderObject is PackObject shader)
                    shaders.Add(new(
                        (uint)PackValue.AsU32(shader.Fields.GetValueOrDefault("instructionCount")),
                        PackValue.AsU32List(shader.Fields.GetValueOrDefault("constTokens"))));

        return new(
            samplers,
            shaders,
            PackValue.ArrayLength(root.Fields.GetValueOrDefault("techniques")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("useLegacyBindTextures")) != 0);
    }
}
