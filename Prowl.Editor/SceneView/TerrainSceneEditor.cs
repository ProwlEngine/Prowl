// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Inspector;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

/// <summary>
/// Scene view editor for terrain — provides toolbar with terrain brush tools
/// and handles brush input (raycast, preview, application).
/// </summary>
[SceneViewEditorFor(typeof(TerrainComponent))]
public class TerrainSceneEditor : ISceneViewEditor
{
    private TerrainComponent? _terrain;
    private bool _isPainting;
    private bool _useTransformTool;

    public int Priority => 0;

    public void OnActivate(GameObject target)
    {
        _terrain = target.GetComponent<TerrainComponent>();
        _useTransformTool = false;
        _isPainting = false;
    }

    public void OnDeactivate()
    {
        if (_terrain != null)
        {
            _terrain.BrushVisible = false;
            if (_isPainting)
                Undo.EndContinuous();
        }
        _terrain = null;
        _isPainting = false;
    }

    public bool DrawToolbar(Paper paper, string id, Prowl.Scribe.FontFile font)
    {
        if (_terrain == null) return false;

        // Transform tool button (always available)
        bool isTransform = _useTransformTool;
        paper.Box($"{id}_xform")
            .Width(24).Height(24).Rounded(4)
            .BackgroundColor(isTransform ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .Text(EditorIcons.ArrowsUpDownLeftRight, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => _useTransformTool = true);

        paper.Box($"{id}_sep").Width(1).Height(18).BackgroundColor(EditorTheme.Ink200);

        // Terrain-specific tools based on active tab
        if (TerrainEditor.ActiveTab == TerrainTab.Height)
        {
            DrawHeightToolButtons(paper, id, font);
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Paint)
        {
            bool isPaintActive = !_useTransformTool;
            paper.Box($"{id}_paint")
                .Width(24).Height(24).Rounded(4)
                .BackgroundColor(isPaintActive ? EditorTheme.Purple400 : Color.Transparent)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.Paintbrush, font).TextColor(EditorTheme.Ink500)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => _useTransformTool = false);
        }

        // Suppress default toolbar — we're providing our own
        return true;
    }

    private void DrawHeightToolButtons(Paper paper, string id, Prowl.Scribe.FontFile font)
    {
        DrawHeightToolBtn(paper, $"{id}_raise", EditorIcons.ArrowUp, HeightTool.Raise, font);
        DrawHeightToolBtn(paper, $"{id}_lower", EditorIcons.ArrowDown, HeightTool.Lower, font);
        DrawHeightToolBtn(paper, $"{id}_flat", EditorIcons.GripLines, HeightTool.Flatten, font);
        DrawHeightToolBtn(paper, $"{id}_smooth", EditorIcons.WaveSquare, HeightTool.Smooth, font);
    }

    private void DrawHeightToolBtn(Paper paper, string id, string icon, HeightTool tool, Prowl.Scribe.FontFile font)
    {
        bool active = !_useTransformTool && TerrainEditor.ActiveHeightTool == tool;
        paper.Box(id)
            .Width(24).Height(24).Rounded(4)
            .BackgroundColor(active ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .Text(icon, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) =>
            {
                _useTransformTool = false;
                TerrainEditor.ActiveHeightTool = tool;
            });
    }

    public bool OnSceneInput(Camera camera, Scene scene, Ray mouseRay, Float2 mousePos, bool viewportHovered)
    {
        if (_terrain == null || _useTransformTool)
        {
            if (_terrain != null) _terrain.BrushVisible = false;
            return false;
        }

        // Only handle brush input on height/paint tabs
        if (TerrainEditor.ActiveTab == TerrainTab.Settings)
        {
            _terrain.BrushVisible = false;
            return false;
        }

        // Raycast against terrain
        if (!viewportHovered || !_terrain.Raycast(mouseRay, out Float3 hitPoint, out Float2 terrainUV))
        {
            _terrain.BrushVisible = false;
            return false;
        }

        // Update brush preview
        var terrainData = _terrain.Data.Res;
        if (terrainData == null)
        {
            _terrain.BrushVisible = false;
            return false;
        }

        _terrain.BrushPosition = terrainUV;
        _terrain.BrushRadius = TerrainEditor.BrushSize / terrainData.Size;
        _terrain.BrushFalloff = TerrainEditor.BrushFalloff;
        _terrain.BrushVisible = true;

        // Handle painting
        bool leftDown = Input.GetMouseButton(0);
        bool leftPressed = Input.GetMouseButtonDown(0);
        bool leftReleased = Input.GetMouseButtonUp(0);

        // Don't paint if right/middle mouse (camera controls)
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            return false;

        if (leftPressed && !_isPainting)
        {
            _isPainting = true;
            Undo.BeginContinuous([_terrain.GameObject], "Terrain Brush");
        }

        if (_isPainting && leftDown)
        {
            TerrainEditor.ApplyBrush(terrainData, terrainUV, Time.DeltaTime, out bool hChanged, out bool sChanged);

            if (hChanged || sChanged)
            {
                TerrainEditor.ActiveInstance?.MarkDirty();
                EditorSceneManager.IsDirty = true;
            }
        }

        if (_isPainting && (leftReleased || !leftDown))
        {
            _isPainting = false;
            Undo.EndContinuous();
        }

        // Consume input when over terrain with brush tool (prevents object picking)
        return true;
    }

    public void DrawOverlay(Prowl.Quill.Canvas canvas, Rect viewport)
    {
        // Could draw additional 2D brush info here in the future
    }
}
