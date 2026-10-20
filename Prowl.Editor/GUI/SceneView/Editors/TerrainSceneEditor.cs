// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

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
    private bool _useTransformTool;

    // Temporary full snapshot taken at stroke start used to extract the changed region at stroke end
    private short[]? _preStrokeHeights;
    private float[]? _preStrokeSplats;
    private byte[]? _preStrokeHoles;
    private float[]? _preStrokeWater;
    private float[]? _preStrokeWaterLevels;
    private float[]? _preStrokeWaterCarve;
    private List<byte[]>? _preStrokeDetails;

    public int Priority => 0;

    public override void OnActivated(SceneToolContext ctx)
    {
        GameObject? target = ctx.ActiveObject;
        if (target == null) return;
        _terrain = target.GetComponent<TerrainComponent>();
        _useTransformTool = false;
        _isPainting = false;
    }

    public override void OnDeactivated()
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

    public override bool OverridesToolStrip => true;

    public override void OnToolStripGUI(SceneToolContext ctx, Paper paper, string id)
    {
        if (_terrain == null) return;
        // Transform tool button (always available)
        EditorGUI.ToolStripButton(paper, $"{id}_xform", EditorIcons.ArrowsUpDownLeftRight_I, "Move", _useTransformTool,
            () => _useTransformTool = true);

        EditorGUI.Divider(paper, $"{id}_sep", 4f);

        // Terrain-specific tools based on active tab
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
        else if (TerrainEditor.ActiveTab == TerrainTab.Water)
        {
            DrawSimpleToolBtn(paper, $"{id}_water", EditorIcons.Droplet_I, "Water");
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
        if (_terrain == null || _useTransformTool)
        {
            if (_terrain != null) _terrain.BrushVisible = false;
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
        if (terrainData == null)
        {
            _terrain.BrushVisible = false;
            return;
        }

        // Set brush preview based on active tab
        bool isTreeTab = TerrainEditor.ActiveTab == TerrainTab.Trees;
        float brushSize = isTreeTab ? TerrainEditor.TreeBrushSize : TerrainEditor.BrushSize;

        _terrain.BrushPosition = terrainUV;
        _terrain.BrushRadius = brushSize / terrainData.Size;
        _terrain.BrushFalloff = isTreeTab ? 1f : TerrainEditor.BrushFalloff;
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

        if (isTreeTab)
        {
            // Trees: click to place/erase (not continuous drag)
            if (leftPressed)
            {
                var preTreeList = new List<TreeInstance>(terrainData.Trees);

                if (shiftHeld)
                {
                    int removed = TerrainEditor.RemoveTrees(terrainData, terrainUV, terrainData.Size);
                    if (removed > 0)
                    {
                        var postTreeList = new List<TreeInstance>(terrainData.Trees);
                        TerrainData capturedData = terrainData;
                        List<TreeInstance> pre = preTreeList;
                        List<TreeInstance> post = postTreeList;
                        Undo.RegisterAction("Remove Trees",
                            () => { capturedData.Trees = new List<TreeInstance>(pre); },
                            () => { capturedData.Trees = new List<TreeInstance>(post); });
                        TerrainEditor.ActiveInstance?.MarkDirty();
                        EditorSceneManager.MarkDirty();
                    }
                }
                else
                {
                    TerrainEditor.PlaceTrees(terrainData, terrainUV, terrainData.Size);
                    var postTreeList = new List<TreeInstance>(terrainData.Trees);
                    TerrainData capturedData = terrainData;
                    List<TreeInstance> pre = preTreeList;
                    List<TreeInstance> post = postTreeList;
                    Undo.RegisterAction("Place Trees",
                        () => { capturedData.Trees = new List<TreeInstance>(pre); },
                        () => { capturedData.Trees = new List<TreeInstance>(post); });
                    TerrainEditor.ActiveInstance?.MarkDirty();
                    EditorSceneManager.MarkDirty();
                }
            }
        }
        else
        {
            // Height/Paint/Details: continuous brush drag
            if (leftPressed && !_isPainting)
            {
                _isPainting = true;
                TerrainEditor.EndWaterStroke();
                // Snapshot arrays before stroke begins
                SnapshotPreStroke(terrainData);
            }

            if (_isPainting && leftDown)
            {
                TerrainEditor.ApplyBrush(terrainData, terrainUV, Time.DeltaTime,
                    out bool hChanged, out bool sChanged, out bool gChanged, out bool hoChanged,
                    eraseWater: TerrainEditor.ActiveTab == TerrainTab.Water && shiftHeld);

                if (hChanged || sChanged || gChanged || hoChanged)
                {
                    if (hChanged || gChanged) _terrain.InvalidateDetailCache();
                    TerrainEditor.ActiveInstance?.MarkDirty();
                    EditorSceneManager.MarkDirty();
                }
            }

            if (_isPainting && (leftReleased || !leftDown))
            {
                _isPainting = false;
                TerrainEditor.EndWaterStroke();
                RegisterStrokeUndo(terrainData);
            }
        }
    }

    private void SnapshotPreStroke(TerrainData data)
    {
        // Lightweight: only snapshot the array that the active tab modifies
        _preStrokeHeights = null;
        _preStrokeSplats = null;
        _preStrokeHoles = null;
        _preStrokeWater = null;
        _preStrokeWaterLevels = null;
        _preStrokeWaterCarve = null;
        _preStrokeDetails = null;

        if (TerrainEditor.ActiveTab == TerrainTab.Height && data.Heights != null)
            _preStrokeHeights = (short[])data.Heights.Clone();
        else if (TerrainEditor.ActiveTab == TerrainTab.Water)
        {
            _preStrokeWater = (float[])data.Water.Clone();
            _preStrokeWaterLevels = (float[])data.WaterLevels.Clone();
            _preStrokeWaterCarve = (float[])data.WaterCarve.Clone();
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Paint && data.Splats != null)
            _preStrokeSplats = (float[])data.Splats.Clone();
        else if (TerrainEditor.ActiveTab == TerrainTab.Holes && data.Holes != null)
            _preStrokeHoles = (byte[])data.Holes.Clone();
        else if (TerrainEditor.ActiveTab == TerrainTab.Holes)
        {
            // Holes not yet allocated - snapshot an all-solid array so undo restores to no holes
            _preStrokeHoles = new byte[data.SplatmapResolution * data.SplatmapResolution];
            Array.Fill(_preStrokeHoles, (byte)255);
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Details)
        {
            int idx = TerrainEditor.ActiveDetailIndex;
            if (idx >= 0 && idx < data.DetailLayers.Count && data.DetailLayers[idx] != null)
                _preStrokeDetails = [(byte[])data.DetailLayers[idx].Clone()];
        }
    }

    private void RegisterStrokeUndo(TerrainData data)
    {
        if (_preStrokeHeights != null && data.Heights != null)
        {
            // Find changed region
            int res = data.HeightmapResolution;
            FindChangedRect(_preStrokeHeights, data.Heights, res, res,
                out int minX, out int minZ, out int maxX, out int maxZ);

            if (minX <= maxX)
            {
                short[] preRect = CopyRect(_preStrokeHeights, res, minX, minZ, maxX, maxZ);
                short[] postRect = CopyRect(data.Heights, res, minX, minZ, maxX, maxZ);
                int cx = minX, cz = minZ, cxe = maxX, cze = maxZ, cres = res;
                TerrainData capturedData = data;
                Undo.RegisterAction("Terrain Height",
                    () => { PasteRect(capturedData.Heights!, cres, cx, cz, cxe, cze, preRect); capturedData.SetHeightmapDirty(); },
                    () => { PasteRect(capturedData.Heights!, cres, cx, cz, cxe, cze, postRect); capturedData.SetHeightmapDirty(); });
            }
            _preStrokeHeights = null;
        }
        else if (_preStrokeWater != null)
        {
            float[] before = _preStrokeWater;
            float[] after = (float[])data.Water.Clone();
            float[] beforeLevels = _preStrokeWaterLevels!;
            float[] afterLevels = (float[])data.WaterLevels.Clone();
            float[] beforeCarve = _preStrokeWaterCarve!;
            float[] afterCarve = (float[])data.WaterCarve.Clone();
            TerrainData captured = data;
            Undo.RegisterAction("Terrain Water",
                () => { captured.Water = (float[])before.Clone(); captured.WaterLevels = (float[])beforeLevels.Clone(); captured.WaterCarve = (float[])beforeCarve.Clone(); },
                () => { captured.Water = (float[])after.Clone(); captured.WaterLevels = (float[])afterLevels.Clone(); captured.WaterCarve = (float[])afterCarve.Clone(); });
            _preStrokeWater = null;
            _preStrokeWaterLevels = null;
            _preStrokeWaterCarve = null;
        }
        else if (_preStrokeSplats != null && data.Splats != null)
        {
            int res = data.SplatmapResolution;
            int stride = data.LayerCount;
            FindChangedRect(_preStrokeSplats, data.Splats, res, res * stride,
                out int minX, out int minZ, out int maxX, out int maxZ);

            if (minX <= maxX)
            {
                float[] preRect = CopyRectStride(_preStrokeSplats, res, stride, minX, minZ, maxX, maxZ);
                float[] postRect = CopyRectStride(data.Splats, res, stride, minX, minZ, maxX, maxZ);
                int cx = minX, cz = minZ, cxe = maxX, cze = maxZ, cres = res;
                TerrainData capturedData = data;

                // Skipped once a layer or resolution change has altered the pixel layout
                void Paste(float[] rect)
                {
                    if (capturedData.LayerCount != stride || capturedData.SplatmapResolution != cres) return;
                    PasteRectStride(capturedData.Splats, cres, stride, cx, cz, cxe, cze, rect);
                    capturedData.SetSplatmapDirty();
                }

                Undo.RegisterAction("Terrain Paint", () => Paste(preRect), () => Paste(postRect));
            }
            _preStrokeSplats = null;
        }
        else if (_preStrokeHoles != null && data.Holes != null)
        {
            // Simple full-array undo for holes (byte array is small)
            byte[] pre = _preStrokeHoles;
            byte[] post = (byte[])data.Holes.Clone();
            TerrainData capturedData = data;
            Undo.RegisterAction("Terrain Holes",
                () => { capturedData.Holes = (byte[])pre.Clone(); capturedData.SetHolesDirty(); },
                () => { capturedData.Holes = (byte[])post.Clone(); capturedData.SetHolesDirty(); });
            _preStrokeHoles = null;
        }
        else if (_preStrokeDetails != null)
        {
            int idx = TerrainEditor.ActiveDetailIndex;
            if (idx >= 0 && idx < data.DetailLayers.Count && _preStrokeDetails.Count > 0)
            {
                byte[] preArr = _preStrokeDetails[0];
                byte[] postArr = data.DetailLayers[idx];
                int res = data.DetailResolution;
                FindChangedRect(preArr, postArr, res, res,
                    out int minX, out int minZ, out int maxX, out int maxZ);

                if (minX <= maxX)
                {
                    byte[] preRect = CopyRect(preArr, res, minX, minZ, maxX, maxZ);
                    byte[] postRect = CopyRect(postArr, res, minX, minZ, maxX, maxZ);
                    int cx = minX, cz = minZ, cxe = maxX, cze = maxZ, cres = res, cidx = idx;
                    TerrainData capturedData = data;
                    Undo.RegisterAction("Terrain Detail",
                        () => { PasteRect(capturedData.DetailLayers[cidx], cres, cx, cz, cxe, cze, preRect); capturedData.SetDetailsDirty(); },
                        () => { PasteRect(capturedData.DetailLayers[cidx], cres, cx, cz, cxe, cze, postRect); capturedData.SetDetailsDirty(); });
                }
            }
            _preStrokeDetails = null;
        }
    }

    // byte[] overloads for detail-density undo
    private static void FindChangedRect(byte[] a, byte[] b, int res, int rowStride,
        out int minX, out int minZ, out int maxX, out int maxZ)
    {
        minX = int.MaxValue; minZ = int.MaxValue;
        maxX = int.MinValue; maxZ = int.MinValue;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int idx = z * res + x;
                if (idx < a.Length && idx < b.Length && a[idx] != b[idx])
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                }
            }
    }

    private static byte[] CopyRect(byte[] src, int res, int minX, int minZ, int maxX, int maxZ)
    {
        int w = maxX - minX + 1, h = maxZ - minZ + 1;
        byte[] rect = new byte[w * h];
        for (int z = 0; z < h; z++)
            Array.Copy(src, (minZ + z) * res + minX, rect, z * w, w);
        return rect;
    }

    private static void PasteRect(byte[] dst, int res, int minX, int minZ, int maxX, int maxZ, byte[] rect)
    {
        int w = maxX - minX + 1, h = maxZ - minZ + 1;
        for (int z = 0; z < h; z++)
            Array.Copy(rect, z * w, dst, (minZ + z) * res + minX, w);
    }

    // short[] overloads for 16-bit heightmap undo
    private static void FindChangedRect(short[] a, short[] b, int res, int rowStride,
        out int minX, out int minZ, out int maxX, out int maxZ)
    {
        minX = int.MaxValue; minZ = int.MaxValue;
        maxX = int.MinValue; maxZ = int.MinValue;
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int idx = z * res + x;
                if (idx < a.Length && idx < b.Length && a[idx] != b[idx])
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                }
            }
    }

    private static short[] CopyRect(short[] src, int res, int minX, int minZ, int maxX, int maxZ)
    {
        int w = maxX - minX + 1, h = maxZ - minZ + 1;
        short[] rect = new short[w * h];
        for (int z = 0; z < h; z++)
            Array.Copy(src, (minZ + z) * res + minX, rect, z * w, w);
        return rect;
    }

    private static void PasteRect(short[] dst, int res, int minX, int minZ, int maxX, int maxZ, short[] rect)
    {
        int w = maxX - minX + 1, h = maxZ - minZ + 1;
        for (int z = 0; z < h; z++)
            Array.Copy(rect, z * w, dst, (minZ + z) * res + minX, w);
    }

    // Find the bounding rect of changed values between two arrays (single-stride)
    private static void FindChangedRect(float[] a, float[] b, int res, int rowStride,
        out int minX, out int minZ, out int maxX, out int maxZ)
    {
        minX = int.MaxValue; minZ = int.MaxValue;
        maxX = int.MinValue; maxZ = int.MinValue;
        int elementsPerPixel = rowStride / res; // 1 for heightmap, 4 for splatmap

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                int baseIdx = (z * res + x) * elementsPerPixel;
                bool changed = false;
                for (int c = 0; c < elementsPerPixel; c++)
                {
                    int idx = baseIdx + c;
                    if (idx < a.Length && idx < b.Length && a[idx] != b[idx])
                    { changed = true; break; }
                }
                if (changed)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minZ = Math.Min(minZ, z);
                    maxZ = Math.Max(maxZ, z);
                }
            }
        }
    }

    private static float[] CopyRect(float[] src, int res, int minX, int minZ, int maxX, int maxZ)
    {
        int w = maxX - minX + 1;
        int h = maxZ - minZ + 1;
        float[] rect = new float[w * h];
        for (int z = 0; z < h; z++)
            Array.Copy(src, (minZ + z) * res + minX, rect, z * w, w);
        return rect;
    }

    private static void PasteRect(float[] dst, int res, int minX, int minZ, int maxX, int maxZ, float[] rect)
    {
        int w = maxX - minX + 1;
        int h = maxZ - minZ + 1;
        for (int z = 0; z < h; z++)
            Array.Copy(rect, z * w, dst, (minZ + z) * res + minX, w);
    }

    private static float[] CopyRectStride(float[] src, int res, int stride, int minX, int minZ, int maxX, int maxZ)
    {
        int w = maxX - minX + 1;
        int h = maxZ - minZ + 1;
        float[] rect = new float[w * h * stride];
        for (int z = 0; z < h; z++)
            Array.Copy(src, ((minZ + z) * res + minX) * stride, rect, z * w * stride, w * stride);
        return rect;
    }

    private static void PasteRectStride(float[] dst, int res, int stride, int minX, int minZ, int maxX, int maxZ, float[] rect)
    {
        int w = maxX - minX + 1;
        int h = maxZ - minZ + 1;
        for (int z = 0; z < h; z++)
            Array.Copy(rect, z * w * stride, dst, ((minZ + z) * res + minX) * stride, w * stride);
    }

    public override void OnDrawOverlay(SceneToolContext ctx, Quill.Canvas canvas)
    {
        // Could draw additional 2D brush info here in the future
    }
}
