using System;
using System.Collections.Generic;
using Crystal.Graphics;
using Gw2.Client.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Hud;

public sealed class HudTextures : IDisposable
{
    private readonly GrDevice _device;
    private readonly Gw2ContentReader _reader;
    private readonly Dictionary<uint, Texture2D?> _byFileId = [];

    public HudTextures(GrDevice device, Gw2ContentReader reader)
    {
        _device = device;
        _reader = reader;
    }

    public Texture2D? Of(uint fileId)
    {
        if (_byFileId.TryGetValue(fileId, out var cached)) return cached;
        var uploaded = _reader.ReadTexture(fileId) is { } asset ? GrTextureUpload.FromAsset(_device.GraphicsDevice, asset) : null;
        _byFileId[fileId] = uploaded;
        return uploaded;
    }

    public void Dispose()
    {
        foreach (var texture in _byFileId.Values) texture?.Dispose();
        _byFileId.Clear();
    }
}
