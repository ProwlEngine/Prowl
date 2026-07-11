// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Canvas = Prowl.Quill.Canvas;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Dock window for editing a texture's sprites. Sprite config lives in the texture's <c>.meta</c>
/// (via <see cref="TextureSpriteMeta"/>). In Multiple mode the canvas is a full authoring surface
/// (drag to make rects, move/resize, slice); in Single mode there is one fixed full-texture rect and only
/// the border + pivot are editable. Rects use corner + whole-edge resize handles, green border handles, and
/// a draggable pivot. All edits are undoable; Save writes the meta and reimports.
/// </summary>
[EditorWindow("Tools/Sprite Editor")]
public class SpriteEditorWindow : DockPanel
{
    private enum DragMode { None, Create, Move, ResizeRect, MovePivot, MoveBorder }

    private const float HandleDraw = 7f;
    private const float HandleHit = 8f;
    private const float EdgeHit = 5f;
    private const float PivotRadius = 6f;
    private const float PivotHit = 9f;

    private Guid _textureGuid;
    private AssetRef<Texture2D> _texture;
    private SpriteEditTarget _target = new();
    private SpriteImportSettings _settings => _target.Settings;

    private readonly Canvas2DView _view = new();
    private int _selected = -1;
    private bool _needsFrame = true;

    private bool _slicingOpen;
    private float _slicingBtnX, _slicingBtnY;

    private float _cvX, _cvY, _cvW, _cvH;

    private DragMode _drag = DragMode.None;
    private int _resizeHandle = -1;
    private int _borderSide = -1;
    private Float4 _startRect;
    private Float2 _startContent;
    private Float2 _createStart, _createEnd;
    private EditSnapshot? _dragBefore;
    private bool _dragChanged;

    public override string Title => $"Sprite: {_texture.Res?.Name ?? "?"}";
    public override string Icon => EditorIcons.Image;

    private bool IsSingle => _settings.Mode == SpriteMode.Single;

    // --- Open / persistence ----------------------------------------------------------

