using System.IO;
using Crystal.Graphics;
using Gw2.Client.Model;

namespace Gw2.Client.Debug;

public static class Screenshots
{
    public static void Save(GraphicsDevice graphicsDevice, string path)
    {
        var width = graphicsDevice.BackBufferWidth;
        var height = graphicsDevice.BackBufferHeight;
        var rgba = new byte[width * height * 4];
        graphicsDevice.GetBackBufferData<byte>(rgba);
        for (var alpha = 3; alpha < rgba.Length; alpha += 4) rgba[alpha] = byte.MaxValue;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Gw2PngEncoder.EncodeRgba(width, height, rgba));
    }
}
