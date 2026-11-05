using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Client.Graphics;
using Gw2.Client.Model;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;

namespace Gw2.Client;

public sealed class Gw2AgentNameLabels : IDisposable
{
    private const string UnnamedAgent = "";
    public const float NameplateRangeInGameUnits = 5000f;
    public const float RegularNameplateFullAtInGameUnits = 3200f;
    public const float RegularNameplateZeroAtInGameUnits = 3500f;
    public const float EmphasisedNameplateFullAtInGameUnits = 4700f;
    public const float EmphasisedNameplateZeroAtInGameUnits = 5000f;
    public const float ItemNameplateRangeInGameUnits = 575f;
    private const float NameRaiseInPixels = 32f;
    private const uint NameTypeface = 0;
    private const uint NameVariant = 0;

    private readonly GrDevice _device;
    private readonly Gw2ContentReader _reader;
    private readonly SpriteBatch _spriteBatch;
    private readonly ShaderProgram _depthTestedProgram;
    private readonly Gw2SpriteText? _font;
    private readonly Dictionary<string, string?> _textByCodedName = [];
    private readonly Dictionary<uint, string?> _textByTextId = [];
    private readonly Dictionary<string, Gw2TextLine> _lineByName = [];

    public Gw2AgentNameLabels(GrDevice device, Gw2ContentReader reader)
    {
        var graphicsDevice = device.GraphicsDevice;
        _device = device;
        _reader = reader;
        _spriteBatch = new SpriteBatch(graphicsDevice, depthStencilFormat: SurfaceFormat.Depth24UnormStencil8);
        _depthTestedProgram = device.LoadShader("GrNameLabel").GetProgram("NameLabel");
        var manifest = Gw2FontManifest.Parse(reader.ReadBytes(Gw2FontManifest.ManifestFileId));
        if (manifest.Where(entry => entry.FontIndex == NameTypeface && entry.Variant == NameVariant).MinBy(entry => entry.SizeKey) is { } entry)
            _font = new Gw2SpriteText(graphicsDevice, Gw2FontFace.Load(reader, entry));
    }

    public void CollectAnchors(IReadOnlyList<Gw2SceneCharacter> characters, Matrix4x4 viewProjection, Vector3 viewer, uint? targetAgentId, Vector2 screenSize, List<Gw2AgentScreenAnchor> anchors)
    {
        anchors.Clear();
        foreach (var character in characters)
        {
            if ((NameOf(character) ?? UnnamedAgent) is not { } name) continue;
            var anchor = character.PositionInGameUnits - new Vector3(0f, 0f, character.HeightInGameUnits);
            if (Project(anchor, viewProjection, screenSize) is not { } onScreen) continue;
            var opacity = NameplateOpacityAt(Vector3.Distance(character.PositionInGameUnits, viewer), character.AgentId == targetAgentId);
            anchors.Add(new Gw2AgentScreenAnchor(character.AgentId, name, new Vector2(onScreen.X, onScreen.Y), onScreen.Z, character.VisibleAttitude, character.IsControlled, opacity));
        }
    }

    public static float NameplateOpacityAt(float distanceInGameUnits, bool isEmphasised)
    {
        var (fullAt, zeroAt) = isEmphasised
            ? (EmphasisedNameplateFullAtInGameUnits, EmphasisedNameplateZeroAtInGameUnits)
            : (RegularNameplateFullAtInGameUnits, RegularNameplateZeroAtInGameUnits);
        return Math.Clamp((distanceInGameUnits - zeroAt) / (fullAt - zeroAt), 0f, 1f);
    }

    public void CollectNamedObjectAnchors(IReadOnlyList<Gw2SceneNamedObject> namedObjects, Matrix4x4 viewProjection, Vector3 viewer, Vector2 screenSize, List<Gw2AgentScreenAnchor> anchors)
    {
        foreach (var namedObject in namedObjects)
        {
            if (Project(namedObject.PositionInGameUnits, viewProjection, screenSize) is not { } onScreen) continue;
            var withinRange = Vector3.DistanceSquared(namedObject.PositionInGameUnits, viewer) <= namedObject.NameplateRangeInGameUnits * namedObject.NameplateRangeInGameUnits;
            anchors.Add(new Gw2AgentScreenAnchor(namedObject.AgentId, namedObject.Name, new Vector2(onScreen.X, onScreen.Y), onScreen.Z, Gw2VisibleAttitude.Indifferent, false, withinRange ? 1f : 0f));
        }
    }