    public static void OpenFor(Guid textureGuid)
    {
        var panel = new SpriteEditorWindow();
        panel.Load(textureGuid);
        EditorApplication.Instance?.OpenPanelInstance(panel, 1100, 720);
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_textureGuid == Guid.Empty) return false;
        state["texture"] = _textureGuid.ToString();
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        if (Guid.TryParse(state["texture"]?.GetValue<string>(), out Guid guid))
            Load(guid);
    }

    private void Load(Guid textureGuid)
    {
        _textureGuid = textureGuid;
        _texture = new AssetRef<Texture2D>(textureGuid);
        _texture.EnsureLoaded();
        _target = SpriteEditRegistry.Get(textureGuid);
        _selected = -1;
        _needsFrame = true;
        EnsureSingleSlice();
    }

    // The Sprite Editor doesn't persist - it edits the shared settings instance and flags it dirty; the
    // texture inspector's Save & Reimport does the write. Revert reloads the shared instance from disk.
    private void RevertChanges()
    {
        // Register the reload as an undo step, like any other edit (but without Mutate's forced
        // Dirty = true, since Reload already correctly clears it). Without this, the prior Mutate
        // records already on the (global) undo stack are unaware the settings were just reloaded,
        // so a Ctrl+Z would replay a stale pre-revert snapshot and silently resurrect the edits
        // Revert just discarded.
        EditSnapshot before = Capture();
        SpriteEditRegistry.Reload(_textureGuid);
        EditSnapshot after = Capture();
        Undo.RegisterAction("Revert Sprite Changes", () => Restore(before), () => Restore(after));

        _selected = -1;
        EnsureSingleSlice();
    }

    // Single mode always has exactly one full-texture slice; keep pivot/border of any existing one.
    private void EnsureSingleSlice()
    {
        if (!IsSingle || _texture.Res is not Texture2D tex) return;
        var slice = _settings.Slices.Count > 0 ? _settings.Slices[0] : new SpriteSliceData();
        slice.Name = tex.Name;
        slice.Rect = new SpriteRect(0, 0, (int)tex.Width, (int)tex.Height);
        _settings.Slices = new List<SpriteSliceData> { slice };
        _selected = 0;
    }

    private bool Valid(int i) => i >= 0 && i < _settings.Slices.Count;

    // --- Undo -----------------------------------------------------------------------

    private sealed class EditSnapshot
    {
        public List<SpriteSliceData> Slices = new();
        public float PixelsPerUnit;
        public bool GenerateTightMesh;
        public float TightMeshDetail;
        public byte TightMeshAlphaThreshold;
    }

    private static SpriteSliceData CloneSlice(SpriteSliceData s) => new()
    {
        Name = s.Name, Rect = s.Rect, Alignment = s.Alignment,
        CustomPivot = s.CustomPivot, PivotUnit = s.PivotUnit, Border = s.Border,
    };

    private EditSnapshot Capture() => new()
    {
        Slices = _settings.Slices.ConvertAll(CloneSlice),
        PixelsPerUnit = _settings.PixelsPerUnit,
        GenerateTightMesh = _settings.GenerateTightMesh,
        TightMeshDetail = _settings.TightMeshDetail,
        TightMeshAlphaThreshold = _settings.TightMeshAlphaThreshold,
    };

    private void Restore(EditSnapshot s)
    {
        _settings.Slices = s.Slices.ConvertAll(CloneSlice);
        _settings.PixelsPerUnit = s.PixelsPerUnit;
        _settings.GenerateTightMesh = s.GenerateTightMesh;
        _settings.TightMeshDetail = s.TightMeshDetail;
        _settings.TightMeshAlphaThreshold = s.TightMeshAlphaThreshold;
        if (_selected >= _settings.Slices.Count) _selected = _settings.Slices.Count - 1;
        _target.Dirty = true;
    }

    private void Mutate(string desc, Action change, bool coalesce = false)
    {
        EditSnapshot before = Capture();
        change();
        EditSnapshot after = Capture();
        if (coalesce)
            Undo.RegisterCoalescableAction(desc, () => Restore(before), () => Restore(after));
        else
            Undo.RegisterAction(desc, () => Restore(before), () => Restore(after));
        _target.Dirty = true;
    }

    // --- Root ------------------------------------------------------------------------

    public override void OnGUI(Paper paper, float width, float height)
    {
        if (EditorTheme.DefaultFont == null) return;
        EnsureSingleSlice();

        using (paper.Column("se_root").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
        {
            DrawToolbar(paper);
            paper.Box("se_hdiv").Width(UnitValue.Stretch()).Height(1)
                .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            using (paper.Row("se_body").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                DrawCanvas(paper);
                paper.Box("se_vdiv").Width(1).Height(UnitValue.Stretch())
                    .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
                DrawSidebar(paper, height);
            }
        }

        if (IsSingle) _slicingOpen = false;
        if (_slicingOpen)
            DrawSlicingPopover(paper);

        if ((Input.GetMouseButton(1) || Input.GetMouseButton(2)) && PointerInCanvas(paper))
        {
            _view.PanBy(paper.PointerDelta);
            ClampPan();
        }

        if (Input.GetKeyDown(KeyCode.Delete) && !IsSingle && _drag == DragMode.None && PointerInCanvas(paper) && Valid(_selected))
            DeleteSelected();
    }

    private bool PointerInCanvas(Paper paper)
    {
        Float2 p = paper.PointerPos;
        return p.X >= _cvX && p.X <= _cvX + _cvW && p.Y >= _cvY && p.Y <= _cvY + _cvH;
    }

    private void DeleteSelected()
    {
        int idx = _selected;
        Mutate("Delete Sprite", () =>
        {
            _settings.Slices.RemoveAt(idx);
            _selected = Math.Min(idx, _settings.Slices.Count - 1);
        });
    }

    // --- Toolbar + slicing popup -----------------------------------------------------

    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("se_toolbar").Width(UnitValue.Stretch()).Height(36)
            .Padding(8, 4).ColBetween(6).Enter())
        {
            if (!IsSingle)
            {
                using (paper.Box("se_slicing_wrap").Width(UnitValue.Auto).Height(UnitValue.Stretch())
                    .Margin(UnitValue.Auto, UnitValue.StretchOne)
                    .OnPostLayout((h, r) => { _slicingBtnX = (float)r.Min.X; _slicingBtnY = (float)r.Max.Y; })
                    .Enter())
                    Origami.Button(paper, "se_slicing", $"{EditorIcons.BorderAll}  Slicing", () => _slicingOpen = !_slicingOpen).Show();
            }

            paper.Box("se_spacer").Width(UnitValue.Stretch());

            Origami.Button(paper, "se_revert", "Revert", RevertChanges).Ghost().Disabled(!_target.Dirty).Show();
        }
    }

    // Anchored popover (ColorField-style) under the Slicing button: labeled settings + a Slice button.
    private void DrawSlicingPopover(Paper paper)
    {
        paper.Box("se_slice_backdrop")
            .PositionType(PositionType.SelfDirected).Position(0, 0)
            .Size(UnitValue.Stretch(), UnitValue.Stretch())
            .Layer(Layer.Overlay)
            .OnClick(_ => _slicingOpen = false);

        using (paper.Column("se_slice_pop")
            .PositionType(PositionType.SelfDirected).Position(_slicingBtnX, _slicingBtnY + 4)
            .Width(300).Height(UnitValue.Auto)
            .Layer(Layer.Overlay + 1)
            .BackgroundColor(Origami.Current.Popover)
            .BorderColor(System.Drawing.Color.FromArgb(255, 60, 62, 72)).BorderWidth(1).Rounded(6)
            .Padding(8).ColBetween(6)
            .StopEventPropagation()
            .Enter())
        {
            DrawSlicingSettings(paper);
            Origami.Button(paper, "se_slice_do", $"{EditorIcons.BorderAll}  Slice",
                () => Mutate("Slice Sprites", RunSliceCore)).Primary().FullWidth().Show();
        }
    }

    private void DrawSlicingSettings(Paper paper)
    {
        EditorGUI.Row(paper, "sp_mode", "Mode", () =>
            Origami.EnumDropdown<SpriteSlicingTool>(paper, "sp_mode_v", _settings.SlicingTool,
                v => { _settings.SlicingTool = v; _target.Dirty = true; }).Show());

        EditorGUI.Row(paper, "sp_pivot", "Generated Pivot", () =>
            Origami.EnumDropdown<SpriteAlignment>(paper, "sp_pivot_v", _settings.GeneratedPivot,
                v => { _settings.GeneratedPivot = v; _target.Dirty = true; }).Show());

        switch (_settings.SlicingTool)
        {
            case SpriteSlicingTool.GridBySize:
                Int2Row(paper, "sp_cell", "Pixel Size", _settings.GridCellSize, v => _settings.GridCellSize = v);
                Int2Row(paper, "sp_off", "Offset", _settings.GridOffset, v => _settings.GridOffset = v);
                Int2Row(paper, "sp_pad", "Padding", _settings.GridPadding, v => _settings.GridPadding = v);
                BoolRow(paper, "sp_keep", "Keep Empty Rects", _settings.KeepEmptyRects, v => _settings.KeepEmptyRects = v);
                break;
            case SpriteSlicingTool.GridByCount:
                Int2Row(paper, "sp_count", "Column / Row Count", _settings.GridCellCount, v => _settings.GridCellCount = v);
                Int2Row(paper, "sp_off", "Offset", _settings.GridOffset, v => _settings.GridOffset = v);
                Int2Row(paper, "sp_pad", "Padding", _settings.GridPadding, v => _settings.GridPadding = v);
                BoolRow(paper, "sp_keep", "Keep Empty Rects", _settings.KeepEmptyRects, v => _settings.KeepEmptyRects = v);
                break;
            case SpriteSlicingTool.Isometric:
                Int2Row(paper, "sp_cell", "Pixel Size", _settings.GridCellSize, v => _settings.GridCellSize = v);
                Int2Row(paper, "sp_off", "Offset", _settings.GridOffset, v => _settings.GridOffset = v);
                BoolRow(paper, "sp_keep", "Keep Empty Rects", _settings.KeepEmptyRects, v => _settings.KeepEmptyRects = v);
                BoolRow(paper, "sp_alt", "Is Alternate", _settings.IsoIsAlternate, v => _settings.IsoIsAlternate = v);
                break;
            case SpriteSlicingTool.Automatic:
                Origami.Label(paper, "sp_auto", "Detects rects from connected opaque (alpha) regions.").Show();
                break;
        }
    }

    private void Int2Row(Paper paper, string id, string label, Int2 value, Action<Int2> set)
        => EditorGUI.Row(paper, id, label, () =>
            Origami.Int2Field(paper, $"{id}_v", value, v => { set(v); _target.Dirty = true; }).Show());

    private void BoolRow(Paper paper, string id, string label, bool value, Action<bool> set)
        => EditorGUI.Row(paper, id, label, () =>
            Origami.Checkbox(paper, $"{id}_v", value, v => { set(v); _target.Dirty = true; }).Show());

    private void RunSliceCore()
    {
        if (_texture.Res is not Texture2D tex) return;
        int texW = (int)tex.Width, texH = (int)tex.Height;
        byte[]? alpha = (_settings.SlicingTool == SpriteSlicingTool.Automatic || !_settings.KeepEmptyRects)
            ? SpriteSlicer.ReadAlpha(tex) : null;
        _settings.Slices = SpriteSlicer.Slice(_settings, texW, texH, alpha, tex.Name);
        _selected = -1;
    }

    // --- Canvas ----------------------------------------------------------------------

    private void DrawCanvas(Paper paper)
    {
        using (paper.Box("se_canvas").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .BackgroundColor(System.Drawing.Color.FromArgb(255, 24, 25, 29)).Clip()
            .Cursor(HoverCursor(paper))
            .OnScroll(e =>
            {
                float factor = e.Delta > 0 ? Canvas2DView.ZoomStep : 1f / Canvas2DView.ZoomStep;
                _view.ZoomBy(factor, e.RelativePosition);
                ClampPan();
            })
            .OnClick(_ => ClickSelect(paper))
            .OnDragStart(_ => DragStart(paper))
            .OnDragging(_ => DragUpdate(paper))
            .OnDragEnd(_ => DragEnd())
            .OnPostLayout((handle, rect) =>
            {
                _cvX = (float)rect.Min.X; _cvY = (float)rect.Min.Y;
                _cvW = (float)rect.Size.X; _cvH = (float)rect.Size.Y;

                if (_needsFrame && _texture.Res is Texture2D t && _cvW > 1 && _cvH > 1)
                {
                    _view.Frame(t.Width, t.Height, new Float2(_cvW, _cvH));
                    ClampPan();
                    _needsFrame = false;
                }

                paper.Draw(ref handle, (canvas, r) =>
                    DrawContent(canvas, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y));
            })
            .Enter())
        {
        }
    }

    // --- Coordinate helpers ----------------------------------------------------------

    private Float2 ContentAt(Paper paper)
    {
        Float2 p = paper.PointerPos;
        return _view.CanvasToContent(new Float2(p.X - _cvX, p.Y - _cvY));
    }

    private Float2 ScreenOf(Float2 content) => new Float2(_cvX, _cvY) + _view.ContentToCanvas(content);

    private static Float4 DisplayRect(SpriteRect rc, int texH) => new(rc.X, texH - rc.MaxY, rc.Width, rc.Height);

    private static SpriteRect ToSpriteRect(float dx, float dy, float dw, float dh, int texH)
    {
        int rx = (int)MathF.Round(dx), ry = (int)MathF.Round(dy);
        int rw = Math.Max(1, (int)MathF.Round(dw)), rh = Math.Max(1, (int)MathF.Round(dh));
        return new SpriteRect(rx, texH - ry - rh, rw, rh);
    }

    private static void ClampDisplay(ref float dx, ref float dy, ref float dw, ref float dh, int texW, int texH)
    {
        dw = Math.Clamp(dw, 1, texW);
        dh = Math.Clamp(dh, 1, texH);
        dx = Math.Clamp(dx, 0, texW - dw);
        dy = Math.Clamp(dy, 0, texH - dh);
    }

    private static Float2 ClampContent(Float2 c, int texW, int texH)
        => new(Math.Clamp(c.X, 0, texW), Math.Clamp(c.Y, 0, texH));

    private Float2 NormalizedPivot(SpriteSliceData s)
    {
        if (s.Alignment != SpriteAlignment.Custom)
            return Sprite.PivotFromAlignment(s.Alignment);
        if (s.PivotUnit == PivotUnitMode.Pixels)
            return new Float2(s.CustomPivot.X / Math.Max(1, s.Rect.Width), s.CustomPivot.Y / Math.Max(1, s.Rect.Height));
        return s.CustomPivot;
    }

    private Float2 PivotContent(SpriteSliceData s, int texH)
    {
        Float2 n = NormalizedPivot(s);
        return new Float2(s.Rect.X + n.X * s.Rect.Width, texH - (s.Rect.Y + n.Y * s.Rect.Height));
    }

    // Handle content positions. Corners: 0=TL,2=TR,4=BR,6=BL. Edge-mids (for hit math): 1,3,5,7.
    private static Float2 HandleContent(Float4 dr, int i) => i switch
    {
        0 => new Float2(dr.X, dr.Y),
        1 => new Float2(dr.X + dr.Z * 0.5f, dr.Y),
        2 => new Float2(dr.X + dr.Z, dr.Y),
        3 => new Float2(dr.X + dr.Z, dr.Y + dr.W * 0.5f),
        4 => new Float2(dr.X + dr.Z, dr.Y + dr.W),
        5 => new Float2(dr.X + dr.Z * 0.5f, dr.Y + dr.W),
        6 => new Float2(dr.X, dr.Y + dr.W),
        _ => new Float2(dr.X, dr.Y + dr.W * 0.5f),
    };

    // Green border-handle positions along each edge (side: 0=L,1=R,2=T,3=B), inset by the current border.
    private static Float2 BorderHandleContent(Float4 dr, Float4 b, int side) => side switch
    {
        0 => new Float2(dr.X + b.X, dr.Y + dr.W * 0.5f),          // Left
        1 => new Float2(dr.X + dr.Z - b.Z, dr.Y + dr.W * 0.5f),   // Right
        2 => new Float2(dr.X + dr.Z * 0.5f, dr.Y + b.Y),          // Top
        _ => new Float2(dr.X + dr.Z * 0.5f, dr.Y + dr.W - b.W),   // Bottom
    };

    // --- Interaction -----------------------------------------------------------------

    private void ClickSelect(Paper paper)
    {
        if (_drag != DragMode.None || IsSingle) return;
        if (_texture.Res is not Texture2D tex) return;
        _selected = HitBody(ContentAt(paper), (int)tex.Height);
    }

    private void DragStart(Paper paper)
    {
        if (_texture.Res is not Texture2D tex) { _drag = DragMode.None; return; }
        int texW = (int)tex.Width, texH = (int)tex.Height;
        Float2 content = ContentAt(paper);
        Float2 pointer = paper.PointerPos;

        _dragBefore = Capture();
        _dragChanged = false;

        if (Valid(_selected))
        {
            SpriteSliceData s = _settings.Slices[_selected];

            if (Dist(ScreenOf(PivotContent(s, texH)), pointer) <= PivotHit)
            {
                if (s.Alignment != SpriteAlignment.Custom)
                {
                    s.CustomPivot = Sprite.PivotFromAlignment(s.Alignment);
                    s.PivotUnit = PivotUnitMode.Normalized;
                    s.Alignment = SpriteAlignment.Custom;
                }
                _drag = DragMode.MovePivot;
                return;
            }

            int bside = HitBorderHandle(pointer, s, texH);
            if (bside >= 0) { _drag = DragMode.MoveBorder; _borderSide = bside; return; }

            if (!IsSingle)
            {
                int corner = HitCorner(pointer, s.Rect, texH);
                int handle = corner >= 0 ? corner : HitEdge(pointer, s.Rect, texH);
                if (handle >= 0)
                {
                    _drag = DragMode.ResizeRect;
                    _resizeHandle = handle;
                    _startRect = DisplayRect(s.Rect, texH);
                    return;
                }
            }
        }

        if (IsSingle) { _drag = DragMode.None; return; }

        int body = HitBody(content, texH);
        if (body >= 0)
        {
            _selected = body;
            _drag = DragMode.Move;
            _startRect = DisplayRect(_settings.Slices[body].Rect, texH);
            _startContent = content;
            return;
        }

        _createStart = _createEnd = ClampContent(content, texW, texH);
        _drag = DragMode.Create;
    }

    private void DragUpdate(Paper paper)
    {
        if (_texture.Res is not Texture2D tex) return;
        int texW = (int)tex.Width, texH = (int)tex.Height;
        Float2 content = ContentAt(paper);

        switch (_drag)
        {
            case DragMode.Create:
                _createEnd = ClampContent(content, texW, texH);
                break;

            case DragMode.Move when Valid(_selected):
            {
                Float2 delta = content - _startContent;
                float dx = _startRect.X + delta.X, dy = _startRect.Y + delta.Y, dw = _startRect.Z, dh = _startRect.W;
                ClampDisplay(ref dx, ref dy, ref dw, ref dh, texW, texH);
                _settings.Slices[_selected].Rect = ToSpriteRect(dx, dy, dw, dh, texH);
                _dragChanged = true;
                break;
            }

            case DragMode.ResizeRect when Valid(_selected):
            {
                Float2 c = ClampContent(content, texW, texH);
                float left = _startRect.X, top = _startRect.Y, right = _startRect.X + _startRect.Z, bottom = _startRect.Y + _startRect.W;
                if (_resizeHandle is 0 or 6 or 7) left = c.X;
                if (_resizeHandle is 2 or 3 or 4) right = c.X;
                if (_resizeHandle is 0 or 1 or 2) top = c.Y;
                if (_resizeHandle is 4 or 5 or 6) bottom = c.Y;

                float dx = MathF.Min(left, right), dy = MathF.Min(top, bottom);
                float dw = MathF.Max(1, MathF.Abs(right - left)), dh = MathF.Max(1, MathF.Abs(bottom - top));
                ClampDisplay(ref dx, ref dy, ref dw, ref dh, texW, texH);
                _settings.Slices[_selected].Rect = ToSpriteRect(dx, dy, dw, dh, texH);
                _dragChanged = true;
                break;
            }

            case DragMode.MovePivot when Valid(_selected):
            {
                SpriteSliceData s = _settings.Slices[_selected];
                SpriteRect rc = s.Rect;
                float normX = (content.X - rc.X) / Math.Max(1, rc.Width);
                float normY = ((texH - content.Y) - rc.Y) / Math.Max(1, rc.Height);
                s.CustomPivot = s.PivotUnit == PivotUnitMode.Pixels
                    ? new Float2(normX * rc.Width, normY * rc.Height)
                    : new Float2(normX, normY);
                _dragChanged = true;
                break;
            }

            case DragMode.MoveBorder when Valid(_selected):
            {
                SpriteSliceData s = _settings.Slices[_selected];
                Float4 dr = DisplayRect(s.Rect, texH);
                Float4 b = s.Border;
                switch (_borderSide)
                {
                    case 0: b.X = Math.Clamp(content.X - dr.X, 0, dr.Z - b.Z); break;
                    case 1: b.Z = Math.Clamp(dr.X + dr.Z - content.X, 0, dr.Z - b.X); break;
                    case 2: b.Y = Math.Clamp(content.Y - dr.Y, 0, dr.W - b.W); break;
                    default: b.W = Math.Clamp(dr.Y + dr.W - content.Y, 0, dr.W - b.Y); break;
                }
                s.Border = b;
                _dragChanged = true;
                break;
            }
        }
    }

    private void DragEnd()
    {
        string? desc = null;

        if (_drag == DragMode.Create && _texture.Res is Texture2D tex)
        {
            int texW = (int)tex.Width, texH = (int)tex.Height;
            float dx = MathF.Min(_createStart.X, _createEnd.X), dy = MathF.Min(_createStart.Y, _createEnd.Y);
            float dw = MathF.Abs(_createEnd.X - _createStart.X), dh = MathF.Abs(_createEnd.Y - _createStart.Y);
            if (dw >= 2 && dh >= 2)
            {
                ClampDisplay(ref dx, ref dy, ref dw, ref dh, texW, texH);
                _settings.Slices.Add(new SpriteSliceData
                {
                    Name = $"{tex.Name}_{_settings.Slices.Count}",
                    Rect = ToSpriteRect(dx, dy, dw, dh, texH),
                    Alignment = _settings.GeneratedPivot,
                    CustomPivot = Sprite.PivotFromAlignment(_settings.GeneratedPivot),
                    PivotUnit = PivotUnitMode.Normalized,
                });
                _selected = _settings.Slices.Count - 1;
                desc = "Create Sprite";
            }
        }
        else if (_dragChanged)
        {
            desc = _drag switch
            {
                DragMode.Move => "Move Sprite",
                DragMode.ResizeRect => "Resize Sprite",
                DragMode.MovePivot => "Move Pivot",
                DragMode.MoveBorder => "Edit Border",
                _ => null,
            };
        }

        if (desc != null && _dragBefore != null)
        {
            EditSnapshot before = _dragBefore, after = Capture();
            Undo.RegisterAction(desc, () => Restore(before), () => Restore(after));
            _target.Dirty = true;
        }

        _drag = DragMode.None;
        _dragChanged = false;
        _dragBefore = null;
    }

    private int HitBody(Float2 content, int texH)
    {
        for (int i = _settings.Slices.Count - 1; i >= 0; i--)
        {
            Float4 dr = DisplayRect(_settings.Slices[i].Rect, texH);
            if (content.X >= dr.X && content.X <= dr.X + dr.Z && content.Y >= dr.Y && content.Y <= dr.Y + dr.W)
                return i;
        }
        return -1;
    }

    private static readonly int[] s_corners = { 0, 2, 4, 6 };

    private int HitCorner(Float2 pointer, SpriteRect rc, int texH)
    {
        Float4 dr = DisplayRect(rc, texH);
        foreach (int i in s_corners)
            if (Dist(ScreenOf(HandleContent(dr, i)), pointer) <= HandleHit)
                return i;
        return -1;
    }

    // Whole-edge hit: near an edge line, within its span. Returns 7=L,3=R,1=T,5=B.
    private int HitEdge(Float2 pointer, SpriteRect rc, int texH)
    {
        Float4 dr = DisplayRect(rc, texH);
        Float2 tl = ScreenOf(HandleContent(dr, 0)), tr = ScreenOf(HandleContent(dr, 2));
        Float2 br = ScreenOf(HandleContent(dr, 4)), bl = ScreenOf(HandleContent(dr, 6));

        if (MathF.Abs(pointer.X - tl.X) <= EdgeHit && Between(pointer.Y, tl.Y, bl.Y)) return 7;
        if (MathF.Abs(pointer.X - tr.X) <= EdgeHit && Between(pointer.Y, tr.Y, br.Y)) return 3;
        if (MathF.Abs(pointer.Y - tl.Y) <= EdgeHit && Between(pointer.X, tl.X, tr.X)) return 1;
        if (MathF.Abs(pointer.Y - bl.Y) <= EdgeHit && Between(pointer.X, bl.X, br.X)) return 5;
        return -1;
    }

    private int HitBorderHandle(Float2 pointer, SpriteSliceData s, int texH)
    {
        Float4 dr = DisplayRect(s.Rect, texH);
        for (int side = 0; side < 4; side++)
            if (Dist(ScreenOf(BorderHandleContent(dr, s.Border, side)), pointer) <= HandleHit)
                return side;
        return -1;
    }

    private static bool Between(float v, float a, float b) => v >= MathF.Min(a, b) - EdgeHit && v <= MathF.Max(a, b) + EdgeHit;

    private static float Dist(Float2 a, Float2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void ClampPan()
    {
        if (_texture.Res is not Texture2D tex) return;
        float cw = tex.Width * _view.Zoom, ch = tex.Height * _view.Zoom;
        Float2 p = _view.Pan;
        p.X = Math.Clamp(p.X, MathF.Min(0, _cvW - cw), MathF.Max(0, _cvW - cw));
        p.Y = Math.Clamp(p.Y, MathF.Min(0, _cvH - ch), MathF.Max(0, _cvH - ch));
        _view.Pan = p;
    }

    // OS cursor shape for whatever the pointer is over (or the active drag).
    private PaperCursor HoverCursor(Paper paper)
    {
        if (_texture.Res is not Texture2D tex || !PointerInCanvas(paper)) return PaperCursor.Default;
        int texH = (int)tex.Height;
        Float2 pointer = paper.PointerPos;

        if (_drag != DragMode.None)
            return _drag switch
            {
                DragMode.MovePivot => PaperCursor.Grabbing,
                DragMode.Move => PaperCursor.ResizeAll,
                DragMode.Create => PaperCursor.Crosshair,
                DragMode.MoveBorder => _borderSide is 0 or 1 ? PaperCursor.ResizeHorizontal : PaperCursor.ResizeVertical,
                DragMode.ResizeRect => ResizeCursor(_resizeHandle),
                _ => PaperCursor.Default,
            };

        if (Valid(_selected))
        {
            SpriteSliceData s = _settings.Slices[_selected];
            if (Dist(ScreenOf(PivotContent(s, texH)), pointer) <= PivotHit) return PaperCursor.Grab;

            int bside = HitBorderHandle(pointer, s, texH);
            if (bside >= 0) return bside is 0 or 1 ? PaperCursor.ResizeHorizontal : PaperCursor.ResizeVertical;

            if (!IsSingle)
            {
                int corner = HitCorner(pointer, s.Rect, texH);
                if (corner >= 0) return ResizeCursor(corner);
                int edge = HitEdge(pointer, s.Rect, texH);
                if (edge >= 0) return ResizeCursor(edge);
            }
        }

        if (!IsSingle)
        {
            Float2 content = _view.CanvasToContent(new Float2(pointer.X - _cvX, pointer.Y - _cvY));
            return HitBody(content, texH) >= 0 ? PaperCursor.ResizeAll : PaperCursor.Crosshair;
        }
        return PaperCursor.Default;
    }

    private static PaperCursor ResizeCursor(int handle) => handle switch
    {
        0 or 4 => PaperCursor.ResizeNWSE, // TL / BR
        2 or 6 => PaperCursor.ResizeNESW, // TR / BL
        1 or 5 => PaperCursor.ResizeVertical, // T / B edge
        _ => PaperCursor.ResizeHorizontal,   // L / R edge (3, 7)
    };

    // --- Drawing ---------------------------------------------------------------------

    private void DrawContent(Canvas canvas, float ox, float oy, float ow, float oh)
    {
        DrawCheckerboard(canvas, ox, oy, ow, oh);

        if (_texture.Res is not Texture2D tex)
        {
            canvas.DrawText("Texture not loaded", ox + 12, oy + 12, new Color32(200, 200, 200, 200), 14, EditorTheme.DefaultFont!);
            return;
        }

        int texW = (int)tex.Width, texH = (int)tex.Height;

        // Draw the texture in screen space with a flipped V (textures are stored Y-up), the same brush idiom
        // the scene view uses for its render target. Matches how the slice rects are placed via ScreenOf.
        Float2 imgTL = ScreenOf(Float2.Zero);
        float imgW = texW * _view.Zoom, imgH = texH * _view.Zoom;
        canvas.SetBrushTexture(tex);
        canvas.SetBrushTextureTransform(
            Prowl.Vector.Spatial.Transform2D.CreateTranslation(imgTL.X, imgTL.Y + imgH) *
            Prowl.Vector.Spatial.Transform2D.CreateScale(imgW, -imgH));
        canvas.RectFilled(imgTL.X, imgTL.Y, imgW, imgH, new Color32(255, 255, 255, 255));
        canvas.ClearBrushTexture();

        for (int i = 0; i < _settings.Slices.Count; i++)
        {
            Float4 dr = DisplayRect(_settings.Slices[i].Rect, texH);
            Float2 tl = ScreenOf(new Float2(dr.X, dr.Y));
            StrokeRectShadowed(canvas, tl.X, tl.Y, dr.Z * _view.Zoom, dr.W * _view.Zoom);
        }

        if (_drag == DragMode.Create)
            DrawCreatePreview(canvas);

        if (Valid(_selected))
            DrawSelectionOverlays(canvas, _settings.Slices[_selected], texH);
    }

    private void DrawCreatePreview(Canvas canvas)
    {
        float dx = MathF.Min(_createStart.X, _createEnd.X), dy = MathF.Min(_createStart.Y, _createEnd.Y);
        float dw = MathF.Abs(_createEnd.X - _createStart.X), dh = MathF.Abs(_createEnd.Y - _createStart.Y);
        if (dw < 1 || dh < 1) return;

        Float2 tl = ScreenOf(new Float2(dx, dy));
        float sw = dw * _view.Zoom, sh = dh * _view.Zoom;
        var accent = ToC32(EditorTheme.Purple400);
        canvas.RectFilled(tl.X, tl.Y, sw, sh, new Color32(accent.R, accent.G, accent.B, 48));
        StrokeRectShadowed(canvas, tl.X, tl.Y, sw, sh);
    }

    private void DrawSelectionOverlays(Canvas canvas, SpriteSliceData s, int texH)
    {
        var white = new Color32(255, 255, 255, 255);
        var shadow = new Color32(0, 0, 0, 180);
        var green = new Color32(90, 220, 130, 255);
        var accent = ToC32(EditorTheme.Purple400);

        Float4 dr = DisplayRect(s.Rect, texH);

        // Corner handles (resize) - Multiple mode only.
        if (!IsSingle)
        {
            foreach (int i in s_corners)
                DrawHandleSquare(canvas, ScreenOf(HandleContent(dr, i)), white, shadow);
        }

        // Border lines + green border handles.
        DrawBorderLines(canvas, dr, s.Border, green);
        for (int side = 0; side < 4; side++)
            DrawHandleSquare(canvas, ScreenOf(BorderHandleContent(dr, s.Border, side)), green, shadow);

        // Pivot.
        Float2 pv = ScreenOf(PivotContent(s, texH));
        canvas.CircleFilled(pv.X + 1, pv.Y + 1, PivotRadius, shadow);
        canvas.CircleFilled(pv.X, pv.Y, PivotRadius, accent);
        canvas.SetStrokeColor(white);
        canvas.SetStrokeWidth(1.5f);
        canvas.BeginPath();
        canvas.MoveTo(pv.X - PivotRadius, pv.Y); canvas.LineTo(pv.X + PivotRadius, pv.Y);
        canvas.MoveTo(pv.X, pv.Y - PivotRadius); canvas.LineTo(pv.X, pv.Y + PivotRadius);
        canvas.Stroke();
    }

    private void DrawBorderLines(Canvas canvas, Float4 dr, Float4 b, Color32 green)
    {
        canvas.SetStrokeColor(green);
        canvas.SetStrokeWidth(1f);
        Float2 tl = ScreenOf(new Float2(dr.X, dr.Y));
        float w = dr.Z * _view.Zoom, h = dr.W * _view.Zoom, z = _view.Zoom;
        canvas.BeginPath();
        if (b.X > 0) { float x = tl.X + b.X * z; canvas.MoveTo(x, tl.Y); canvas.LineTo(x, tl.Y + h); }
        if (b.Z > 0) { float x = tl.X + w - b.Z * z; canvas.MoveTo(x, tl.Y); canvas.LineTo(x, tl.Y + h); }
        if (b.Y > 0) { float y = tl.Y + b.Y * z; canvas.MoveTo(tl.X, y); canvas.LineTo(tl.X + w, y); }
        if (b.W > 0) { float y = tl.Y + h - b.W * z; canvas.MoveTo(tl.X, y); canvas.LineTo(tl.X + w, y); }
        canvas.Stroke();
    }

    private static void DrawHandleSquare(Canvas canvas, Float2 p, Color32 fill, Color32 shadow)
    {
        canvas.RectFilled(p.X - HandleDraw * 0.5f + 1, p.Y - HandleDraw * 0.5f + 1, HandleDraw, HandleDraw, shadow);
        canvas.RectFilled(p.X - HandleDraw * 0.5f, p.Y - HandleDraw * 0.5f, HandleDraw, HandleDraw, fill);
    }

    private static void StrokeRectShadowed(Canvas canvas, float x, float y, float w, float h)
    {
        canvas.SetStrokeWidth(1f);
        canvas.SetStrokeColor(new Color32(0, 0, 0, 200));
        canvas.BeginPath(); canvas.Rect(x + 1, y + 1, w, h); canvas.Stroke();
        canvas.SetStrokeColor(new Color32(255, 255, 255, 235));
        canvas.BeginPath(); canvas.Rect(x, y, w, h); canvas.Stroke();
    }

    private static void DrawCheckerboard(Canvas canvas, float x, float y, float w, float h)
    {
        var a = new Color32(46, 47, 52, 255);
        var b = new Color32(38, 39, 44, 255);
        canvas.RectFilled(x, y, w, h, b);
        const float tile = 10f;
        int cols = (int)(w / tile) + 1, rows = (int)(h / tile) + 1;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                if (((row + col) & 1) != 0) continue;
                float tx = x + col * tile, ty = y + row * tile;
                float tw = MathF.Min(tile, x + w - tx), th = MathF.Min(tile, y + h - ty);
                if (tw > 0 && th > 0) canvas.RectFilled(tx, ty, tw, th, a);
            }
    }

    private static Color32 ToC32(System.Drawing.Color c) => new(c.R, c.G, c.B, c.A);

    // --- Sidebar ---------------------------------------------------------------------

    private void DrawSidebar(Paper paper, float windowHeight)
    {
        using (paper.Column("se_side").Width(300).Height(UnitValue.Stretch())
            .Padding(8).ColBetween(6).Clip().Enter())
        {
            DrawAssetSettings(paper);
            if (Valid(_selected))
                DrawRectSettings(paper, _settings.Slices[_selected]);
        }
    }

    private void DrawAssetSettings(Paper paper)
    {
        Origami.Header(paper, "se_h_asset", "Asset Settings").Show();

        EditorGUI.Row(paper, "se_ppu", "Pixels Per Unit", () =>
            Origami.NumericField<float>(paper, "se_ppu_v", _settings.PixelsPerUnit,
                v => Mutate("Edit Pixels Per Unit", () => _settings.PixelsPerUnit = MathF.Max(0.0001f, v), true)).Min(0.0001f).Show());

        EditorGUI.Row(paper, "se_tight", "Tight Mesh", () =>
            Origami.Checkbox(paper, "se_tight_v", _settings.GenerateTightMesh,
                v => Mutate("Toggle Tight Mesh", () => _settings.GenerateTightMesh = v)).Show());

        if (_settings.GenerateTightMesh)
        {
            EditorGUI.Row(paper, "se_detail", "Detail", () =>
                Origami.NumericField<float>(paper, "se_detail_v", _settings.TightMeshDetail,
                    v => Mutate("Edit Tight Mesh Detail", () => _settings.TightMeshDetail = MathF.Max(0.1f, v), true)).Min(0.1f).Show());

            EditorGUI.Row(paper, "se_alpha", "Alpha Threshold", () =>
                Origami.NumericField<int>(paper, "se_alpha_v", _settings.TightMeshAlphaThreshold,
                    v => Mutate("Edit Alpha Threshold", () => _settings.TightMeshAlphaThreshold = (byte)Math.Clamp(v, 0, 255), true)).Min(0).Show());
        }
    }

    private static void SetPivotUnit(SpriteSliceData s, PivotUnitMode unit)
    {
        if (unit == s.PivotUnit) return;
        float w = Math.Max(1, s.Rect.Width), h = Math.Max(1, s.Rect.Height);
        s.CustomPivot = unit == PivotUnitMode.Pixels
            ? new Float2(s.CustomPivot.X * w, s.CustomPivot.Y * h)
            : new Float2(s.CustomPivot.X / w, s.CustomPivot.Y / h);
        s.PivotUnit = unit;
    }

    private void DrawRectSettings(Paper paper, SpriteSliceData s)
    {
        paper.Box("se_sp1").Height(6);
        Origami.Header(paper, "se_h_rect", IsSingle ? "Sprite" : "Rect Settings").Line().Show();

        EditorGUI.Row(paper, "se_name", "Name", () =>
            Origami.TextField(paper, "se_name_v", s.Name, v => Mutate("Edit Name", () => s.Name = v, true)).Show());

        if (!IsSingle)
        {
            EditorGUI.Row(paper, "se_pos", "Position", () =>
                Origami.Int2Field(paper, "se_pos_v", new Int2(s.Rect.X, s.Rect.Y), v =>
                    Mutate("Edit Position", () => { SpriteRect r = s.Rect; r.X = v.X; r.Y = v.Y; s.Rect = r; }, true)).Show());

            EditorGUI.Row(paper, "se_size", "Size", () =>
                Origami.Int2Field(paper, "se_size_v", new Int2(s.Rect.Width, s.Rect.Height), v =>
                    Mutate("Edit Size", () => { SpriteRect r = s.Rect; r.Width = Math.Max(1, v.X); r.Height = Math.Max(1, v.Y); s.Rect = r; }, true)).Show());
        }

        EditorGUI.Row(paper, "se_align", "Pivot", () =>
            Origami.EnumDropdown<SpriteAlignment>(paper, "se_align_v", s.Alignment,
                v => Mutate("Edit Pivot", () => s.Alignment = v)).Show());

        if (s.Alignment == SpriteAlignment.Custom)
        {
            EditorGUI.Row(paper, "se_punit", "Pivot Unit", () =>
                Origami.EnumDropdown<PivotUnitMode>(paper, "se_punit_v", s.PivotUnit,
                    v => Mutate("Edit Pivot Unit", () => SetPivotUnit(s, v))).Show());

            EditorGUI.Row(paper, "se_pivot", "Custom Pivot", () =>
                Origami.Float2Field(paper, "se_pivot_v", s.CustomPivot, v => Mutate("Edit Custom Pivot", () => s.CustomPivot = v, true)).Show());
        }

        EditorGUI.Row(paper, "se_border_lr", "Border L / R", () =>
            Origami.Float2Field(paper, "se_border_lr_v", new Float2(s.Border.X, s.Border.Z), v =>
                Mutate("Edit Border", () => { Float4 b = s.Border; b.X = MathF.Max(0, v.X); b.Z = MathF.Max(0, v.Y); s.Border = b; }, true)).Show());

        EditorGUI.Row(paper, "se_border_tb", "Border T / B", () =>
            Origami.Float2Field(paper, "se_border_tb_v", new Float2(s.Border.Y, s.Border.W), v =>
                Mutate("Edit Border", () => { Float4 b = s.Border; b.Y = MathF.Max(0, v.X); b.W = MathF.Max(0, v.Y); s.Border = b; }, true)).Show());
    }

    // --- Pan/zoom view -------------------------------------------------------------

    /// <summary>Pan/zoom state for the texture canvas: converts between content space (texture pixels,
    /// top-left origin) and canvas-local pixels, and applies the matching transform to the Quill canvas.</summary>
    private sealed class Canvas2DView
    {
        public const float MinZoom = 0.05f;
        public const float MaxZoom = 40f;
        public const float ZoomStep = 1.10f;

        private Float2 _pan;
        private float _zoom = 1f;

        public Float2 Pan
        {
            get => _pan;
            set => _pan = value;
        }

        public float Zoom
        {
            get => _zoom;
            set => _zoom = Math.Clamp(value, MinZoom, MaxZoom);
        }

        public Float2 ContentToCanvas(Float2 content) => content * _zoom + _pan;
        public Float2 CanvasToContent(Float2 canvas) => (canvas - _pan) / _zoom;

        public void ApplyTransform(Canvas canvas)
        {
            canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateTranslation(_pan.X, _pan.Y));
            canvas.TransformBy(Prowl.Vector.Spatial.Transform2D.CreateScale(_zoom, _zoom));
        }

        public void PanBy(Float2 deltaCanvasPixels) => _pan += deltaCanvasPixels;

        public void ZoomBy(float factor, Float2 anchorCanvas)
        {
            Float2 contentAnchor = CanvasToContent(anchorCanvas);
            Zoom = _zoom * factor;
            _pan = anchorCanvas - contentAnchor * _zoom;
        }

        public void Reset() { _pan = Float2.Zero; _zoom = 1f; }

        public void Frame(float contentWidth, float contentHeight, Float2 viewportSize, float paddingPixels = 24f)
        {
            if (contentWidth <= 0 || contentHeight <= 0) { Reset(); return; }

            float availW = MathF.Max(1f, viewportSize.X - paddingPixels * 2);
            float availH = MathF.Max(1f, viewportSize.Y - paddingPixels * 2);
            Zoom = MathF.Min(availW / contentWidth, availH / contentHeight);

            Float2 contentCenter = new(contentWidth * 0.5f, contentHeight * 0.5f);
            _pan = viewportSize * 0.5f - contentCenter * _zoom;
        }
    }
}
