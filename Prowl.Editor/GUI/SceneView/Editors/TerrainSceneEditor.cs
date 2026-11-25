// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Core;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Scene view editor for terrain provides toolbar with terrain brush tools
/// and handles brush input (raycast, preview, application).
/// </summary>
[ComponentSceneTool(typeof(TerrainComponent))]
public class TerrainSceneEditor : SceneTool
{
    public override string Name => "Terrain";
    public override string Icon => EditorIcons.Mountain;

    private const string BrushControl = "terrain_brush";

    private TerrainComponent? _terrain;
    private bool _isPainting;
    private TerrainData? _strokeData;
    private bool _useTransformTool;
    private bool _strokeChanged;

    // The package's edits when the stroke began, page references only
    private TerrainSnapshot? _beforeStroke;

    public int Priority => 0;

    public override void OnActivated(SceneToolContext ctx)
    {
        GameObject? target = ctx.ActiveObject;
        if (target.IsNotValid()) return;
        EndStroke();
        _terrain = target.GetComponent<TerrainComponent>();
        _useTransformTool = false;
    }

    public override void OnDeactivated()
    {
        EndStroke();
        if (!ReferenceEquals(_terrain, null))
            _terrain.BrushVisible = false;
        _terrain = null;
    }

    private void EndStroke()
    {
        if (!_isPainting) return;
        _isPainting = false;
        if (_terrain.IsValid() && _strokeData.IsValid() && _beforeStroke != null && _strokeChanged)
            Register(StrokeName(), _terrain, _strokeData, _beforeStroke);
        _strokeData = null;
        _beforeStroke = null;
        _strokeChanged = false;
    }

    private static string StrokeName() => TerrainEditor.ActiveTab switch
    {
        TerrainTab.Paint => "Terrain Paint",
        TerrainTab.Holes => "Terrain Holes",
        TerrainTab.Details => "Terrain Detail",
        _ => "Terrain Height",
    };

    // Records an edit already made, from the capture taken before it to the package as it stands
    private static void Register(string name, TerrainComponent terrain, TerrainData data, TerrainSnapshot before)
    {
        TerrainSnapshot after = data.Capture();
        TerrainDataUndo.Register(name, terrain, data, d => d.Restore(before), d => d.Restore(after));
    }

    public override bool OverridesToolStrip => true;

    public override void OnToolStripGUI(SceneToolContext ctx, Paper paper, string id)
    {
        if (_terrain.IsNotValid()) return;
        using (EditorGUI.ToolStripGroup(paper, $"{id}_xform_grp"))
            EditorGUI.ToolStripButton(paper, $"{id}_xform", EditorIcons.ArrowsUpDownLeftRight_I, "Move", _useTransformTool,
                () => _useTransformTool = true);

        // Terrain-specific tools based on active tab
        using IDisposable tools = EditorGUI.ToolStripGroup(paper, $"{id}_tools_grp");
        if (TerrainEditor.ActiveTab == TerrainTab.Height)
        {
            DrawHeightToolButtons(paper, id);
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Paint)
        {
            DrawSimpleToolBtn(paper, $"{id}_paint", EditorIcons.Paintbrush_I, "Paint");
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Holes)
        {
            DrawSimpleToolBtn(paper, $"{id}_holes", EditorIcons.CircleXmark_I, "Holes");
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Details)
        {
            DrawSimpleToolBtn(paper, $"{id}_grass", EditorIcons.Seedling_I, "Details");
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Trees)
        {
            DrawSimpleToolBtn(paper, $"{id}_tplace", EditorIcons.Leaf_I, "Trees");
        }

    }

    private void DrawSimpleToolBtn(Paper paper, string id, IOrigamiIcon icon, string tooltip)
        => EditorGUI.ToolStripButton(paper, id, icon, tooltip, !_useTransformTool, () => _useTransformTool = false);

    private void DrawHeightToolButtons(Paper paper, string id)
    {
        DrawHeightToolBtn(paper, $"{id}_raise", EditorIcons.ArrowUp_I, "Raise", HeightTool.Raise);
        DrawHeightToolBtn(paper, $"{id}_lower", EditorIcons.ArrowDown_I, "Lower", HeightTool.Lower);
        DrawHeightToolBtn(paper, $"{id}_flat", EditorIcons.GripLines_I, "Flatten", HeightTool.Flatten);
        DrawHeightToolBtn(paper, $"{id}_smooth", EditorIcons.WaveSquare_I, "Smooth", HeightTool.Smooth);
    }

    private void DrawHeightToolBtn(Paper paper, string id, IOrigamiIcon icon, string tooltip, HeightTool tool)
        => EditorGUI.ToolStripButton(paper, id, icon, tooltip, !_useTransformTool && TerrainEditor.ActiveHeightTool == tool, () =>
        {
            _useTransformTool = false;
            TerrainEditor.ActiveHeightTool = tool;
        });

