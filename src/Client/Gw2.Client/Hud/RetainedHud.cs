using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Client.Model;
using Gw2.Dat.Archive;
using Gw2.Client.Frames;
using Gw2.Client.Graphics;
using Gw2.Client.Hud.Professions;
using Gw2.Client.Ui;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Hud;

public sealed class RetainedHud : IDisposable
{
    private static readonly Vector2[] OutlineOffsets = [new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f)];
    private const int ShadowHaloRadius = 7;
    private static readonly Color ShadowHaloColor = new(0, 0, 0, 70);
    private static readonly Vector2[] ShadowHaloOffsets = HaloOffsets(ShadowHaloRadius);

    private static Vector2[] HaloOffsets(int radius)
    {
        var offsets = new List<Vector2>();
        for (var y = -radius; y <= radius; y += 2)
            for (var x = -radius; x <= radius; x += 2)
                if (x * x + y * y <= radius * radius) offsets.Add(new Vector2(x, y));
        return [.. offsets];
    }
    private const int WeaponSlotCount = 5;
    private const int BarSlotCount = 5;
    private const int MechanicSlotCount = 5;
    private const float SlotSize = 52f;
    private const float SlotGap = 3f;
    private const float SlotBottom = 22f;
    private const float RowHeight = 74f;
    private const float ProfessionPanelBottom = RowHeight + 24f;
    private const float ProfessionPanelGapFromCentre = 13f;
    private const float CentreWidgetSize = 101f;
    private const float CentreWidgetBottom = 13f;
    private const float EnduranceBaseSize = CentreWidgetSize;
    private const float EnduranceSmallInterfaceScale = 1.15f;
    private const float EnduranceSize = EnduranceSmallInterfaceScale * EnduranceBaseSize + 2f * HudEnduranceArc.Inset;
    private const float EnduranceBottom = CentreWidgetBottom + CentreWidgetSize / 2f - EnduranceSize / 2f;
    private const int UiRootChildIndex = 0;
    private const int EnduranceChildIndex = 1;
    private const int WeaponRowChildIndex = 2;
    private const int BarRowChildIndex = 3;
    private const int ProfessionPanelChildIndex = 6;
    private const int WeaponSwapChildIndex = 7;
    private const int CastBarChildIndex = 8;
    private const float CastBarGapAboveProfessionPanel = 4f;
    private const int ProfessionResourceChildIndex = 0;
    private const int OrbChildIndex = 0;
    private const int FirstSlotChildIndex = 3;

    private readonly FrameTree _tree;
    private readonly HudTextures _textures;
    private readonly GrUiQuads _quads;
    private readonly HudComposer _composer;
    private readonly List<HudWidget> _widgets = [];
    private readonly List<HudSkillSlot> _professionSlots = [];
    private readonly HudProfessionMechanics _professionMechanics;
    private const uint HudTypeface = 0;
    private const uint HudVariant = 0;
    private readonly SpriteBatch _textBatch;
    private readonly Dictionary<HudTextStyle, Gw2SpriteText> _fonts = [];

    public RetainedHud(GrDevice device, Gw2ContentReader reader)
    {
        var graphicsDevice = device.GraphicsDevice;
        _tree = new FrameTree(graphicsDevice.BackBufferWidth, graphicsDevice.BackBufferHeight);
        _textures = new HudTextures(device, reader);
        _quads = new GrUiQuads(device);
        _composer = new HudComposer(_textures, graphicsDevice.BackBufferHeight);
        var configNumbers = new Gw2ConfigNumbers(reader.ContentIndex);
        _professionMechanics = new HudProfessionMechanics(new HolosmithHeatThresholds((configNumbers.NumberOf(Gw2ConfigNumbers.HolosmithOverheatPercentKey) ?? 0f) / 100f, configNumbers.NumberOf(Gw2ConfigNumbers.HolosmithHeatPenaltyKey) ?? 0f, configNumbers.NumberOf(Gw2ConfigNumbers.HolosmithHeatBonusKey) ?? 0f));
        _textBatch = new SpriteBatch(graphicsDevice, depthStencilFormat: SurfaceFormat.Depth24UnormStencil8);
        var manifest = Gw2FontManifest.Parse(reader.ReadBytes(Gw2FontManifest.ManifestFileId));
        foreach (var style in Enum.GetValues<HudTextStyle>())
            if (manifest.FirstOrDefault(entry => entry.FontIndex == HudTypeface && entry.Variant == HudVariant && entry.SizeKey == (ulong)style) is { } fontEntry)
                _fonts[style] = new Gw2SpriteText(graphicsDevice, Gw2FontFace.Load(reader, fontEntry));
        Build();
    }

    private void Build()
    {
        var uiRoot = _tree.CreateChild(null, FrameCreationFlags.FillsParent, UiRootChildIndex);

        var endurance = Add(uiRoot, EnduranceChildIndex, new HudEnduranceArc());
        Centre(endurance, EnduranceSize);
        endurance.Anchor = FrameAnchor.Top;
        endurance.Margins = endurance.Margins with { Top = EnduranceBottom, Bottom = EnduranceBottom + EnduranceSize };

        var orb = Add(endurance, OrbChildIndex, new HudHealthOrb());
        orb.Anchor = FrameAnchor.None;
        orb.Margins = new FrameMargins(CentreWidgetSize / 2f, CentreWidgetSize / 2f, CentreWidgetSize / 2f, CentreWidgetSize / 2f);

        var rowWidth = WeaponSlotCount * SlotSize + (WeaponSlotCount - 1) * SlotGap;
        var halfCentre = CentreWidgetSize / 2f;
        var weaponRow = Row(uiRoot, WeaponRowChildIndex, halfCentre + rowWidth, -halfCentre, 0f, RowHeight);
        AddSlots(weaponRow, 0, WeaponSlotCount, SlotSize, SlotSize + SlotGap, SlotBottom);
        var weaponSwapRight = halfCentre + rowWidth + HudWeaponSwap.GapFromWeaponRow;
        var weaponSwap = Add(uiRoot, WeaponSwapChildIndex, new HudWeaponSwap());
        weaponSwap.Anchor = FrameAnchor.Top;
        weaponSwap.Margins = new FrameMargins(weaponSwapRight + HudWeaponSwap.Width, RowHeight - HudWeaponSwap.DropFromRowTop, -weaponSwapRight, RowHeight - HudWeaponSwap.DropFromRowTop + HudWeaponSwap.Height);
        var barRow = Row(uiRoot, BarRowChildIndex, -halfCentre, halfCentre + rowWidth, 0f, RowHeight);
        AddSlots(barRow, WeaponSlotCount, BarSlotCount, SlotSize, SlotSize + SlotGap, SlotBottom);
        var panelRight = halfCentre + ProfessionPanelGapFromCentre;
        var professionPanel = Row(uiRoot, ProfessionPanelChildIndex, panelRight + HudProfessionMechanic.PanelWidth, -panelRight, ProfessionPanelBottom, ProfessionPanelBottom + HudProfessionMechanic.PanelHeight);
        var resource = Add(professionPanel, ProfessionResourceChildIndex, new HudProfessionResource(_professionMechanics));
        resource.Anchor = FrameAnchor.Left | FrameAnchor.Top;
        resource.Margins = new FrameMargins(0f, 0f, HudProfessionMechanic.PanelWidth, HudProfessionMechanic.PanelHeight);
        for (var index = 0; index < MechanicSlotCount; index++)
        {
            var slot = Add(professionPanel, FirstSlotChildIndex + index, new HudSkillSlot(WeaponSlotCount + BarSlotCount + index));
            slot.Anchor = FrameAnchor.Left | FrameAnchor.Top;
            _professionSlots.Add(slot);
        }

        var castBarBottom = ProfessionPanelBottom + HudProfessionMechanic.PanelHeight + CastBarGapAboveProfessionPanel;
        var castBar = Add(uiRoot, CastBarChildIndex, new HudCastBar());
        Centre(castBar, HudCastBar.Width);
        castBar.Anchor = FrameAnchor.Top;
        castBar.Margins = castBar.Margins with { Top = castBarBottom, Bottom = castBarBottom + HudCastBar.Height };
    }

    private Frame Row(Frame parent, int childIndex, float leftOfCentre, float rightOfCentre, float bottom, float top)
    {
        var row = _tree.CreateChild(parent, FrameCreationFlags.None, childIndex);
        row.Anchor = FrameAnchor.Top;
        row.Margins = new FrameMargins(leftOfCentre, bottom, rightOfCentre, top);
        return row;
    }

    private void AddSlots(Frame row, int firstStateSlot, int count, float size, float step, float bottom)
    {
        for (var index = 0; index < count; index++)
        {
            var slot = Add(row, FirstSlotChildIndex + index, new HudSkillSlot(firstStateSlot + index));
            slot.Anchor = FrameAnchor.Left | FrameAnchor.Top;
            slot.Margins = new FrameMargins(index * step, bottom, index * step + size, bottom + size);
        }
    }

    private static void Centre(Frame frame, float size) =>
        frame.Margins = new FrameMargins(size / 2f, frame.Margins.Top, size / 2f, frame.Margins.Bottom);

    private T Add<T>(Frame parent, int childIndex, T widget) where T : HudWidget
    {
        _tree.CreateChild(parent, FrameCreationFlags.None, childIndex, widget);
        _widgets.Add(widget);
        return widget;
    }

    public void Draw(HudState state, float screenWidth, float screenHeight)
    {
        if (screenWidth != _tree.ScreenWidth || screenHeight != _tree.ScreenHeight) _tree.Resize(screenWidth, screenHeight);
        _composer.ScreenHeight = screenHeight;
        _composer.Texts.Clear();
        PlaceProfessionSlots(state);
        _tree.UpdateScreenRects();
        foreach (var widget in _widgets)
            if (!widget.IsHidden) widget.Compose(_composer, state);

        foreach (var op in FrameOpBuilder.Build(_tree, FrameDrawList.Build(_tree)))
        {
            switch (op)
            {
                case FrameSetClipOp clip:
                    _quads.SetClip(new GrScreenRect(clip.Clip.Left, screenHeight - clip.Clip.Bottom, clip.Clip.Right, screenHeight - clip.Clip.Top));
                    break;
                case FrameDrawModelsOp models:
                    foreach (var entry in models.Models)
                        if (entry.Model is GrUiQuad quad) _quads.Draw(quad);
                    break;
            }
        }
        _quads.ResetClip();
        DrawTexts();
    }

    private void DrawTexts()
    {
        if (_composer.Texts.Count == 0) return;
        _textBatch.Begin();
        foreach (var text in _composer.Texts)
        {
            if (!_fonts.TryGetValue(text.Style, out var font)) continue;
            var line = font.Face.Layout(text.Text);
            var centre = new Vector2((text.Rect.Left + text.Rect.Right) / 2f, (text.Rect.Top + text.Rect.Bottom) / 2f);
            var cellTopLeft = new Vector2(MathF.Round(centre.X - line.Width / 2f), MathF.Round(centre.Y - font.Face.CellHeight / 2f));
            if (text.HasShadowHalo)
                foreach (var offset in ShadowHaloOffsets) font.Draw(_textBatch, line, cellTopLeft + offset, Vector2.One, ShadowHaloColor);
            foreach (var offset in OutlineOffsets) font.Draw(_textBatch, line, cellTopLeft + offset, Vector2.One, Color.Black);
            font.Draw(_textBatch, line, cellTopLeft, Vector2.One, Color.White);
        }
        _textBatch.End();
    }

    private void PlaceProfessionSlots(HudState state)
    {
        var rects = _professionMechanics.For(state).SlotRectsOf(state);
        for (var index = 0; index < _professionSlots.Count; index++)
        {
            var slot = _professionSlots[index];
            var rect = index < rects.Count ? rects[index] : null;
            slot.IsHidden = rect is null || !state.ProfessionSlotHasSkill(index);
            if (rect is { } placed) slot.Margins = new FrameMargins(placed.Left, placed.Bottom, placed.Left + placed.Width, placed.Bottom + placed.Height);
        }
    }

    public void Dispose()
    {
        _quads.Dispose();
        foreach (var font in _fonts.Values) font.Dispose();
        _textures.Dispose();
    }
}
