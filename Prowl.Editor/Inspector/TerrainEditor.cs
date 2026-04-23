// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

public enum TerrainTab { Height, Paint, Details, Trees, Settings }
public enum HeightTool { Raise, Lower, Flatten, Smooth }

[CustomEditor(typeof(TerrainComponent))]
public class TerrainEditor : CustomEditor
{
    // Static brush state persists across selection changes
    public static TerrainTab ActiveTab = TerrainTab.Height;
    public static HeightTool ActiveHeightTool = HeightTool.Raise;
    public static float BrushSize = 5f;
    public static float BrushStrength = 0.5f;
    public static float BrushFalloff = 0.5f;
    public static float FlattenHeight = 0.5f;
    public static int PaintLayer = 0;
    public static int ActiveDetailIndex = 0;
    public static int ActiveTreePrototype = 0;
    public static float TreeBrushSize = 10f;
    public static int TreesPerStroke = 3;
    public static bool TreeEraseMode = false;

    // Instance state
    private bool _isDirty;
    private TerrainComponent? _terrain;

    public override void OnGUI(Paper paper, string id, object target)
    {
        ActiveInstance = this;

        _terrain = (TerrainComponent)target;
        var terrain = _terrain;
        var terrainData = terrain.Data.Res;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Undo.Snapshot(terrain);

        // Show Data and Material asset refs via default property grid
        PropertyGrid.DrawField(paper, $"{id}_data", "Terrain Data", typeof(AssetRef<TerrainData>), terrain.Data,
            v => terrain.Data = (AssetRef<TerrainData>)v!, 0);
        PropertyGrid.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Material>), terrain.Material,
            v => terrain.Material = (AssetRef<Material>)v!, 0);

        if (terrainData == null)
        {
            paper.Box($"{id}_nodata").Height(24)
                .Text("Assign a TerrainData asset to begin editing.", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        paper.Box($"{id}_sp0").Height(6);

        // Tab bar
        DrawTabBar(paper, $"{id}_tabs", font);

        paper.Box($"{id}_sp1").Height(4);

        // Tab content
        switch (ActiveTab)
        {
            case TerrainTab.Height:
                DrawHeightTab(paper, $"{id}_ht", font, terrainData);
                break;
            case TerrainTab.Paint:
                DrawPaintTab(paper, $"{id}_pt", font, terrainData);
                break;
            case TerrainTab.Details:
                DrawDetailsTab(paper, $"{id}_dt", font, terrainData);
                break;
            case TerrainTab.Trees:
                DrawTreesTab(paper, $"{id}_tt", font, terrainData);
                break;
            case TerrainTab.Settings:
                DrawSettingsTab(paper, $"{id}_st", font, terrain, terrainData);
                break;
        }

        // Save button
        if (_isDirty)
        {
            paper.Box($"{id}_sp_save").Height(8);
            EditorGUI.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save to TerrainData")
                .OnValueChanged(_ => SaveToAsset(terrainData));
        }
    }

    #region Tab Bar

    private static void DrawTabBar(Paper paper, string id, Prowl.Scribe.FontFile font)
    {
        using (paper.Row($"{id}_row").Height(28).RowBetween(2).Enter())
        {
            DrawTabButton(paper, $"{id}_h", $"{EditorIcons.Mountain}", TerrainTab.Height, font);
            DrawTabButton(paper, $"{id}_p", $"{EditorIcons.Paintbrush}", TerrainTab.Paint, font);
            DrawTabButton(paper, $"{id}_g", $"{EditorIcons.Seedling}", TerrainTab.Details, font);
            DrawTabButton(paper, $"{id}_t", $"{EditorIcons.Leaf}", TerrainTab.Trees, font);
            DrawTabButton(paper, $"{id}_s", $"{EditorIcons.Gear}", TerrainTab.Settings, font);
        }
    }

    private static void DrawTabButton(Paper paper, string id, string label, TerrainTab tab, Prowl.Scribe.FontFile font)
    {
        bool active = ActiveTab == tab;
        paper.Box(id)
            .Width(UnitValue.Stretch()).Height(26).Rounded(4)
            .BackgroundColor(active ? EditorTheme.Purple400 : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(active ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
            .Text(label, font)
            .TextColor(active ? Prowl.Vector.Color.White : EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => ActiveTab = tab);
    }

    #endregion

    #region Height Tab

    private void DrawHeightTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        // Tool buttons
        using (paper.Row($"{id}_tools").Height(28).RowBetween(2).Enter())
        {
            DrawToolButton(paper, $"{id}_raise", $"{EditorIcons.ArrowUp}  Raise", HeightTool.Raise, font);
            DrawToolButton(paper, $"{id}_lower", $"{EditorIcons.ArrowDown}  Lower", HeightTool.Lower, font);
            DrawToolButton(paper, $"{id}_flat", $"{EditorIcons.GripLines}  Flatten", HeightTool.Flatten, font);
            DrawToolButton(paper, $"{id}_smooth", $"{EditorIcons.WaveSquare}  Smooth", HeightTool.Smooth, font);
        }

        paper.Box($"{id}_sp").Height(6);

        // Brush settings
        DrawBrushSettings(paper, $"{id}_brush", font);

        // Flatten target height
        if (ActiveHeightTool == HeightTool.Flatten)
        {
            EditorGUI.Slider(paper, $"{id}_flath", "Target Height", FlattenHeight, 0f, 1f)
                .OnValueChanged(v => FlattenHeight = v);
        }
    }

    private static void DrawToolButton(Paper paper, string id, string label, HeightTool tool, Prowl.Scribe.FontFile font)
    {
        bool active = ActiveHeightTool == tool;
        paper.Box(id)
            .Width(UnitValue.Stretch()).Height(26).Rounded(4)
            .BackgroundColor(active ? EditorTheme.Purple400 : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(active ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
            .Text(label, font)
            .TextColor(active ? Prowl.Vector.Color.White : EditorTheme.Ink500)
            .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => ActiveHeightTool = tool);
    }

    #endregion

    #region Paint Tab

    private void DrawPaintTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        // Layer selector (4 slots)
        paper.Box($"{id}_lbl").Height(20)
            .Text("Paint Layer", font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        using (paper.Row($"{id}_layers").Height(32).RowBetween(4).Enter())
        {
            for (int i = 0; i < 4; i++)
            {
                int layer = i;
                bool selected = PaintLayer == i;
                paper.Box($"{id}_l{i}")
                    .Width(UnitValue.Stretch()).Height(28).Rounded(4)
                    .BackgroundColor(selected ? EditorTheme.Purple400 : EditorTheme.Neutral300)
                    .Hovered.BackgroundColor(selected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                    .BorderWidth(selected ? 2 : 0).BorderColor(EditorTheme.Purple400)
                    .Text($"Layer {i}", font)
                    .TextColor(selected ? Prowl.Vector.Color.White : EditorTheme.Ink500)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => PaintLayer = layer);
            }
        }

        paper.Box($"{id}_sp0").Height(4);

        // Selected layer settings
        var sl = data.Layers[PaintLayer];
        paper.Box($"{id}_slbl").Height(20)
            .Text($"Layer {PaintLayer} Settings", font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        PropertyGrid.DrawField(paper, $"{id}_alb", "Albedo", typeof(AssetRef<Texture2D>), sl.Albedo,
            v => { sl.Albedo = (AssetRef<Texture2D>)v!; _isDirty = true; }, 0);
        PropertyGrid.DrawField(paper, $"{id}_nrm", "Normal Map", typeof(AssetRef<Texture2D>), sl.NormalMap,
            v => { sl.NormalMap = (AssetRef<Texture2D>)v!; _isDirty = true; }, 0);
        EditorGUI.FloatField(paper, $"{id}_til", sl.Tiling, "Tiling")
            .OnValueChanged(v => { sl.Tiling = MathF.Max(0.01f, v); _isDirty = true; });
        EditorGUI.Slider(paper, $"{id}_rgh", "Roughness", sl.Roughness, 0f, 1f)
            .OnValueChanged(v => { sl.Roughness = v; _isDirty = true; });
        EditorGUI.Slider(paper, $"{id}_met", "Metallic", sl.Metallic, 0f, 1f)
            .OnValueChanged(v => { sl.Metallic = v; _isDirty = true; });

        paper.Box($"{id}_sp1").Height(6);

        DrawBrushSettings(paper, $"{id}_brush", font);
    }

    #endregion

    #region Details Tab

    private void DrawDetailsTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        // Grid selector for detail prototypes
        DrawPrototypeGrid(paper, $"{id}_grid", font, data.DetailPrototypes,
            ActiveDetailIndex, i => ActiveDetailIndex = i,
            proto => proto.RenderMode == DetailRenderMode.Mesh ? (proto.Mesh.Res?.Name ?? "Empty") : (proto.Texture.Res?.Name ?? "Empty"),
            proto => proto.RenderMode == DetailRenderMode.Mesh ? EditorIcons.Cube : EditorIcons.Seedling);

        // Add/Remove buttons
        using (paper.Row($"{id}_btns").Height(24).RowBetween(4).Enter())
        {
            EditorGUI.Button(paper, $"{id}_add", $"{EditorIcons.Plus}  Add")
                .OnValueChanged(_ => { data.AddDetailPrototype(new DetailPrototype()); MarkDetailsDirty(); });
            if (data.DetailPrototypes.Count > 1)
                EditorGUI.Button(paper, $"{id}_rem", $"{EditorIcons.Trash}  Remove")
                    .OnValueChanged(_ =>
                    {
                        data.RemoveDetailPrototype(ActiveDetailIndex);
                        ActiveDetailIndex = Math.Clamp(ActiveDetailIndex, 0, Math.Max(0, data.DetailPrototypes.Count - 1));
                        MarkDetailsDirty();
                    });
        }

        paper.Box($"{id}_sp0").Height(6);

        // Selected detail settings
        if (ActiveDetailIndex >= 0 && ActiveDetailIndex < data.DetailPrototypes.Count)
        {
            var dp = data.DetailPrototypes[ActiveDetailIndex];

            EditorGUI.EnumDropdown(paper, $"{id}_mode", "Render Mode", dp.RenderMode)
                .OnValueChanged(v => { dp.RenderMode = v; MarkDetailsDirty(); });

            if (dp.RenderMode == DetailRenderMode.Mesh)
            {
                PropertyGrid.DrawField(paper, $"{id}_mesh", "Mesh", typeof(AssetRef<Mesh>), dp.Mesh,
                    v => { dp.Mesh = (AssetRef<Mesh>)v!; MarkDetailsDirty(); }, 0);
                DrawPrototypeMaterials(paper, $"{id}_mats", "Materials", dp.Mesh.Res, dp.Materials);
            }
            else
            {
                PropertyGrid.DrawField(paper, $"{id}_tex", "Texture", typeof(AssetRef<Texture2D>), dp.Texture,
                    v => { dp.Texture = (AssetRef<Texture2D>)v!; MarkDetailsDirty(); }, 0);
            }

            EditorGUI.Slider(paper, $"{id}_minw", "Min Width", dp.MinWidth, 0.1f, 5f)
                .OnValueChanged(v => { dp.MinWidth = v; MarkDetailsDirty(); });
            EditorGUI.Slider(paper, $"{id}_maxw", "Max Width", dp.MaxWidth, 0.1f, 5f)
                .OnValueChanged(v => { dp.MaxWidth = v; MarkDetailsDirty(); });
            EditorGUI.Slider(paper, $"{id}_minh", "Min Height", dp.MinHeight, 0.1f, 5f)
                .OnValueChanged(v => { dp.MinHeight = v; MarkDetailsDirty(); });
            EditorGUI.Slider(paper, $"{id}_maxh", "Max Height", dp.MaxHeight, 0.1f, 5f)
                .OnValueChanged(v => { dp.MaxHeight = v; MarkDetailsDirty(); });
            EditorGUI.Slider(paper, $"{id}_noise", "Noise Spread", dp.NoiseSpread, 0.01f, 1f)
                .OnValueChanged(v => { dp.NoiseSpread = v; MarkDetailsDirty(); });
            EditorGUI.Slider(paper, $"{id}_bend", "Bend Factor", dp.BendFactor, 0f, 1f)
                .OnValueChanged(v => { dp.BendFactor = v; MarkDetailsDirty(); });
            EditorGUI.Toggle(paper, $"{id}_atn", "Align To Normal", dp.AlignToNormal)
                .OnValueChanged(v => { dp.AlignToNormal = v; MarkDetailsDirty(); });

            PropertyGrid.DrawField(paper, $"{id}_hc", "Healthy Color", typeof(Prowl.Vector.Color), dp.HealthyColor,
                v => { dp.HealthyColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
            PropertyGrid.DrawField(paper, $"{id}_dc", "Dry Color", typeof(Prowl.Vector.Color), dp.DryColor,
                v => { dp.DryColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
        }

        paper.Box($"{id}_sp1").Height(6);
        DrawBrushSettings(paper, $"{id}_brush", font);
    }

    #endregion

    #region Trees Tab

    private void DrawTreesTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        // Grid selector for tree prototypes
        DrawPrototypeGrid(paper, $"{id}_grid", font, data.TreePrototypes,
            ActiveTreePrototype, i => ActiveTreePrototype = i,
            proto => proto.Mesh.Res?.Name ?? "Empty",
            _ => EditorIcons.Leaf);

        // Add/Remove buttons
        using (paper.Row($"{id}_btns").Height(24).RowBetween(4).Enter())
        {
            EditorGUI.Button(paper, $"{id}_add", $"{EditorIcons.Plus}  Add")
                .OnValueChanged(_ => { data.TreePrototypes.Add(new TreePrototype()); _isDirty = true; });
            if (data.TreePrototypes.Count > 0)
                EditorGUI.Button(paper, $"{id}_rem", $"{EditorIcons.Trash}  Remove")
                    .OnValueChanged(_ =>
                    {
                        if (ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Count)
                        {
                            data.TreePrototypes.RemoveAt(ActiveTreePrototype);
                            ActiveTreePrototype = Math.Clamp(ActiveTreePrototype, 0, Math.Max(0, data.TreePrototypes.Count - 1));
                            _isDirty = true;
                        }
                    });
        }

        paper.Box($"{id}_sp0").Height(6);

        // Selected prototype settings
        if (data.TreePrototypes.Count > 0 && ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Count)
        {
            var proto = data.TreePrototypes[ActiveTreePrototype];

            PropertyGrid.DrawField(paper, $"{id}_mesh", "Mesh", typeof(AssetRef<Mesh>), proto.Mesh,
                v => { proto.Mesh = (AssetRef<Mesh>)v!; _isDirty = true; }, 0);

            DrawPrototypeMaterials(paper, $"{id}_mats", "Materials", proto.Mesh.Res, proto.Materials);

            EditorGUI.Slider(paper, $"{id}_bend", "Bend Factor", proto.BendFactor, 0f, 2f)
                .OnValueChanged(v => { proto.BendFactor = v; _isDirty = true; });
        }

        paper.Box($"{id}_sp1").Height(6);

        // Tree brush settings
        EditorGUI.Slider(paper, $"{id}_tsz", "Brush Size", TreeBrushSize, 1f, 100f)
            .OnValueChanged(v => TreeBrushSize = v);
        EditorGUI.IntField(paper, $"{id}_tps", TreesPerStroke, "Trees Per Stroke")
            .OnValueChanged(v => TreesPerStroke = Math.Max(1, v));

        paper.Box($"{id}_hint").Height(20)
            .Text("Click to place, Shift+click to erase", font)
            .TextColor(EditorTheme.Ink300).FontSize(9f).Alignment(TextAlignment.MiddleLeft);
    }

    #endregion

    #region Prototype Grid

    /// <summary>Draw a 4-column grid of prototype items with selection.</summary>
    private static void DrawPrototypeGrid<T>(Paper paper, string id, Prowl.Scribe.FontFile font,
        IList<T> items, int selectedIndex, Action<int> onSelect,
        Func<T, string> getName, Func<T, string> getIcon)
    {
        int cols = 4;
        int rows = (items.Count + cols - 1) / cols;

        for (int row = 0; row < rows; row++)
        {
            using (paper.Row($"{id}_r{row}").Height(48).RowBetween(4).Enter())
            {
                for (int col = 0; col < cols; col++)
                {
                    int idx = row * cols + col;
                    if (idx < items.Count)
                    {
                        int capturedIdx = idx;
                        bool selected = selectedIndex == idx;
                        string icon = getIcon(items[idx]);
                        string name = getName(items[idx]);

                        paper.Box($"{id}_i{idx}")
                            .Width(UnitValue.Stretch()).Height(44).Rounded(4)
                            .BackgroundColor(selected ? EditorTheme.Purple400 : EditorTheme.Neutral300)
                            .Hovered.BackgroundColor(selected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                            .BorderWidth(selected ? 2 : 0).BorderColor(EditorTheme.Purple400)
                            .Text($"{icon}\n{name}", font)
                            .TextColor(selected ? Prowl.Vector.Color.White : EditorTheme.Ink500)
                            .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                            .OnClick(0, (_, _) => onSelect(capturedIdx));
                    }
                    else
                    {
                        paper.Box($"{id}_e{row}_{col}").Width(UnitValue.Stretch()).Height(44);
                    }
                }
            }
        }
    }

    #endregion

    #region Settings Tab

    private void DrawSettingsTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainComponent terrain, TerrainData data)
    {
        EditorGUI.FloatField(paper, $"{id}_size", data.Size, "Terrain Size")
            .OnValueChanged(v => { data.Size = MathF.Max(1f, v); _isDirty = true; });
        EditorGUI.FloatField(paper, $"{id}_height", data.Height, "Terrain Height")
            .OnValueChanged(v => { data.Height = MathF.Max(0.1f, v); _isDirty = true; });

        paper.Box($"{id}_sp").Height(6);

        // Heightmap resolution dropdown
        string[] hmOptions = ["33", "65", "129", "257", "513", "1025"];
        int hmCurrent = Array.IndexOf(hmOptions, data.HeightmapResolution.ToString());
        if (hmCurrent < 0) hmCurrent = 4; // default 513
        EditorGUI.Dropdown(paper, $"{id}_hmres", "Heightmap Resolution", hmCurrent, hmOptions)
            .OnValueChanged(v =>
            {
                int newRes = int.Parse(hmOptions[v]);
                if (newRes != data.HeightmapResolution)
                {
                    ModalDialog.Confirm("Reset Heightmap?",
                        $"Changing heightmap resolution to {newRes} will reset all height data.",
                        () => { data.ResizeHeightmap(newRes); _isDirty = true; });
                }
            });

        // Splatmap resolution dropdown
        string[] smOptions = ["32", "64", "128", "256", "512", "1024"];
        int smCurrent = Array.IndexOf(smOptions, data.SplatmapResolution.ToString());
        if (smCurrent < 0) smCurrent = 4; // default 512
        EditorGUI.Dropdown(paper, $"{id}_smres", "Splatmap Resolution", smCurrent, smOptions)
            .OnValueChanged(v =>
            {
                int newRes = int.Parse(smOptions[v]);
                if (newRes != data.SplatmapResolution)
                {
                    ModalDialog.Confirm("Reset Splatmap?",
                        $"Changing splatmap resolution to {newRes} will reset all splat data.",
                        () => { data.ResizeSplatmap(newRes); _isDirty = true; });
                }
            });

        paper.Box($"{id}_sp2").Height(6);

        // Mesh resolution dropdown (on TerrainComponent, not TerrainData)
        string[] meshOptions = ["16", "32", "64", "128"];
        int meshCurrent = Array.IndexOf(meshOptions, terrain.MeshResolution.ToString());
        if (meshCurrent < 0) meshCurrent = 0;
        EditorGUI.Dropdown(paper, $"{id}_meshres", "Mesh Resolution", meshCurrent, meshOptions)
            .OnValueChanged(v => { terrain.MeshResolution = int.Parse(meshOptions[v]); });

        EditorGUI.IntField(paper, $"{id}_lod", terrain.MaxLODLevel, "Max LOD Levels")
            .OnValueChanged(v => { terrain.MaxLODLevel = Math.Clamp(v, 1, 8); });

        paper.Box($"{id}_sp3").Height(10);
        EditorGUI.Label(paper, $"{id}_veg_hdr", "Vegetation");

        EditorGUI.Slider(paper, $"{id}_grassdist", "Grass View Distance", terrain.GrassDistance, 10f, 1000f)
            .OnValueChanged(v => { terrain.GrassDistance = MathF.Max(1f, v); });

        EditorGUI.Slider(paper, $"{id}_grassfade", "Grass Fade Start", terrain.GrassFadeStart, 0f, 0.99f)
            .OnValueChanged(v => { terrain.GrassFadeStart = Math.Clamp(v, 0f, 0.99f); });

        EditorGUI.Slider(paper, $"{id}_grassdensity", "Grass Density", terrain.GrassDensityMultiplier, 0f, 4f)
            .OnValueChanged(v => { terrain.GrassDensityMultiplier = MathF.Max(0f, v); terrain.InvalidateGrassCache(); });

        EditorGUI.Slider(paper, $"{id}_treedist", "Tree View Distance", terrain.TreeDistance, 50f, 2000f)
            .OnValueChanged(v => { terrain.TreeDistance = MathF.Max(1f, v); });
    }

    #endregion

    #region Brush Settings (shared)

    private static void DrawBrushSettings(Paper paper, string id, Prowl.Scribe.FontFile font)
    {
        EditorGUI.Slider(paper, $"{id}_size", "Brush Size", BrushSize, 1f, 500f)
            .OnValueChanged(v => BrushSize = v);
        EditorGUI.Slider(paper, $"{id}_str", "Brush Strength", BrushStrength, 0f, 1f)
            .OnValueChanged(v => BrushStrength = v);
        EditorGUI.Slider(paper, $"{id}_fall", "Brush Falloff", BrushFalloff, 0f, 1f)
            .OnValueChanged(v => BrushFalloff = v);
    }

    #endregion

    #region Material List (shared)

    /// <summary>
    /// Draws a per-submesh Materials list for a terrain prototype. Resizes the <paramref name="materials"/>
    /// list to match the mesh's <see cref="Mesh.SubMeshCount"/> when a mesh is assigned, so users see one
    /// slot per submesh; falls back to a single "Material" field when no mesh is assigned yet.
    /// </summary>
    private void DrawPrototypeMaterials(Paper paper, string id, string label, Mesh? mesh, List<AssetRef<Material>> materials)
    {
        if (mesh == null)
        {
            // No mesh assigned show a single material field (will end up as submesh 0).
            AssetRef<Material> single = materials.Count > 0 ? materials[0] : default;
            PropertyGrid.DrawField(paper, $"{id}_single", label, typeof(AssetRef<Material>), single,
                v =>
                {
                    var val = (AssetRef<Material>)v!;
                    if (materials.Count == 0) materials.Add(val); else materials[0] = val;
                    _isDirty = true; MarkDetailsDirty();
                }, 0);
            return;
        }

        int subCount = mesh.SubMeshCount;
        // Grow the list so the user has one slot per submesh. Shrinking is left alone so user
        // can still see (and clear) extra entries if they swap to a smaller mesh.
        while (materials.Count < subCount) materials.Add(default);

        for (int i = 0; i < subCount; i++)
        {
            int capturedIndex = i;
            string slotLabel = subCount > 1 ? $"{label} [{i}]" : label;
            PropertyGrid.DrawField(paper, $"{id}_{i}", slotLabel, typeof(AssetRef<Material>), materials[i],
                v =>
                {
                    materials[capturedIndex] = (AssetRef<Material>)v!;
                    _isDirty = true; MarkDetailsDirty();
                }, 0);
        }
    }

    #endregion

    #region Brush Application

    /// <summary>
    /// Apply a brush stroke at the given terrain UV position.
    /// Called from TerrainSceneEditor during mouse drag.
    /// </summary>
    public static void ApplyBrush(TerrainData data, Float2 terrainUV, float deltaTime, out bool heightChanged, out bool splatChanged, out bool detailChanged)
    {
        heightChanged = false;
        splatChanged = false;
        detailChanged = false;

        if (ActiveTab == TerrainTab.Height)
            ApplyHeightBrush(data, terrainUV, deltaTime, ref heightChanged);
        else if (ActiveTab == TerrainTab.Paint)
            ApplySplatBrush(data, terrainUV, deltaTime, ref splatChanged);
        else if (ActiveTab == TerrainTab.Details)
            ApplyDetailBrush(data, terrainUV, deltaTime, ref detailChanged);
    }

    private static void ApplyHeightBrush(TerrainData data, Float2 uv, float dt, ref bool changed)
    {
        int res = data.HeightmapResolution;
        float radiusPixels = BrushSize / data.Size * (res - 1);
        int cx = (int)(uv.X * (res - 1));
        int cz = (int)(uv.Y * (res - 1));
        int r = (int)MathF.Ceiling(radiusPixels);

        for (int z = cz - r; z <= cz + r; z++)
        {
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= res || z < 0 || z >= res) continue;

                float dx = x - cx;
                float dz = z - cz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > radiusPixels) continue;

                float t = dist / radiusPixels;
                float falloffStart = 1f - BrushFalloff;
                float falloff = 1f - SmoothStep(falloffStart, 1f, t);

                float h = data.GetHeight(x, z);
                float delta = BrushStrength * falloff * dt * 1.5f;

                switch (ActiveHeightTool)
                {
                    case HeightTool.Raise:
                        h += delta;
                        break;
                    case HeightTool.Lower:
                        h -= delta;
                        break;
                    case HeightTool.Flatten:
                        h += (FlattenHeight - h) * delta;
                        break;
                    case HeightTool.Smooth:
                        float avg = SampleAverage(data, x, z, res);
                        h += (avg - h) * delta;
                        break;
                }

                data.SetHeight(x, z, h);
                changed = true;
            }
        }
    }

    private static float SampleAverage(TerrainData data, int cx, int cz, int res)
    {
        float sum = 0;
        int count = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x >= 0 && x < res && z >= 0 && z < res)
                {
                    sum += data.GetHeight(x, z);
                    count++;
                }
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private static void ApplySplatBrush(TerrainData data, Float2 uv, float dt, ref bool changed)
    {
        int res = data.SplatmapResolution;
        float radiusPixels = BrushSize / data.Size * (res - 1);
        int cx = (int)(uv.X * (res - 1));
        int cz = (int)(uv.Y * (res - 1));
        int r = (int)MathF.Ceiling(radiusPixels);

        for (int z = cz - r; z <= cz + r; z++)
        {
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= res || z < 0 || z >= res) continue;

                float dx = x - cx;
                float dz = z - cz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > radiusPixels) continue;

                float t = dist / radiusPixels;
                float falloffStart = 1f - BrushFalloff;
                float falloff = 1f - SmoothStep(falloffStart, 1f, t);
                float delta = BrushStrength * falloff * dt * 5f; // 5x multiplier for responsive painting

                // Increase target channel, then normalize all 4
                float[] weights = new float[4];
                for (int c = 0; c < 4; c++)
                    weights[c] = data.GetSplat(x, z, c);

                weights[PaintLayer] += delta;

                // Normalize
                float sum = weights[0] + weights[1] + weights[2] + weights[3];
                if (sum > 0)
                    for (int c = 0; c < 4; c++)
                        data.SetSplat(x, z, c, weights[c] / sum);

                changed = true;
            }
        }
    }

    #endregion

    #region Save

    private void SaveToAsset(TerrainData data)
    {
        var db = EditorAssetDatabase.Instance;
        if (db != null && data.AssetID != Guid.Empty)
        {
            db.SaveAsset(data);
            _isDirty = false;
            Debug.Log("[TerrainEditor] Saved terrain data.");
        }
        else
        {
            Debug.LogWarning("[TerrainEditor] Cannot save TerrainData has no asset ID.");
        }
    }

    private static void ApplyDetailBrush(TerrainData data, Float2 uv, float dt, ref bool changed)
    {
        if (ActiveDetailIndex < 0 || ActiveDetailIndex >= data.DetailLayers.Count) return;

        int res = data.DetailResolution;
        float radiusPixels = BrushSize / data.Size * (res - 1);
        int cx = (int)(uv.X * (res - 1));
        int cz = (int)(uv.Y * (res - 1));
        int r = (int)MathF.Ceiling(radiusPixels);

        for (int z = cz - r; z <= cz + r; z++)
        {
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= res || z < 0 || z >= res) continue;

                float dx = x - cx;
                float dz = z - cz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > radiusPixels) continue;

                float t = dist / radiusPixels;
                float falloffStart = 1f - BrushFalloff;
                float falloff = 1f - SmoothStep(falloffStart, 1f, t);
                float delta = BrushStrength * falloff * dt * 3f;

                float d = data.GetDetailDensity(ActiveDetailIndex, x, z);
                d += delta;
                data.SetDetailDensity(ActiveDetailIndex, x, z, d);
                changed = true;
            }
        }
    }

    /// <summary>Place trees at the given terrain UV within the brush radius.</summary>
    public static void PlaceTrees(TerrainData data, Float2 uv, float terrainSize)
    {
        if (data.TreePrototypes.Count == 0 || ActiveTreePrototype >= data.TreePrototypes.Count)
            return;

        var rng = new Random((int)(uv.X * 10000) ^ (int)(uv.Y * 10000) ^ Environment.TickCount);
        float brushRadiusUV = TreeBrushSize / terrainSize;

        for (int i = 0; i < TreesPerStroke; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float radius = (float)(rng.NextDouble() * brushRadiusUV);
            Float2 pos = uv + new Float2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);

            if (pos.X < 0 || pos.X > 1 || pos.Y < 0 || pos.Y > 1) continue;

            // TODO: Add MinScale/MaxScale fields to TreePrototype for per-type control
            float scale = 0.8f + (float)rng.NextDouble() * 0.4f;
            float rotation = (float)(rng.NextDouble() * Math.PI * 2);

            data.Trees.Add(new TreeInstance
            {
                Position = pos,
                PrototypeIndex = ActiveTreePrototype,
                Rotation = rotation,
                WidthScale = scale,
                HeightScale = scale,
                Tint = Prowl.Vector.Color.White,
            });
        }
    }

    /// <summary>Remove trees within radius of the given terrain UV.</summary>
    public static int RemoveTrees(TerrainData data, Float2 uv, float terrainSize)
    {
        float brushRadiusUV = TreeBrushSize / terrainSize;
        float radiusSq = brushRadiusUV * brushRadiusUV;
        int removed = 0;

        for (int i = data.Trees.Count - 1; i >= 0; i--)
        {
            var diff = data.Trees[i].Position - uv;
            if (Float2.Dot(diff, diff) < radiusSq)
            {
                data.Trees.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = MathF.Max(0, MathF.Min(1, (x - edge0) / (edge1 - edge0)));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Mark the terrain data as dirty (called from TerrainSceneEditor after brush strokes).</summary>
    public void MarkDirty() => _isDirty = true;

    private void MarkDetailsDirty()
    {
        _isDirty = true;
        _terrain?.InvalidateGrassCache();
    }

    // Static accessor for the scene editor to find the active TerrainEditor instance
    internal static TerrainEditor? ActiveInstance { get; set; }

    #endregion

    #region Editor Callbacks

    /// <summary>Auto-save all TerrainData assets when the scene is saved.</summary>
    [OnSceneSaved]
    static void OnSceneSaved()
    {
        if (Scene.Current == null) return;
        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        foreach (var go in Scene.Current.ActiveObjects)
        {
            var terrain = go.GetComponent<Runtime.Terrain.TerrainComponent>();
            if (terrain == null) continue;

            var terrainData = terrain.Data.Res;
            if (terrainData == null || terrainData.AssetID == Guid.Empty) continue;

            try
            {
                db.SaveAsset(terrainData);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save TerrainData '{terrainData.Name}': {ex.Message}");
            }
        }

        // Clear dirty flag on active editor
        if (ActiveInstance != null)
            ActiveInstance._isDirty = false;
    }

    /// <summary>Invalidate terrain caches after undo/redo so changes are visible.</summary>
    [OnUndoRedo]
    static void OnUndoRedo()
    {
        if (Scene.Current == null) return;

        foreach (var go in Scene.Current.ActiveObjects)
        {
            var terrain = go.GetComponent<Runtime.Terrain.TerrainComponent>();
            if (terrain == null) continue;

            terrain.InvalidateGrassCache();

            // Re-mark GPU textures as dirty so they regenerate
            var data = terrain.Data.Res;
            if (data != null)
            {
                data.SetHeightmapDirty();
                data.SetSplatmapDirty();
                data.SetDetailsDirty();
            }
        }
    }

    #endregion
}
