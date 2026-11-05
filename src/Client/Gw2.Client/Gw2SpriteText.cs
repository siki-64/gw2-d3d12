using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Client.Model;

namespace Gw2.Client;

public sealed class Gw2SpriteText : IDisposable
{
    private const int AtlasWidth = 512;
    private const int GlyphPadding = 1;

    private readonly Texture2D _atlas;
    private readonly Dictionary<int, Rectangle> _atlasBounds = [];

    public Gw2SpriteText(GraphicsDevice graphicsDevice, Gw2FontFace face)
    {
        Face = face;
        var x = 0;
        var y = 0;
        var rowHeight = 0;
        foreach (var glyph in face.Glyphs.OrderByDescending(glyph => glyph.Height))
        {
            if (x + glyph.Width > AtlasWidth)
            {
                x = 0;
                y += rowHeight + GlyphPadding;
                rowHeight = 0;
            }
            _atlasBounds[glyph.CodePoint] = new Rectangle(x, y, glyph.Width, glyph.Height);
            x += glyph.Width + GlyphPadding;
            rowHeight = Math.Max(rowHeight, glyph.Height);
        }

        var atlasHeight = Math.Max(1, y + rowHeight);
        var pixels = new Color[AtlasWidth * atlasHeight];
        foreach (var glyph in face.Glyphs)
        {
            var bounds = _atlasBounds[glyph.CodePoint];
            for (var row = 0; row < glyph.Height; row++)
            for (var column = 0; column < glyph.Width; column++)
            {
                var coverage = glyph.Coverage[row * glyph.Width + column];
                pixels[(bounds.Y + row) * AtlasWidth + bounds.X + column] = new Color(coverage, coverage, coverage, coverage);
            }
        }

        _atlas = new Texture2D(graphicsDevice, new TextureDescription { Width = AtlasWidth, Height = atlasHeight });
        _atlas.SetData<Color>(pixels);
    }

    public Gw2FontFace Face { get; }

    public void Draw(SpriteBatch spriteBatch, Gw2TextLine line, Vector2 cellTopLeft, Vector2 scale, Color color, float depth = 0f)
    {
        foreach (var placed in line.Glyphs)
        {
            if (!_atlasBounds.TryGetValue(placed.Glyph.CodePoint, out var source)) continue;
            var position = cellTopLeft + new Vector2(placed.X, placed.Glyph.TopOffset) * scale;
            spriteBatch.Draw(_atlas, position, source, color, 0f, Vector2.Zero, scale, SpriteEffects.None, depth);
        }
    }

    public void Dispose() => _atlas.Dispose();
}