    public override void OnSceneInput(SceneToolContext toolCtx)
    {
        HandleContext ctx = toolCtx.Handles;
        if (_isPainting && (!ctx.PrimaryHeld || _useTransformTool || TerrainEditor.ActiveTab == TerrainTab.Settings))
            EndStroke();

        if (_terrain.IsNotValid() || _useTransformTool)
        {
            if (_terrain.IsValid()) _terrain.BrushVisible = false;
            return;
        }

        // Skip brush input on the Settings tab
        if (TerrainEditor.ActiveTab == TerrainTab.Settings)
        {
            _terrain.BrushVisible = false;
            return;
        }

        // Raycast against terrain
        if (!ctx.ViewportHovered || !_terrain.Raycast(ctx.MouseRay, out Float3 hitPoint, out Float2 terrainUV))
        {
            _terrain.BrushVisible = false;
            return;
        }

        // Update brush preview
        TerrainData? terrainData = _terrain.Data;
        if (terrainData.IsNotValid())
        {
            _terrain.BrushVisible = false;
            return;
        }

        // Set brush preview based on active tab
        bool isTreeTab = TerrainEditor.ActiveTab == TerrainTab.Trees;
        float brushSize = isTreeTab ? TerrainEditor.TreeBrushSize : TerrainEditor.BrushSize;

        _terrain.BrushPosition = terrainUV;
        _terrain.BrushRadius = brushSize / terrainData.Size;
        _terrain.BrushFalloff = isTreeTab ? 0.01f : 0.3f;
        _terrain.BrushVisible = true;

        // The brush covers the whole terrain surface rather than a point, so it registers as a body:
        // far enough to lose to any real handle the cursor is near, near enough to beat object picking.
        ControlID brush = ctx.GetControlID(BrushControl);
        ctx.AddControl(brush, HandleContext.BodyDistance, ctx.DepthOf(hitPoint));
        if (!ctx.IsNearest(brush) && !ctx.IsHot(brush))
            return;

        // Don't act while the camera is being driven
        if (ctx.Blocked)
            return;

        bool leftPressed = ctx.TryBeginDrag(brush);
        bool leftDown = ctx.IsHot(brush) && ctx.PrimaryHeld;
        bool leftReleased = ctx.PrimaryUp;
        bool shiftHeld = ctx.Shift;
        ctx.TryEndDrag(brush);

        if (_isPainting && (isTreeTab || !ReferenceEquals(_strokeData, terrainData)))
            EndStroke();

        if (isTreeTab)
        {
            // Trees: click to place/erase (not continuous drag)
            if (leftPressed)
            {
                TerrainSnapshot before = terrainData.Capture();
                if (shiftHeld)
                {
                    if (TerrainEditor.RemoveTrees(terrainData, terrainUV) > 0)
                    {
                        Register("Remove Trees", _terrain, terrainData, before);
                        TerrainEditor.ActiveInstance?.MarkDirty();
                    }
                }
                else if (TerrainEditor.PlaceTrees(terrainData, terrainUV) > 0)
                {
                    Register("Place Trees", _terrain, terrainData, before);
                    TerrainEditor.ActiveInstance?.MarkDirty();
                }
            }
        }
        else
        {
            // Height/Paint/Details: continuous brush drag
            if (leftPressed && !_isPainting)
            {
                _isPainting = true;
                _strokeData = terrainData;
                _beforeStroke = terrainData.Capture();
            }

            if (_isPainting && leftDown && TerrainEditor.ApplyBrush(terrainData, terrainUV, Time.DeltaTime, shiftHeld))
            {
                _strokeChanged = true;
                TerrainEditor.ActiveInstance?.MarkDirty();
            }

            if (_isPainting && (leftReleased || !leftDown))
                EndStroke();
        }
    }

    public override void OnDrawOverlay(SceneToolContext ctx, Quill.Canvas canvas)
    {
        // Could draw additional 2D brush info here in the future
    }
}

/// <summary>
/// An undo step that changes terrain data. Owned by the TerrainData asset, or by the open scene when the
/// data is embedded. Finds the data again by asset id or by the terrain's identifier.
/// </summary>
internal sealed class TerrainDataUndo : IUndoOperation
{
    private readonly Guid _assetId;
    private readonly Guid _terrainId;
    private readonly Action<TerrainData> _undo;
    private readonly Action<TerrainData> _redo;

    public string Name { get; }
    public IReadOnlyList<UndoOwner> Owners { get; }

    private TerrainDataUndo(string name, TerrainComponent terrain, TerrainData data, Action<TerrainData> undo, Action<TerrainData> redo)
    {
        Name = name;
        _terrainId = terrain.Identifier;
        _assetId = data.IsFromDatabase ? data.AssetID : Guid.Empty;
        Owners = [data.IsFromDatabase ? UndoOwner.Of(data) : UndoOwner.Of(terrain)];
        _undo = undo;
        _redo = redo;
    }

    /// <summary>Records a change already made to <paramref name="data"/>.</summary>
    public static void Register(string name, TerrainComponent terrain, TerrainData data, Action<TerrainData> undo, Action<TerrainData> redo)
        => Core.Undo.RegisterOperation(new TerrainDataUndo(name, terrain, data, undo, redo));

    public void Undo()
    {
        TerrainData? data = Resolve();
        if (data.IsValid()) _undo(data);
    }

    public void Redo()
    {
        TerrainData? data = Resolve();
        if (data.IsValid()) _redo(data);
    }

    private TerrainData? Resolve()
    {
        if (_assetId != Guid.Empty)
            return AssetDatabase.TryGetExisting(_assetId, out Asset asset) && asset is TerrainData { IsLoaded: true } loaded ? loaded : null;

        Scene scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid()) return null;
        TerrainComponent? terrain = scene.FindObject<TerrainComponent>(_terrainId);
        return terrain.IsValid() ? terrain.Data : null;
    }
}
