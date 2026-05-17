// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Scene view editor for terrain provides toolbar with terrain brush tools
/// and handles brush input (raycast, preview, application).
/// </summary>
[SceneViewEditorFor(typeof(TerrainComponent))]
public class TerrainSceneEditor : ISceneViewEditor
{
    private TerrainComponent? _terrain;
    private bool _isPainting;
    private bool _useTransformTool;

    // Temporary full snapshot taken at stroke start used to extract the changed region at stroke end
    private short[]? _preStrokeHeights;
    private float[]? _preStrokeSplats;
    private byte[]? _preStrokeHoles;
    private List<float[]>? _preStrokeDetails;

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

    public bool DrawToolbar(Paper paper, string id, Scribe.FontFile font)
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
            DrawSimpleToolBtn(paper, $"{id}_paint", EditorIcons.Paintbrush, font);
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Holes)
        {
            DrawSimpleToolBtn(paper, $"{id}_holes", EditorIcons.CircleXmark, font);
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Details)
        {
            DrawSimpleToolBtn(paper, $"{id}_grass", EditorIcons.Seedling, font);
        }
        else if (TerrainEditor.ActiveTab == TerrainTab.Trees)
        {
            DrawSimpleToolBtn(paper, $"{id}_tplace", EditorIcons.Leaf, font);
        }

        // Suppress default toolbar we're providing our own
        return true;
    }

    private void DrawSimpleToolBtn(Paper paper, string id, string icon, Scribe.FontFile font)
    {
        bool active = !_useTransformTool;
        paper.Box(id)
            .Width(24).Height(24).Rounded(4)
            .BackgroundColor(active ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .Text(icon, font).TextColor(EditorTheme.Ink500)
            .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => _useTransformTool = false);
    }

    private void DrawHeightToolButtons(Paper paper, string id, Scribe.FontFile font)
    {
        DrawHeightToolBtn(paper, $"{id}_raise", EditorIcons.ArrowUp, HeightTool.Raise, font);
        DrawHeightToolBtn(paper, $"{id}_lower", EditorIcons.ArrowDown, HeightTool.Lower, font);
        DrawHeightToolBtn(paper, $"{id}_flat", EditorIcons.GripLines, HeightTool.Flatten, font);
        DrawHeightToolBtn(paper, $"{id}_smooth", EditorIcons.WaveSquare, HeightTool.Smooth, font);
    }

    private void DrawHeightToolBtn(Paper paper, string id, string icon, HeightTool tool, Scribe.FontFile font)
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

        // Set brush preview based on active tab
        bool isTreeTab = TerrainEditor.ActiveTab == TerrainTab.Trees;
        float brushSize = isTreeTab ? TerrainEditor.TreeBrushSize : TerrainEditor.BrushSize;

        _terrain.BrushPosition = terrainUV;
        _terrain.BrushRadius = brushSize / terrainData.Size;
        _terrain.BrushFalloff = isTreeTab ? 1f : TerrainEditor.BrushFalloff;
        _terrain.BrushVisible = true;

        // Handle input
        bool leftDown = Input.GetMouseButton(0);
        bool leftPressed = Input.GetMouseButtonDown(0);
        bool leftReleased = Input.GetMouseButtonUp(0);
        bool shiftHeld = Input.GetKey(KeyCode.ShiftLeft) || Input.GetKey(KeyCode.ShiftRight);

        // Don't act if right/middle mouse (camera controls)
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            return false;

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
                        var capturedData = terrainData;
                        var pre = preTreeList;
                        var post = postTreeList;
                        Undo.RegisterAction("Remove Trees",
                            () => { capturedData.Trees = new List<TreeInstance>(pre); },
                            () => { capturedData.Trees = new List<TreeInstance>(post); });
                        TerrainEditor.ActiveInstance?.MarkDirty();
                        EditorSceneManager.IsDirty = true;
                    }
                }
                else
                {
                    TerrainEditor.PlaceTrees(terrainData, terrainUV, terrainData.Size);
                    var postTreeList = new List<TreeInstance>(terrainData.Trees);
                    var capturedData = terrainData;
                    var pre = preTreeList;
                    var post = postTreeList;
                    Undo.RegisterAction("Place Trees",
                        () => { capturedData.Trees = new List<TreeInstance>(pre); },
                        () => { capturedData.Trees = new List<TreeInstance>(post); });
                    TerrainEditor.ActiveInstance?.MarkDirty();
                    EditorSceneManager.IsDirty = true;
                }
            }
        }
        else
        {
            // Height/Paint/Details: continuous brush drag
            if (leftPressed && !_isPainting)
            {
                _isPainting = true;
                // Snapshot arrays before stroke begins
                SnapshotPreStroke(terrainData);
            }

            if (_isPainting && leftDown)
            {
                TerrainEditor.ApplyBrush(terrainData, terrainUV, Time.DeltaTime,
                    out bool hChanged, out bool sChanged, out bool gChanged, out bool hoChanged);

                if (hChanged || sChanged || gChanged || hoChanged)
                {
                    if (hChanged || gChanged) _terrain.InvalidateGrassCache();
                    TerrainEditor.ActiveInstance?.MarkDirty();
                    EditorSceneManager.IsDirty = true;
                }
            }

            if (_isPainting && (leftReleased || !leftDown))
            {
                _isPainting = false;
                RegisterStrokeUndo(terrainData);
            }
        }

        // Consume input when over terrain with brush tool (prevents object picking)
        return true;
    }

    private void SnapshotPreStroke(TerrainData data)
    {
        // Lightweight: only snapshot the array that the active tab modifies
        _preStrokeHeights = null;
        _preStrokeSplats = null;
        _preStrokeHoles = null;
        _preStrokeDetails = null;

        if (TerrainEditor.ActiveTab == TerrainTab.Height && data.Heights != null)
            _preStrokeHeights = (short[])data.Heights.Clone();
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
                _preStrokeDetails = [(float[])data.DetailLayers[idx].Clone()];
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
                var preRect = CopyRect(_preStrokeHeights, res, minX, minZ, maxX, maxZ);
                var postRect = CopyRect(data.Heights, res, minX, minZ, maxX, maxZ);
                int cx = minX, cz = minZ, cxe = maxX, cze = maxZ, cres = res;
                var capturedData = data;
                Undo.RegisterAction("Terrain Height",
                    () => { PasteRect(capturedData.Heights!, cres, cx, cz, cxe, cze, preRect); capturedData.SetHeightmapDirty(); },
                    () => { PasteRect(capturedData.Heights!, cres, cx, cz, cxe, cze, postRect); capturedData.SetHeightmapDirty(); });
            }
            _preStrokeHeights = null;
        }
        else if (_preStrokeSplats != null && data.Splats != null)
        {
            int res = data.SplatmapResolution;
            FindChangedRect(_preStrokeSplats, data.Splats, res, res * 4,
                out int minX, out int minZ, out int maxX, out int maxZ);

            if (minX <= maxX)
            {
                int stride = 4;
                var preRect = CopyRectStride(_preStrokeSplats, res, stride, minX, minZ, maxX, maxZ);
                var postRect = CopyRectStride(data.Splats, res, stride, minX, minZ, maxX, maxZ);
                int cx = minX, cz = minZ, cxe = maxX, cze = maxZ, cres = res;
                var capturedData = data;
                Undo.RegisterAction("Terrain Paint",
                    () => { PasteRectStride(capturedData.Splats!, cres, stride, cx, cz, cxe, cze, preRect); capturedData.SetSplatmapDirty(); },
                    () => { PasteRectStride(capturedData.Splats!, cres, stride, cx, cz, cxe, cze, postRect); capturedData.SetSplatmapDirty(); });
            }
            _preStrokeSplats = null;
        }
        else if (_preStrokeHoles != null && data.Holes != null)
        {
            // Simple full-array undo for holes (byte array is small)
            var pre = _preStrokeHoles;
            var post = (byte[])data.Holes.Clone();
            var capturedData = data;
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
                var preArr = _preStrokeDetails[0];
                var postArr = data.DetailLayers[idx];
                int res = data.DetailResolution;
                FindChangedRect(preArr, postArr, res, res,
                    out int minX, out int minZ, out int maxX, out int maxZ);

                if (minX <= maxX)
                {
                    var preRect = CopyRect(preArr, res, minX, minZ, maxX, maxZ);
                    var postRect = CopyRect(postArr, res, minX, minZ, maxX, maxZ);
                    int cx = minX, cz = minZ, cxe = maxX, cze = maxZ, cres = res, cidx = idx;
                    var capturedData = data;
                    Undo.RegisterAction("Terrain Detail",
                        () => { PasteRect(capturedData.DetailLayers[cidx], cres, cx, cz, cxe, cze, preRect); capturedData.SetDetailsDirty(); },
                        () => { PasteRect(capturedData.DetailLayers[cidx], cres, cx, cz, cxe, cze, postRect); capturedData.SetDetailsDirty(); });
                }
            }
            _preStrokeDetails = null;
        }
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
        var rect = new short[w * h];
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
        var rect = new float[w * h];
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
        var rect = new float[w * h * stride];
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

    public void DrawOverlay(Quill.Canvas canvas, Rect viewport)
    {
        // Could draw additional 2D brush info here in the future
    }
}
