using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Scene;

public sealed class Gw2WaterConstants
{
    private const float BumpAmountScale = 1f / 1024f;
    private const int TileSizeDivisor = 32;

    public static readonly uint WaveScrollToken = Token.Encode("wtrprmd");
    public static readonly uint TileToken = Token.Encode("wtrprmv");
    public static readonly uint PatternTileToken = Token.Encode("wtrprme");
    public static readonly uint BumpToken = Token.Encode("wtrprmp");
    public static readonly uint ShallowColorToken = Token.Encode("wtrclrs");
    public static readonly uint DeepColorToken = Token.Encode("wtrclrd");
    public static readonly uint PatternColorToken = Token.Encode("wtrclrp");

    private readonly float _tileSize;
    private Vector4 _bumpScroll;
    private Vector2 _patternScroll;

    public Gw2WaterConstants(float tileSize) => _tileSize = tileSize;

    public Vector4 BumpScroll => _bumpScroll;

    public Vector2 PatternScroll => _patternScroll;

    public void Advance(float elapsedSeconds, Gw2MapEnvWaterInfo water)
    {
        var bumpAngle0 = water.BumpAngles[0] * MathF.PI / 180f;
        var bumpAngle1 = water.BumpAngles[1] * MathF.PI / 180f;
        var patternAngle = water.PatternAngle * MathF.PI / 180f;
        var bump0 = elapsedSeconds * water.BumpSpeeds[0] * water.BumpTiles[0] / _tileSize;
        var bump1 = elapsedSeconds * water.BumpSpeeds[1] * water.BumpTiles[1] / _tileSize;
        var pattern = elapsedSeconds * water.PatternSpeed * water.PatternTile / _tileSize;
        _bumpScroll = new Vector4(
            Wrap(_bumpScroll.X + MathF.Cos(bumpAngle0) * bump0),
            Wrap(_bumpScroll.Y + MathF.Sin(bumpAngle0) * bump0),
            Wrap(_bumpScroll.Z + MathF.Cos(bumpAngle1) * bump1),
            Wrap(_bumpScroll.W + MathF.Sin(bumpAngle1) * bump1));
        _patternScroll = new Vector2(
            Wrap(_patternScroll.X + MathF.Cos(patternAngle) * pattern),
            Wrap(_patternScroll.Y + MathF.Sin(patternAngle) * pattern));
    }

    public Dictionary<uint, Vector4> Pack(Gw2MapEnvWaterInfo water)
    {
        var constants = new Dictionary<uint, Vector4>();
        for (var index = 0; index < water.ConstantTokens.Count && index < water.ConstantValues.Count; index++)
        {
            var value = water.ConstantValues[index];
            constants[water.ConstantTokens[index]] = value.Length < 4 ? Vector4.Zero : new Vector4(value[0], value[1], value[2], value[3]);
        }
        var tileScale = 1f / (TileSizeDivisor * _tileSize);
        constants[WaveScrollToken] = _bumpScroll;
        constants[TileToken] = new Vector4(water.BumpTiles[0] * tileScale, water.BumpTiles[1] * tileScale, _patternScroll.X, _patternScroll.Y);
        constants[PatternTileToken] = new Vector4(water.PatternTile * tileScale, 0f, 0f, 0f);
        constants[BumpToken] = new Vector4(water.BumpScales[0], water.BumpScales[1], water.BumpAmount * BumpAmountScale, water.SurfaceFresnel);
        constants[ShallowColorToken] = Color(water.ShallowColor, water.PatternEdge);
        constants[DeepColorToken] = Color(water.DeepColor, 0f);
        var patternColor = Color(water.PatternColor, water.PatternColor.Length > 3 ? water.PatternColor[3] / 255f : 0f);
        constants[PatternColorToken] = new Vector4(patternColor.X * 2f, patternColor.Y * 2f, patternColor.Z * 2f, patternColor.W);
        return constants;
    }

    private static float Wrap(float value) => value % 1f;

    private static Vector4 Color(IReadOnlyList<byte> blueGreenRed, float alpha) =>
        blueGreenRed.Count < 3 ? new Vector4(0f, 0f, 0f, alpha) : new Vector4(blueGreenRed[2] / 255f, blueGreenRed[1] / 255f, blueGreenRed[0] / 255f, alpha);
}
