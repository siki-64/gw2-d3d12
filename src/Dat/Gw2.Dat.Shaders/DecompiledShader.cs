namespace Gw2.Dat.Shaders;

public sealed record DecompiledShader(string Text, IReadOnlyList<string> GlobalNames);

public enum ShaderCompilerTarget
{
    Fxc,
    Dxc,
}
