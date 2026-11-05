using System;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrEngineTextures : IDisposable
{
    private const int EnvironmentCubeIndex = 1;
    private const int DepthCopyIndex = 3;
    private const int ScreenCopyIndex = 4;
    private const int ShadowMapIndex = 5;
    private const int ShadowNearIndex = 7;
    private readonly Texture2D _blackFallback;
    private readonly Texture2D _whiteFallback;
    private readonly TextureCube _blackFallbackCube;

    public GrEngineTextures(GraphicsDevice graphicsDevice)
    {
        _blackFallback = SolidTexture(graphicsDevice, Color.Transparent, "EngineBlackFallback");
        _whiteFallback = SolidTexture(graphicsDevice, Color.White, "EngineWhiteFallback");
        _blackFallbackCube = SolidCube(graphicsDevice, Color.Black);
    }

    public Texture2D BlackFallback => _blackFallback;

    public Texture2D? FarShadow { get; set; }

    public Texture2D? NearShadow { get; set; }

    public Texture2D? DepthCopy { get; set; }

    public Texture2D? ScreenCopy { get; set; }

    public Texture Resolve(int engineTextureIndex) => engineTextureIndex switch
    {
        EnvironmentCubeIndex => _blackFallbackCube,
        DepthCopyIndex => DepthCopy ?? _blackFallback,
        ScreenCopyIndex => ScreenCopy ?? _blackFallback,
        ShadowMapIndex => FarShadow ?? _whiteFallback,
        ShadowNearIndex => NearShadow ?? _blackFallback,
        _ => _blackFallback,
    };

    private static Texture2D SolidTexture(GraphicsDevice graphicsDevice, Color color, string name)
    {
        var texture = new Texture2D(graphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = name });
        texture.SetData<Color>([color]);
        return texture;
    }

    private static TextureCube SolidCube(GraphicsDevice graphicsDevice, Color color)
    {
        var cube = new TextureCube(graphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = "EngineBlackFallbackCube" });
        foreach (var face in Enum.GetValues<CubeFace>())
            cube.SetData<Color>(face, [color]);
        return cube;
    }

    public void Dispose()
    {
        _blackFallback.Dispose();
        _whiteFallback.Dispose();
        _blackFallbackCube.Dispose();
    }
}
