using System.Globalization;
using System.Text;
using Gw2.Dat.Content;
using Gw2.Dat.Shaders;

namespace Gw2.DatSurvey;

public sealed record ShaderSourceFile(string FileName, string Text);

public static class ShaderEffectSource
{
    public static IReadOnlyList<ShaderSourceFile> Write(Gw2AmatMaterial material, string fileStem)
    {
        ArgumentNullException.ThrowIfNull(material);
        var files = new List<ShaderSourceFile>();
        var effect = new StringBuilder();
        var entryPoints = new Dictionary<int, string>();
        for (var index = 0; index < material.Shaders.Count; index++)
        {
            var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
            if (binary is null || Gw2BgfxShaderBinary.TryParse(binary.Data) is not { } shader) continue;
            var prefix = StagePrefix(material, index);
            var fileName = $"{fileStem}_{prefix}.hlsl";
            var decompiled = ShaderDecompiler.DecompileShader(shader, ShaderCompilerTarget.Dxc);
            files.Add(new(fileName, decompiled.Text));
            entryPoints[index] = $"{prefix}_main";

            foreach (var name in decompiled.GlobalNames) effect.Append(CultureInfo.InvariantCulture, $"#define {name} {prefix}_{name}\n");
            effect.Append(CultureInfo.InvariantCulture, $"#include \"{fileName}\"\n");
            foreach (var name in decompiled.GlobalNames) effect.Append(CultureInfo.InvariantCulture, $"#undef {name}\n");
            effect.Append('\n');
        }

        for (var techniqueIndex = 0; techniqueIndex < material.Techniques.Count; techniqueIndex++)
        {
            var technique = material.Techniques[techniqueIndex];
            for (var passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
                foreach (var pass in technique.Passes[passIndex].Effects)
                    foreach (var variant in pass.VertexShaderVariants)
                    {
                        if (!entryPoints.TryGetValue(checked((int)variant.VertexShaderIndex), out var vertexEntry) ||
                            !entryPoints.TryGetValue(checked((int)pass.PixelShaderIndex), out var pixelEntry)) continue;
                        effect.Append(CultureInfo.InvariantCulture, $"technique {TechniqueName(techniqueIndex, technique, passIndex, pass, variant)}\n{{\n    pass P0\n    {{\n");
                        effect.Append(CultureInfo.InvariantCulture, $"        VertexShader = compile vs_6_0 {vertexEntry}();\n");
                        effect.Append(CultureInfo.InvariantCulture, $"        PixelShader = compile ps_6_0 {pixelEntry}();\n    }}\n}}\n\n");
                    }
        }

        files.Add(new($"{fileStem}.fx", effect.ToString()));
        return files;
    }

    public static string StagePrefix(Gw2AmatMaterial material, int shaderIndex) =>
        $"{(material.Shaders[shaderIndex].IsPixelShader ? "ps" : "vs")}{shaderIndex.ToString(CultureInfo.InvariantCulture)}";

    public static string TechniqueName(int techniqueIndex, Gw2AmatTechnique technique, int passIndex, Gw2AmatEffect effect, Gw2AmatVertexShaderVariant variant) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Technique{techniqueIndex}{QualityNamePart(technique)}Pass{passIndex}Effect{effect.Token:X}Variant{variant.Variant}");

    private static string QualityNamePart(Gw2AmatTechnique technique) =>
        technique.QualityName.Length == 0 ? string.Empty : char.ToUpperInvariant(technique.QualityName[0]) + technique.QualityName[1..];
}
