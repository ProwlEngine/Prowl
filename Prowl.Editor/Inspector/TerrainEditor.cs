// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

public enum TerrainTab { Height, Paint, Grass, Trees, Settings }
public enum HeightTool { Raise, Lower, Flatten, Smooth }

[CustomComponentEditor(typeof(TerrainComponent))]
public class TerrainEditor : ComponentEditor
{
    // Static brush state — persists across selection changes
    public static TerrainTab ActiveTab = TerrainTab.Height;
    public static HeightTool ActiveHeightTool = HeightTool.Raise;
    public static float BrushSize = 5f;
    public static float BrushStrength = 0.5f;
    public static float BrushFalloff = 0.5f;
    public static float FlattenHeight = 0.5f;
    public static int PaintLayer = 0;
    public static int ActiveGrassType = 0;
    public static int ActiveTreePrototype = 0;
    public static float TreeBrushSize = 10f;
    public static int TreesPerStroke = 3;
    public static bool TreeEraseMode = false;

    // Instance state
    private bool _isDirty;

    public override void OnGUI(Paper paper, string id, MonoBehaviour component)
    {
        ActiveInstance = this;

        var terrain = (TerrainComponent)component;
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
            case TerrainTab.Grass:
                DrawGrassTab(paper, $"{id}_gt", font, terrainData);
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
            DrawTabButton(paper, $"{id}_g", $"{EditorIcons.Seedling}", TerrainTab.Grass, font);
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

    #region Grass Tab

    private void DrawGrassTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        // Grass type selector
        if (data.GrassTypes.Length > 0)
        {
            int gt = Math.Clamp(ActiveGrassType, 0, data.GrassTypes.Length - 1);
            var grassType = data.GrassTypes[gt];

            paper.Box($"{id}_lbl").Height(20)
                .Text($"Grass Type {gt}", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            PropertyGrid.DrawField(paper, $"{id}_tex", "Texture", typeof(AssetRef<Texture2D>), grassType.Texture,
                v => { grassType.Texture = (AssetRef<Texture2D>)v!; _isDirty = true; }, 0);

            EditorGUI.Slider(paper, $"{id}_minw", "Min Width", grassType.MinWidth, 0.1f, 5f)
                .OnValueChanged(v => { grassType.MinWidth = v; _isDirty = true; });
            EditorGUI.Slider(paper, $"{id}_maxw", "Max Width", grassType.MaxWidth, 0.1f, 5f)
                .OnValueChanged(v => { grassType.MaxWidth = v; _isDirty = true; });
            EditorGUI.Slider(paper, $"{id}_minh", "Min Height", grassType.MinHeight, 0.1f, 5f)
                .OnValueChanged(v => { grassType.MinHeight = v; _isDirty = true; });
            EditorGUI.Slider(paper, $"{id}_maxh", "Max Height", grassType.MaxHeight, 0.1f, 5f)
                .OnValueChanged(v => { grassType.MaxHeight = v; _isDirty = true; });
            EditorGUI.Slider(paper, $"{id}_noise", "Noise Spread", grassType.NoiseSpread, 0.01f, 1f)
                .OnValueChanged(v => { grassType.NoiseSpread = v; _isDirty = true; });
            EditorGUI.Slider(paper, $"{id}_bend", "Bend Factor", grassType.BendFactor, 0f, 1f)
                .OnValueChanged(v => { grassType.BendFactor = v; _isDirty = true; });

            PropertyGrid.DrawField(paper, $"{id}_tint", "Healthy Color", typeof(Color), grassType.Tint,
                v => { grassType.Tint = (Color)v!; _isDirty = true; }, 0);
            PropertyGrid.DrawField(paper, $"{id}_dry", "Dry Color", typeof(Color), grassType.DryTint,
                v => { grassType.DryTint = (Color)v!; _isDirty = true; }, 0);
        }

        paper.Box($"{id}_sp").Height(6);
        DrawBrushSettings(paper, $"{id}_brush", font);
    }

    #endregion

    #region Trees Tab

    private void DrawTreesTab(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        // Prototype list
        paper.Box($"{id}_lbl").Height(20)
            .Text("Tree Prototypes", font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        for (int i = 0; i < data.TreePrototypes.Length; i++)
        {
            int idx = i;
            bool selected = ActiveTreePrototype == i;
            var proto = data.TreePrototypes[i];
            string name = proto.Mesh.Res?.Name ?? $"Prototype {i}";

            paper.Box($"{id}_tp{i}")
                .Height(24).Rounded(4)
                .BackgroundColor(selected ? EditorTheme.Purple400 : EditorTheme.Neutral300)
                .Hovered.BackgroundColor(selected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                .Text(name, font)
                .TextColor(selected ? Prowl.Vector.Color.White : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .OnClick(0, (_, _) => ActiveTreePrototype = idx);
        }

        // Add/Remove buttons
        using (paper.Row($"{id}_btns").Height(24).RowBetween(4).Enter())
        {
            EditorGUI.Button(paper, $"{id}_add", $"{EditorIcons.Plus}  Add")
                .OnValueChanged(_ =>
                {
                    var list = new List<TerrainTreePrototype>(data.TreePrototypes);
                    list.Add(new TerrainTreePrototype());
                    data.TreePrototypes = [.. list];
                    ActiveTreePrototype = data.TreePrototypes.Length - 1;
                    _isDirty = true;
                });

            if (data.TreePrototypes.Length > 0)
            {
                EditorGUI.Button(paper, $"{id}_rem", $"{EditorIcons.Trash}  Remove")
                    .OnValueChanged(_ =>
                    {
                        if (ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Length)
                        {
                            var list = new List<TerrainTreePrototype>(data.TreePrototypes);
                            list.RemoveAt(ActiveTreePrototype);
                            data.TreePrototypes = [.. list];
                            ActiveTreePrototype = Math.Clamp(ActiveTreePrototype, 0, Math.Max(0, data.TreePrototypes.Length - 1));
                            _isDirty = true;
                        }
                    });
            }
        }

        paper.Box($"{id}_sp0").Height(6);

        // Selected prototype settings
        if (data.TreePrototypes.Length > 0 && ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Length)
        {
            var proto = data.TreePrototypes[ActiveTreePrototype];

            PropertyGrid.DrawField(paper, $"{id}_mesh", "Mesh", typeof(AssetRef<Mesh>), proto.Mesh,
                v => { proto.Mesh = (AssetRef<Mesh>)v!; _isDirty = true; }, 0);
            PropertyGrid.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Material>), proto.Material,
                v => { proto.Material = (AssetRef<Material>)v!; _isDirty = true; }, 0);
            EditorGUI.Slider(paper, $"{id}_mins", "Min Scale", proto.MinScale, 0.1f, 5f)
                .OnValueChanged(v => { proto.MinScale = v; _isDirty = true; });
            EditorGUI.Slider(paper, $"{id}_maxs", "Max Scale", proto.MaxScale, 0.1f, 5f)
                .OnValueChanged(v => { proto.MaxScale = v; _isDirty = true; });
        }

        paper.Box($"{id}_sp1").Height(6);

        // Tree brush settings
        paper.Box($"{id}_blbl").Height(20)
            .Text("Tree Brush", font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        EditorGUI.Slider(paper, $"{id}_tsz", "Brush Size", TreeBrushSize, 1f, 100f)
            .OnValueChanged(v => TreeBrushSize = v);
        EditorGUI.IntField(paper, $"{id}_tps", TreesPerStroke, "Trees Per Stroke")
            .OnValueChanged(v => TreesPerStroke = Math.Max(1, v));

        paper.Box($"{id}_hint").Height(20)
            .Text("Left-click to place, Shift+click to erase", font)
            .TextColor(EditorTheme.Ink300).FontSize(9f).Alignment(TextAlignment.MiddleLeft);
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
        string[] meshOptions = ["16", "32", "64"];
        int meshCurrent = Array.IndexOf(meshOptions, terrain.MeshResolution.ToString());
        if (meshCurrent < 0) meshCurrent = 0;
        EditorGUI.Dropdown(paper, $"{id}_meshres", "Mesh Resolution", meshCurrent, meshOptions)
            .OnValueChanged(v => { terrain.MeshResolution = int.Parse(meshOptions[v]); });

        EditorGUI.IntField(paper, $"{id}_lod", terrain.MaxLODLevel, "Max LOD Levels")
            .OnValueChanged(v => { terrain.MaxLODLevel = Math.Clamp(v, 1, 8); });
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

    #region Brush Application

    /// <summary>
    /// Apply a brush stroke at the given terrain UV position.
    /// Called from TerrainSceneEditor during mouse drag.
    /// </summary>
    public static void ApplyBrush(TerrainData data, Float2 terrainUV, float deltaTime, out bool heightChanged, out bool splatChanged, out bool grassChanged)
    {
        heightChanged = false;
        splatChanged = false;
        grassChanged = false;

        if (ActiveTab == TerrainTab.Height)
            ApplyHeightBrush(data, terrainUV, deltaTime, ref heightChanged);
        else if (ActiveTab == TerrainTab.Paint)
            ApplySplatBrush(data, terrainUV, deltaTime, ref splatChanged);
        else if (ActiveTab == TerrainTab.Grass)
            ApplyGrassBrush(data, terrainUV, deltaTime, ref grassChanged);
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
            Debug.LogWarning("[TerrainEditor] Cannot save — TerrainData has no asset ID.");
        }
    }

    private static void ApplyGrassBrush(TerrainData data, Float2 uv, float dt, ref bool changed)
    {
        int res = data.GrassmapResolution;
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

                float d = data.GetGrassDensity(x, z);
                d += delta;
                data.SetGrassDensity(x, z, d);
                changed = true;
            }
        }
    }

    /// <summary>Place trees at the given terrain UV within the brush radius.</summary>
    public static void PlaceTrees(TerrainData data, Float2 uv, float terrainSize)
    {
        if (data.TreePrototypes.Length == 0 || ActiveTreePrototype >= data.TreePrototypes.Length)
            return;

        var proto = data.TreePrototypes[ActiveTreePrototype];
        float brushRadiusUV = TreeBrushSize / terrainSize;
        var rng = new Random((int)(uv.X * 10000) ^ (int)(uv.Y * 10000) ^ Environment.TickCount);

        for (int i = 0; i < TreesPerStroke; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float radius = (float)(rng.NextDouble() * brushRadiusUV);
            Float2 pos = uv + new Float2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);

            if (pos.X < 0 || pos.X > 1 || pos.Y < 0 || pos.Y > 1) continue;

            float scale = proto.MinScale + (float)rng.NextDouble() * (proto.MaxScale - proto.MinScale);
            float rotation = (float)(rng.NextDouble() * Math.PI * 2);

            data.Trees.Add(new TreeInstance
            {
                Position = pos,
                PrototypeIndex = ActiveTreePrototype,
                Rotation = rotation,
                Scale = scale,
                Tint = Color.White,
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

    // Static accessor for the scene editor to find the active TerrainEditor instance
    internal static TerrainEditor? ActiveInstance { get; set; }

    #endregion
}
