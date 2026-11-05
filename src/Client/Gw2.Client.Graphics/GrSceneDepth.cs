using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrSceneDepth
{
    private readonly GrDevice _device;
    private readonly ShaderProgram[] _programs;

    public GrSceneDepth(GrDevice device)
    {
        _device = device;
        var library = device.LoadShader("GrSceneDepth");
        _programs =
        [
            library.GetProgram("SceneDepth"),
            library.GetProgram("InstancedSceneDepth"),
            library.GetProgram("AlphaTestedSceneDepth"),
            library.GetProgram("InstancedAlphaTestedSceneDepth"),
        ];
    }

    public void SetSlopeScaledDepthBias(float bias)
    {
        foreach (var program in _programs) program.Parameters["SlopeScaledDepthBias"]?.SetValue(bias);
    }

    public void Draw(GrDepthCaster caster, GrConstants constants, Matrix4x4 viewProjection, RasterizerState rasterizerState)
    {
        var instanced = caster.Mesh is IGrInstancedMesh;
        var alphaTested = caster.AlphaMask is not null;
        var program = _programs[(instanced ? 1 : 0) + (alphaTested ? 2 : 0)];
        if (alphaTested)
        {
            program.Parameters["AlphaMask"]!.SetValue(caster.AlphaMask);
            program.Parameters["AlphaMaskSampler"]!.SetValue(caster.AlphaMaskSampler ?? _device.Sampler(SamplerDescription.LinearWrap));
        }

        constants.SetWorld(caster.World);
        constants.Apply(program, viewProjection);
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.Default, rasterizerState, caster.Mesh.VertexLayout);
        caster.Mesh.Draw(_device.GraphicsDevice);
    }
}