    public void Draw(IReadOnlyList<Gw2AgentScreenAnchor> anchors, Texture2D? sceneDepth)
    {
        if (_font is null) return;
        var graphicsDevice = _device.GraphicsDevice;
        var screenSize = new Vector2(graphicsDevice.BackBufferWidth, graphicsDevice.BackBufferHeight);
        _device.SetBackBuffer();
        if (sceneDepth is not null)
        {
            _depthTestedProgram.Parameters["SceneDepth"]?.SetValue(sceneDepth);
            _depthTestedProgram.Parameters["SceneDepthScale"]?.SetValue(new Vector4(sceneDepth.Width / screenSize.X, sceneDepth.Height / screenSize.Y, 0f, 0f));
        }
        _spriteBatch.Begin(program: sceneDepth is null ? null : _depthTestedProgram);
        foreach (var anchor in anchors)
            if (!anchor.IsControlled && anchor.WithinNameplateRange && anchor.Name.Length > 0)
                DrawName(anchor);
        _spriteBatch.End();
    }

    private static readonly Color HaloColor = new(0, 0, 0, 128);
    private static readonly Vector2[] HaloOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0), new(1, 0),
        new(-1, 1), new(0, 1), new(1, 1),
    ];

    private void DrawName(Gw2AgentScreenAnchor anchor)
    {
        if (!_lineByName.TryGetValue(anchor.Name, out var line)) _lineByName[anchor.Name] = line = _font!.Face.Layout(anchor.Name);
        var cellTopLeft = new Vector2(anchor.Position.X - line.Width / 2f, anchor.Position.Y - NameRaiseInPixels - _font!.Face.CellHeight);
        var textTopLeft = new Vector2(MathF.Round(cellTopLeft.X), MathF.Round(cellTopLeft.Y));
        foreach (var offset in HaloOffsets)
            _font.Draw(_spriteBatch, line, textTopLeft + offset, Vector2.One, new Color(HaloColor.ToVector4() * anchor.Opacity), anchor.Depth);
        _font.Draw(_spriteBatch, line, textTopLeft, Vector2.One, new Color(Gw2NameColors.Of(anchor.VisibleAttitude) * anchor.Opacity), anchor.Depth);
    }

    public string? NameOf(Gw2SceneCharacter character)
    {
        if (character.LiteralName is { } literal) return literal;
        if (character.CodedName is not { } coded) return null;
        if (!_textByCodedName.TryGetValue(coded, out var text)) _textByCodedName[coded] = text = Resolve(coded);
        return text;
    }

    private string? Resolve(string codedName)
    {
        if (!CodedNpcName.TryToTextId(codedName, out var textId, out var key)) return null;
        return Visible(_reader.ReadText(textId, Gw2Language.English, key));
    }

    private static string? Visible((Gw2TextStatus Status, string Text) read)
    {
        if (read.Status is not (Gw2TextStatus.Plain or Gw2TextStatus.Decrypted)) return null;
        var visible = Gw2TextTemplate.WithoutTags(read.Text);
        return visible.Length == 0 ? null : visible;
    }

    private static Vector3? Project(Vector3 position, Matrix4x4 viewProjection, Vector2 screenSize)
    {
        var clip = Vector4.Transform(new Vector4(position, 1f), viewProjection);
        if (clip.W <= 0f) return null;
        var normalized = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (MathF.Abs(normalized.X) > 1f || MathF.Abs(normalized.Y) > 1f || normalized.Z is < 0f or > 1f) return null;
        return new Vector3((normalized.X + 1f) * 0.5f * screenSize.X, (1f - normalized.Y) * 0.5f * screenSize.Y, normalized.Z);
    }

    public void Dispose()
    {
        _font?.Dispose();
        _spriteBatch.Dispose();
    }
}
