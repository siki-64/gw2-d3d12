using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;
using Gw2.Client.Graphics;
using Gw2.Client.Hud;
using Gw2.Client.Net.Game;
using Gw2.Dat.Content;

namespace Gw2.Client;

public readonly record struct Gw2NameplateHealth(float Fraction, bool IsDowned);

public readonly record struct Gw2HealthBarColors(uint Fill, uint Frame);

public sealed class Gw2NameplateHealthBars : IDisposable
{
    public const float BarWidthInPixels = 81f;
    public const float BarHeightInPixels = 5.25f;
    private const float BarRaiseInPixels = 32f;
    private const int PlainShader = 3;
    private const int ColourBandWindowShader = 0x1C;
    private const uint FrameFile = 0x262FD;
    private const uint FillColourFile = 0x262FE;
    private const uint FillRampFile = 0x262FF;

    public static readonly Gw2HealthBarColors Hostile = new(0xFFBB2A17, 0xFF401508);
    public static readonly Gw2HealthBarColors HostileDowned = new(0xFF64140A, 0xFF401508);
    public static readonly Gw2HealthBarColors Friendly = new(0xFF6AC951, 0xFF0D2501);
    public static readonly Gw2HealthBarColors FriendlyDowned = new(0xFF0F5A0A, 0xFF0D2501);

    private static readonly HudArtwork Artwork = new(256, 32, 15, 7, 231, 21);

    private readonly GrDevice _device;
    private readonly HudTextures _textures;
    private readonly GrUiQuads _quads;

    public Gw2NameplateHealthBars(GrDevice device, Gw2ContentReader reader)
    {
        _device = device;
        _textures = new HudTextures(device, reader);
        _quads = new GrUiQuads(device);
    }

    public static Gw2HealthBarColors ColorsOf(Gw2VisibleAttitude visibleAttitude, bool isDowned) =>
        visibleAttitude is Gw2VisibleAttitude.Hostile or Gw2VisibleAttitude.Indifferent or Gw2VisibleAttitude.IndifferentWhiteName
            ? isDowned ? HostileDowned : Hostile
            : isDowned ? FriendlyDowned : Friendly;

    public static GrScreenRect BarRectOf(Vector2 anchor)
    {
        var left = MathF.Round(anchor.X - BarWidthInPixels / 2f);
        var top = MathF.Round(anchor.Y - BarRaiseInPixels);
        return new(left, top, left + BarWidthInPixels, top + BarHeightInPixels);
    }

    public void Draw(IReadOnlyList<Gw2AgentScreenAnchor> anchors, Func<uint, Gw2NameplateHealth?> healthOf, Texture2D? sceneDepth)
    {
        _device.SetBackBuffer();
        foreach (var anchor in anchors)
        {
            if (anchor.IsControlled || !anchor.WithinNameplateRange || healthOf(anchor.AgentId) is not { } health) continue;
            var colors = ColorsOf(anchor.VisibleAttitude, health.IsDowned);
            var art = Artwork.Covering(BarRectOf(anchor.Position));
            _quads.Draw(Quad(art, PlainShader, [FrameFile], Vector4.Zero, WithOpacity(colors.Frame, anchor.Opacity)), sceneDepth, anchor.Depth);
            _quads.Draw(Quad(art, ColourBandWindowShader, [FillColourFile, FillRampFile], new Vector4(0f, Math.Clamp(health.Fraction, 0f, 1f), 0f, 0f), WithOpacity(colors.Fill, anchor.Opacity)), sceneDepth, anchor.Depth);
        }
    }

    public static uint WithOpacity(uint argb, float opacity) =>
        ((uint)MathF.Round((argb >> 24) * Math.Clamp(opacity, 0f, 1f)) << 24) | (argb & 0x00FFFFFF);

    private GrUiQuad Quad(GrScreenRect rect, int uiShader, uint[] textureFileIds, Vector4 control, uint argb) =>
        new(rect, uiShader, Array.ConvertAll(textureFileIds, _textures.Of), [], HudComposer.ColorOf(argb), control);

    public void Dispose()
    {
        _quads.Dispose();
        _textures.Dispose();
    }
}
