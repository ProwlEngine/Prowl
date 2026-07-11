// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Terrain;
using Prowl.Vector;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using SColor = System.Drawing.Color;
namespace Prowl.Editor.Inspector;

public enum TerrainTab { Height, Paint, Holes, Details, Trees, Settings }
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

        // Terrain Data asset ref (outside tabs - required before any tab content)
        PropertyGridUtils.DrawField(paper, $"{id}_data", "Terrain Data", typeof(AssetRef<TerrainData>), terrain.Data,
            v => terrain.Data = (AssetRef<TerrainData>)v!, 0);

        if (terrainData == null)
        {
            DrawEmptyState(paper, $"{id}_empty", font);
            return;
        }

        // Dirty banner with an inline save-to-asset action.
        if (_isDirty)
            DrawDirtyBanner(paper, $"{id}_dirty", font, terrainData);

        // Main card: a bordered, clipped panel laid out as [ rail | content ].
        using (paper.Row($"{id}_main").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Margin(8, 8, 6, 0).Rounded(10).Clip()
            .BackgroundColor(SColor.FromArgb(36, 0, 0, 0))
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            // Left rail: vertical mode selector on a darker fill + a 1px right divider.
            using (paper.Row($"{id}_railwrap").Width(UnitValue.Auto).Height(UnitValue.StretchOne).Enter())
            {
                using (paper.Column($"{id}_rail").Width(UnitValue.Auto).Height(UnitValue.StretchOne)
                    .Padding(6, 6, 6, 6).BackgroundColor(SColor.FromArgb(51, 0, 0, 0)).Enter())
                {
                    Origami.IconToolbar(paper, $"{id}_modes", (int)ActiveTab, v => ActiveTab = (TerrainTab)v)
                        .Vertical().Container(false).ButtonSize(34)
                        .Item(EditorIcons.Mountain_I, "Sculpt")
                        .Item(EditorIcons.Pencil_I, "Paint")
                        .Item(EditorIcons.Xmark_I, "Holes")
                        .Item(EditorIcons.Seedling_I, "Details")
                        .Item(EditorIcons.Leaf_I, "Trees")
                        .Item(EditorIcons.Gear_I, "Settings")
                        .Show();
                }
                paper.Box($"{id}_raildiv").Width(1).Height(UnitValue.StretchOne)
                    .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            }

            // Content: mode title strip + the active mode's controls.
            using (paper.Column($"{id}_content").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
                .Padding(0, 0, 8, 8).Enter())
            {
                DrawModeTitle(paper, $"{id}_mt", font);

                switch (ActiveTab)
                {
                    case TerrainTab.Height:   DrawSculpt(paper, $"{id}_ht", font, terrainData); break;
                    case TerrainTab.Paint:    DrawPaint(paper, $"{id}_pt", font, terrainData); break;
                    case TerrainTab.Holes:    DrawHoles(paper, $"{id}_ho", font, terrainData); break;
                    case TerrainTab.Details:  DrawDetails(paper, $"{id}_dt", font, terrainData); break;
                    case TerrainTab.Trees:    DrawTrees(paper, $"{id}_tt", font, terrainData); break;
                    case TerrainTab.Settings: DrawSettings(paper, $"{id}_st", font, terrain, terrainData); break;
                }
            }
        }
    }

    #region Overhaul UI (new)

    /// <summary>The rail/title icon + display name for each mode (matches the design's TR_MODES).</summary>
    private static (IOrigamiIcon icon, string name) ModeInfo(TerrainTab tab) => tab switch
    {
        TerrainTab.Height   => (EditorIcons.Mountain_I, "Sculpt"),
        TerrainTab.Paint    => (EditorIcons.Pencil_I, "Paint"),
        TerrainTab.Holes    => (EditorIcons.Xmark_I, "Holes"),
        TerrainTab.Details  => (EditorIcons.Seedling_I, "Details"),
        TerrainTab.Trees    => (EditorIcons.Leaf_I, "Trees"),
        TerrainTab.Settings => (EditorIcons.Gear_I, "Settings"),
        _ => (EditorIcons.Mountain_I, "Sculpt"),
    };

    /// <summary>Empty-state card shown when no TerrainData is assigned (design .tr-empty).</summary>
    private static void DrawEmptyState(Paper paper, string id, Prowl.Scribe.FontFile font)
    {
        using (paper.Column(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Margin(8, 8, 8, 8).Padding(20, 20, 26, 24).Rounded(12)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1).Enter())
        {
            paper.Box($"{id}_ic").Width(58).Height(58).Rounded(14)
                .Margin(UnitValue.Stretch(), UnitValue.Stretch(), 0, 12)
                .BackgroundColor(EditorTheme.Selected).IsNotInteractable()
                .Icon(paper, EditorIcons.Mountain_I, EditorTheme.AccentText, size: 30f);
            paper.Box($"{id}_t").Width(UnitValue.StretchOne).Height(UnitValue.Auto).MinHeight(20).IsNotInteractable()
                .Text("Assign a TerrainData asset to begin editing.", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter);
        }
    }

    /// <summary>Amber "Unsaved edits" banner with an inline save-to-asset button (design .tr-dirty).</summary>
    private void DrawDirtyBanner(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        var m = Origami.Current.Metrics;
        using (paper.Row(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto).MinHeight(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingLarge)
            .Padding(9, 9, 6, 6).Rounded(8).RowBetween(m.SpacingMedium)
            .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Amber400, 26))
            .BorderColor(EditorTheme.WithAlpha(EditorTheme.Amber400, 71)).BorderWidth(1).Enter())
        {
            paper.Box($"{id}_dot").Width(7).Height(7).Rounded(4)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .BackgroundColor(EditorTheme.Amber400)
                .Glow(0, 0, 7, 0, EditorTheme.WithAlpha(EditorTheme.Amber400, 180)).IsNotInteractable();
            paper.Box($"{id}_t").Width(UnitValue.StretchOne).Height(UnitValue.StretchOne).IsNotInteractable()
                .Text("Unsaved edits", font).TextColor(EditorTheme.Amber400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            Origami.Button(paper, $"{id}_save", $"{EditorIcons.CloudArrowUp}  Save to TerrainData",
                () => { SaveToAsset(data); }).Primary().Small().Show();
        }
    }

    /// <summary>Mode-title strip: active mode's icon + name over a bottom divider (design .tr-mode-title).</summary>
    private static void DrawModeTitle(Paper paper, string id, Prowl.Scribe.FontFile font)
    {
        var (icon, name) = ModeInfo(ActiveTab);
        var semi = EditorTheme.FontSemiBold ?? font;
        using (paper.Column(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto).Margin(0, 0, 0, 6).Enter())
        {
            using (paper.Row($"{id}_r").Width(UnitValue.StretchOne).Height(24)
                .Padding(12, 12, 2, 8).RowBetween(7).IsNotInteractable().Enter())
            {
                paper.Box($"{id}_i").Width(14).Height(UnitValue.StretchOne).IsNotInteractable()
                    .Icon(paper, icon, EditorTheme.AccentText, size: 13f);
                paper.Box($"{id}_t").Width(UnitValue.StretchOne).Height(UnitValue.StretchOne).IsNotInteractable()
                    .Text(name, semi).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }
            paper.Box($"{id}_d").Width(UnitValue.StretchOne).Height(1)
                .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
        }
    }

    private void DrawSculpt(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", "Sculpt Tool", first: true);
        EditorGUI.Row(paper, $"{id}_toolrow", "", () =>
            Origami.ButtonGroup(paper, $"{id}_tool", (int)ActiveHeightTool, v => ActiveHeightTool = (HeightTool)v)
                .Segmented().FullWidth()
                .Item("Raise").Item("Lower").Item("Flatten").Item("Smooth")
                .Show());

        if (ActiveHeightTool == HeightTool.Flatten)
            EditorGUI.Row(paper, $"{id}_flath", "Target Height", () =>
                Origami.Slider(paper, $"{id}_flath_v", FlattenHeight, v => FlattenHeight = v, 0f, 1f).Format("F2").Show());

        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private void DrawPaint(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        var m = Origami.Current.Metrics;

        // Clamp the persisted paint layer to the current layer set.
        if (PaintLayer >= data.LayerCount) PaintLayer = data.LayerCount - 1;
        if (PaintLayer < 0) PaintLayer = 0;

        DrawSectionActions(paper, $"{id}_lh", "Splat Layers", () =>
        {
            MiniButton(paper, $"{id}_add", EditorIcons.Plus_I, () =>
            {
                data.AddLayer(new TerrainLayer());
                _isDirty = true;
            }, enabled: data.LayerCount < TerrainData.kMaxLayers);
            MiniButton(paper, $"{id}_rem", EditorIcons.Trash_I, () =>
            {
                data.RemoveLayer(PaintLayer);
                PaintLayer = Math.Clamp(PaintLayer, 0, Math.Max(0, data.LayerCount - 1));
                _isDirty = true;
            }, danger: true, enabled: data.LayerCount > 1);
        });

        // Layer list.
        using (paper.Column($"{id}_layers").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Padding(8, 8, 0, 0).ColBetween(2).Enter())
        {
            for (int i = 0; i < data.LayerCount; i++)
            {
                int idx = i;
                bool selected = PaintLayer == i;
                string lname = data.Layers[i].Albedo.Res?.Name ?? $"Layer {i}";
                using (paper.Row($"{id}_l{i}").Width(UnitValue.StretchOne).Height(28).Rounded(7).Padding(8, 8, 0, 0).RowBetween(8)
                    .BackgroundColor(selected ? EditorTheme.Selected : SColor.Transparent)
                    .Hovered.BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Hover).End()
                    .OnClick(_ => PaintLayer = idx).Enter())
                {
                    if (selected)
                        paper.Box($"{id}_l{i}_bar").Width(2).Height(UnitValue.StretchOne)
                            .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .BackgroundColor(EditorTheme.Accent).IsNotInteractable();
                    var albThumb = EditorAssetDatabase.Instance?.GetThumbnailTexture(data.Layers[i].Albedo.AssetID);
                    var swBox = paper.Box($"{id}_l{i}_sw").Width(16).Height(16).Rounded(4)
                        .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                        .BorderColor(EditorTheme.WithAlpha(SColor.White, 38)).BorderWidth(1).IsNotInteractable();
                    if (albThumb != null)
                        swBox.Clip().OnPostLayout((h, rect) => paper.Draw(ref h, (canvas, rr) =>
                            canvas.DrawImageRounded(albThumb, (float)rr.Min.X, (float)rr.Min.Y,
                                (float)rr.Size.X, (float)rr.Size.Y, 4f)));
                    else
                        swBox.BackgroundColor(SwatchColor(i));
                    paper.Box($"{id}_l{i}_idx").Width(12).Height(UnitValue.StretchOne).IsNotInteractable()
                        .Text(i.ToString(), EditorTheme.FontMono ?? font).TextColor(EditorTheme.InkFaint)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                    paper.Box($"{id}_l{i}_n").Width(UnitValue.StretchOne).Height(UnitValue.StretchOne).IsNotInteractable()
                        .Text(lname, font).TextColor(selected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
                }
            }
        }

        paper.Box($"{id}_cap").Width(UnitValue.StretchOne).Height(16).Margin(0, m.PaddingLarge, 4, 6).IsNotInteractable()
            .Text($"{data.LayerCount} / {TerrainData.kMaxLayers} layers", font)
            .TextColor(EditorTheme.InkDim).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

        // Selected layer settings.
        var sl = data.Layers[PaintLayer];
        EditorGUI.SectionHeader(paper, $"{id}_slh", $"Layer {PaintLayer}");
        PropertyGridUtils.DrawField(paper, $"{id}_alb", "Albedo", typeof(AssetRef<Texture2D>), sl.Albedo,
            v => { sl.Albedo = (AssetRef<Texture2D>)v!; _isDirty = true; }, 0);
        PropertyGridUtils.DrawField(paper, $"{id}_nrm", "Normal Map", typeof(AssetRef<Texture2D>), sl.NormalMap,
            v => { sl.NormalMap = (AssetRef<Texture2D>)v!; _isDirty = true; }, 0);
        EditorGUI.Row(paper, $"{id}_til", "Tiling", () =>
            Origami.NumericField<float>(paper, $"{id}_til_v", sl.Tiling,
                v => { sl.Tiling = MathF.Max(0.01f, v); _isDirty = true; }).Min(0.01f).Show());
        EditorGUI.Row(paper, $"{id}_rgh", "Roughness", () =>
            Origami.Slider(paper, $"{id}_rgh_v", sl.Roughness, v => { sl.Roughness = v; _isDirty = true; }, 0f, 1f).Format("F2").Show());
        EditorGUI.Row(paper, $"{id}_met", "Metallic", () =>
            Origami.Slider(paper, $"{id}_met_v", sl.Metallic, v => { sl.Metallic = v; _isDirty = true; }, 0f, 1f).Format("F2").Show());

        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private void DrawHoles(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        HintPill(paper, $"{id}_hint", font, "Click to paint holes, Shift+click to fill");
        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private void DrawDetails(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        DrawSectionActions(paper, $"{id}_ph", "Detail Prototypes", () =>
        {
            MiniButton(paper, $"{id}_padd", EditorIcons.Plus_I,
                () => { data.AddDetailPrototype(new DetailPrototype()); MarkDetailsDirty(); });
            MiniButton(paper, $"{id}_prem", EditorIcons.Trash_I, () =>
            {
                data.RemoveDetailPrototype(ActiveDetailIndex);
                ActiveDetailIndex = Math.Clamp(ActiveDetailIndex, 0, Math.Max(0, data.DetailPrototypes.Count - 1));
                MarkDetailsDirty();
            }, danger: true, enabled: data.DetailPrototypes.Count > 1);
        });

        DrawProtoGrid(paper, $"{id}_grid", font, data.DetailPrototypes.Count, ActiveDetailIndex,
            i => ActiveDetailIndex = i,
            i =>
            {
                var p = data.DetailPrototypes[i];
                return p.RenderMode == DetailRenderMode.Mesh ? (p.Mesh.Res?.Name ?? "Empty") : (p.Texture.Res?.Name ?? "Empty");
            },
            i => data.DetailPrototypes[i].RenderMode == DetailRenderMode.Mesh ? EditorIcons.Cube_I : EditorIcons.Seedling_I,
            () => { data.AddDetailPrototype(new DetailPrototype()); MarkDetailsDirty(); },
            i =>
            {
                var p = data.DetailPrototypes[i];
                return p.RenderMode == DetailRenderMode.Mesh ? p.Mesh.AssetID : p.Texture.AssetID;
            });

        if (ActiveDetailIndex >= 0 && ActiveDetailIndex < data.DetailPrototypes.Count)
        {
            var dp = data.DetailPrototypes[ActiveDetailIndex];
            EditorGUI.SectionHeader(paper, $"{id}_sh", "Detail Settings");

            EditorGUI.Row(paper, $"{id}_mode", "Render Mode", () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", dp.RenderMode, v => { dp.RenderMode = v; MarkDetailsDirty(); }).Show());

            if (dp.RenderMode == DetailRenderMode.Mesh)
            {
                PropertyGridUtils.DrawField(paper, $"{id}_mesh", "Mesh", typeof(AssetRef<Mesh>), dp.Mesh,
                    v => { dp.Mesh = (AssetRef<Mesh>)v!; MarkDetailsDirty(); }, 0);
                DrawPrototypeMaterials(paper, $"{id}_mats", "Materials", dp.Mesh.Res, dp.Materials);
            }
            else
            {
                PropertyGridUtils.DrawField(paper, $"{id}_tex", "Texture", typeof(AssetRef<Texture2D>), dp.Texture,
                    v => { dp.Texture = (AssetRef<Texture2D>)v!; MarkDetailsDirty(); }, 0);
                PropertyGridUtils.DrawField(paper, $"{id}_gmat", "Grass Material", typeof(AssetRef<Material>), dp.GrassMaterial,
                    v => { dp.GrassMaterial = (AssetRef<Material>)v!; MarkDetailsDirty(); }, 0);
            }

            EditorGUI.Row(paper, $"{id}_minw", "Min Width", () =>
                Origami.Slider(paper, $"{id}_minw_v", dp.MinWidth, v => { dp.MinWidth = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_maxw", "Max Width", () =>
                Origami.Slider(paper, $"{id}_maxw_v", dp.MaxWidth, v => { dp.MaxWidth = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_minh", "Min Height", () =>
                Origami.Slider(paper, $"{id}_minh_v", dp.MinHeight, v => { dp.MinHeight = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_maxh", "Max Height", () =>
                Origami.Slider(paper, $"{id}_maxh_v", dp.MaxHeight, v => { dp.MaxHeight = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_noise", "Noise Spread", () =>
                Origami.Slider(paper, $"{id}_noise_v", dp.NoiseSpread, v => { dp.NoiseSpread = v; MarkDetailsDirty(); }, 0.01f, 1f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_bend", "Bend Factor", () =>
                Origami.Slider(paper, $"{id}_bend_v", dp.BendFactor, v => { dp.BendFactor = v; MarkDetailsDirty(); }, 0f, 1f).Format("F2").Show());
            EditorGUI.SettingsToggle(paper, $"{id}_atn", "Align To Normal", dp.AlignToNormal,
                v => { dp.AlignToNormal = v; MarkDetailsDirty(); }, separator: false);
            PropertyGridUtils.DrawField(paper, $"{id}_hc", "Healthy Color", typeof(Prowl.Vector.Color), dp.HealthyColor,
                v => { dp.HealthyColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
            PropertyGridUtils.DrawField(paper, $"{id}_dc", "Dry Color", typeof(Prowl.Vector.Color), dp.DryColor,
                v => { dp.DryColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
        }

        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private void DrawTrees(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        DrawSectionActions(paper, $"{id}_ph", "Tree Prototypes", () =>
        {
            MiniButton(paper, $"{id}_padd", EditorIcons.Plus_I,
                () => { data.TreePrototypes.Add(new TreePrototype()); _isDirty = true; });
            MiniButton(paper, $"{id}_prem", EditorIcons.Trash_I, () =>
            {
                if (ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Count)
                {
                    data.TreePrototypes.RemoveAt(ActiveTreePrototype);
                    ActiveTreePrototype = Math.Clamp(ActiveTreePrototype, 0, Math.Max(0, data.TreePrototypes.Count - 1));
                    _isDirty = true;
                }
            }, danger: true, enabled: data.TreePrototypes.Count > 0);
        });

        DrawProtoGrid(paper, $"{id}_grid", font, data.TreePrototypes.Count, ActiveTreePrototype,
            i => ActiveTreePrototype = i,
            i => data.TreePrototypes[i].Mesh.Res?.Name ?? "Empty",
            _ => EditorIcons.Leaf_I,
            () => { data.TreePrototypes.Add(new TreePrototype()); _isDirty = true; },
            i => data.TreePrototypes[i].Mesh.AssetID);

        if (data.TreePrototypes.Count > 0 && ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Count)
        {
            var proto = data.TreePrototypes[ActiveTreePrototype];
            EditorGUI.SectionHeader(paper, $"{id}_pth", "Prototype");
            PropertyGridUtils.DrawField(paper, $"{id}_mesh", "Mesh", typeof(AssetRef<Mesh>), proto.Mesh,
                v => { proto.Mesh = (AssetRef<Mesh>)v!; _isDirty = true; }, 0);
            DrawPrototypeMaterials(paper, $"{id}_mats", "Materials", proto.Mesh.Res, proto.Materials);
            EditorGUI.Row(paper, $"{id}_bend", "Bend Factor", () =>
                Origami.Slider(paper, $"{id}_bend_v", proto.BendFactor, v => { proto.BendFactor = v; _isDirty = true; }, 0f, 2f).Format("F2").Show());
        }

        EditorGUI.SectionHeader(paper, $"{id}_pbh", "Placement Brush");
        EditorGUI.Row(paper, $"{id}_tsz", "Brush Size", () =>
            Origami.Slider(paper, $"{id}_tsz_v", TreeBrushSize, v => TreeBrushSize = v, 1f, 100f).Format("F0").Show());
        EditorGUI.Row(paper, $"{id}_tps", "Trees Per Stroke", () =>
            Origami.NumericField<int>(paper, $"{id}_tps_v", TreesPerStroke, v => TreesPerStroke = Math.Max(1, v)).Min(1).Show());

        HintPill(paper, $"{id}_hint", font, "Click to place, Shift+click to erase");
    }

    private void DrawSettings(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainComponent terrain, TerrainData data)
    {
        EditorGUI.SectionHeader(paper, $"{id}_mh", "Materials", first: true);
        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Material>), terrain.Material,
            v => terrain.Material = (AssetRef<Material>)v!, 0);
        PropertyGridUtils.DrawField(paper, $"{id}_grassmat", "Grass Material", typeof(AssetRef<Material>), terrain.GrassMaterial,
            v => { terrain.GrassMaterial = (AssetRef<Material>)v!; terrain.InvalidateGrassCache(); }, 0);

        EditorGUI.SectionHeader(paper, $"{id}_dh", "Dimensions");
        EditorGUI.Row(paper, $"{id}_size", "Terrain Size", () =>
            Origami.NumericField<float>(paper, $"{id}_size_v", data.Size, v => { data.Size = MathF.Max(1f, v); _isDirty = true; }).Min(1f).Show());
        EditorGUI.Row(paper, $"{id}_height", "Terrain Height", () =>
            Origami.NumericField<float>(paper, $"{id}_height_v", data.Height, v => { data.Height = MathF.Max(0.1f, v); _isDirty = true; }).Min(0.1f).Show());
        EditorGUI.Row(paper, $"{id}_interp", "Interpolation", () =>
            Origami.EnumDropdown(paper, $"{id}_interp_v", data.Interpolation,
                v => { data.Interpolation = v; _isDirty = true; terrain.InvalidateGrassCache(); }).Show());

        EditorGUI.SectionHeader(paper, $"{id}_rh", "Resolutions");
        string[] hmOptions = ["33", "65", "129", "257", "513", "1025", "2049", "4097"];
        int hmCurrent = Array.IndexOf(hmOptions, data.HeightmapResolution.ToString());
        if (hmCurrent < 0) hmCurrent = 4; // default 513
        EditorGUI.Row(paper, $"{id}_hmres", "Heightmap", () =>
            Origami.Dropdown(paper, $"{id}_hmres_v", hmCurrent, v =>
            {
                int newRes = int.Parse(hmOptions[v]);
                if (newRes != data.HeightmapResolution)
                    Origami.Confirm("Reset Heightmap?",
                        $"Changing heightmap resolution to {newRes} will reset all height data.",
                        () => { data.ResizeHeightmap(newRes); _isDirty = true; });
            }, hmOptions).Show());

        string[] smOptions = ["32", "64", "128", "256", "512", "1024"];
        int smCurrent = Array.IndexOf(smOptions, data.SplatmapResolution.ToString());
        if (smCurrent < 0) smCurrent = 4; // default 512
        EditorGUI.Row(paper, $"{id}_smres", "Splatmap", () =>
            Origami.Dropdown(paper, $"{id}_smres_v", smCurrent, v =>
            {
                int newRes = int.Parse(smOptions[v]);
                if (newRes != data.SplatmapResolution)
                    Origami.Confirm("Reset Splatmap?",
                        $"Changing splatmap resolution to {newRes} will reset all splat data.",
                        () => { data.ResizeSplatmap(newRes); _isDirty = true; });
            }, smOptions).Show());

        string[] meshOptions = ["16", "32", "64", "128"];
        int meshCurrent = Array.IndexOf(meshOptions, terrain.MeshResolution.ToString());
        if (meshCurrent < 0) meshCurrent = 0;
        EditorGUI.Row(paper, $"{id}_meshres", "Mesh", () =>
            Origami.Dropdown(paper, $"{id}_meshres_v", meshCurrent,
                v => { terrain.MeshResolution = int.Parse(meshOptions[v]); }, meshOptions).Show());

        EditorGUI.SectionHeader(paper, $"{id}_lh", "Level of Detail");
        EditorGUI.Row(paper, $"{id}_lod", "Max LOD Levels", () =>
            Origami.NumericField<int>(paper, $"{id}_lod_v", terrain.MaxLODLevel,
                v => { terrain.MaxLODLevel = Math.Clamp(v, 1, 8); }).Min(1).Max(8).Show());
        EditorGUI.Row(paper, $"{id}_lodq", "LOD Quality", () =>
            Origami.Slider(paper, $"{id}_lodq_v", terrain.LODQuality,
                v => { terrain.LODQuality = MathF.Max(0.1f, v); }, 0.1f, 5f).Format("F1").Show());

        EditorGUI.SectionHeader(paper, $"{id}_vh", "Vegetation");
        EditorGUI.Row(paper, $"{id}_grassdist", "Grass View Distance", () =>
            Origami.Slider(paper, $"{id}_grassdist_v", terrain.GrassDistance,
                v => { terrain.GrassDistance = MathF.Max(1f, v); }, 10f, 1000f).Format("F0").Show());
        EditorGUI.Row(paper, $"{id}_grassfade", "Grass Fade Start", () =>
            Origami.Slider(paper, $"{id}_grassfade_v", terrain.GrassFadeStart,
                v => { terrain.GrassFadeStart = Math.Clamp(v, 0f, 0.99f); }, 0f, 0.99f).Format("F2").Show());
        EditorGUI.Row(paper, $"{id}_grassdensity", "Grass Density", () =>
            Origami.Slider(paper, $"{id}_grassdensity_v", terrain.GrassDensityMultiplier,
                v => { terrain.GrassDensityMultiplier = MathF.Max(0f, v); terrain.InvalidateGrassCache(); }, 0f, 4f).Format("F2").Show());
        EditorGUI.Row(paper, $"{id}_treedist", "Tree View Distance", () =>
            Origami.Slider(paper, $"{id}_treedist_v", terrain.TreeDistance,
                v => { terrain.TreeDistance = MathF.Max(1f, v); }, 50f, 2000f).Format("F0").Show());
    }

    /// <summary>Size / Strength / Falloff sliders (design .tr-brushrow).</summary>
    private static void DrawBrushBlock(Paper paper, string id, Prowl.Scribe.FontFile font, string title = "Brush")
    {
        var m = Origami.Current.Metrics;
        EditorGUI.SectionHeader(paper, $"{id}_h", title);
        using (paper.Column($"{id}_col").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
        {
            EditorGUI.Row(paper, $"{id}_size", "Size", () =>
                Origami.Slider(paper, $"{id}_size_v", BrushSize, v => BrushSize = v, 1f, 500f).Format("F0").Show());
            EditorGUI.Row(paper, $"{id}_str", "Strength", () =>
                Origami.Slider(paper, $"{id}_str_v", BrushStrength, v => BrushStrength = v, 0f, 1f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_fall", "Falloff", () =>
                Origami.Slider(paper, $"{id}_fall_v", BrushFalloff, v => BrushFalloff = v, 0f, 1f).Format("F2").Show());
        }
    }

    /// <summary>Uppercase accent section header with right-aligned action buttons (design .tr-sec-h + .tr-sec-actions).</summary>
    private static void DrawSectionActions(Paper paper, string id, string text, Action drawActions)
    {
        var m = Origami.Current.Metrics;
        var semi = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        using (paper.Row(id).Width(UnitValue.StretchOne).Height(22)
            .Margin(m.PaddingLarge, m.PaddingLarge, 14, 4).RowBetween(4).Enter())
        {
            if (semi != null)
                paper.Box($"{id}_t").Width(UnitValue.StretchOne).Height(UnitValue.StretchOne).IsNotInteractable()
                    .Text(text.ToUpperInvariant(), semi).TextColor(EditorTheme.AccentText)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            drawActions();
        }
    }

    /// <summary>Small 20x20 glass icon button (design .tr-mini).</summary>
    private static void MiniButton(Paper paper, string id, IOrigamiIcon icon, Action onClick, bool danger = false, bool enabled = true)
    {
        var b = paper.Box(id).Width(20).Height(20).Rounded(5)
            .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1);
        if (enabled)
        {
            b.Hovered.BackgroundColor(danger ? EditorTheme.WithAlpha(EditorTheme.Red400, 41) : EditorTheme.Hover).End();
            b.OnClick(_ => onClick());
        }
        b.Icon(paper, icon, enabled ? EditorTheme.Ink400 : EditorTheme.InkFaint, size: 12f);
    }

    /// <summary>Blue info pill (design .tr-hint).</summary>
    private static void HintPill(Paper paper, string id, Prowl.Scribe.FontFile font, string text)
    {
        var m = Origami.Current.Metrics;
        using (paper.Row(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto).MinHeight(32)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.Spacing)
            .Padding(10, 10, 8, 8).Rounded(8).RowBetween(7)
            .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Blue400, 20))
            .BorderColor(EditorTheme.WithAlpha(EditorTheme.Blue400, 51)).BorderWidth(1).Enter())
        {
            paper.Box($"{id}_i").Width(14).Height(UnitValue.StretchOne).IsNotInteractable()
                .Icon(paper, EditorIcons.CircleInfo_I, EditorTheme.Blue400, size: 12f);
            paper.Box($"{id}_t").Width(UnitValue.StretchOne).Height(UnitValue.Auto).MinHeight(16).IsNotInteractable()
                .Text(text, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
    }

    /// <summary>Auto-fill thumbnail grid of prototypes with a trailing "+" add tile (design .tr-protogrid).
    /// Draws the asset's real thumbnail when available, falling back to the vector icon.</summary>
    private void DrawProtoGrid(Paper paper, string id, Prowl.Scribe.FontFile font, int count,
        int selected, Action<int> onSelect, Func<int, string> getName, Func<int, IOrigamiIcon> getIcon,
        Action onAdd, Func<int, Guid> getThumbGuid)
    {
        var m = Origami.Current.Metrics;
        const int cols = 4;
        int total = count + 1; // include the add tile
        int rows = (total + cols - 1) / cols;
        for (int r = 0; r < rows; r++)
        {
            using (paper.Row($"{id}_r{r}").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
                .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).RowBetween(6).Enter())
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx < count)
                    {
                        int capture = idx;
                        bool sel = selected == idx;
                        var tint = SwatchColor(idx);
                        using (paper.Column($"{id}_t{idx}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                            .Padding(3, 3, 6, 6).ColBetween(5).Rounded(9)
                            .BackgroundColor(sel ? EditorTheme.Selected : SColor.Transparent)
                            .Hovered.BackgroundColor(sel ? EditorTheme.Selected : EditorTheme.Hover).End()
                            .BorderColor(sel ? EditorTheme.WithAlpha(EditorTheme.Accent, 102) : SColor.Transparent).BorderWidth(1)
                            .OnClick(_ => onSelect(capture)).Enter())
                        {
                            var thumb = EditorAssetDatabase.Instance?.GetThumbnailTexture(getThumbGuid(idx));
                            var thumbBox = paper.Box($"{id}_t{idx}_th").Width(46).Height(46).Rounded(9)
                                .Margin(UnitValue.Stretch(), UnitValue.Stretch(), 0, 0)
                                .BackgroundColor(EditorTheme.WithAlpha(tint, 34))
                                .BorderColor(EditorTheme.WithAlpha(tint, 85)).BorderWidth(1)
                                .IsNotInteractable();
                            if (thumb != null)
                                thumbBox.Clip().OnPostLayout((h, rect) => paper.Draw(ref h, (canvas, rr) =>
                                    canvas.DrawImageRounded(thumb, (float)rr.Min.X, (float)rr.Min.Y,
                                        (float)rr.Size.X, (float)rr.Size.Y, 9f)));
                            else
                                thumbBox.Icon(paper, getIcon(idx), tint, size: 20f);
                            paper.Box($"{id}_t{idx}_n").Width(UnitValue.StretchOne).Height(12).IsNotInteractable()
                                .Text(getName(idx), font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter).TextTruncate();
                        }
                    }
                    else if (idx == count)
                    {
                        paper.Box($"{id}_add").Width(UnitValue.Stretch()).Height(70).Rounded(9)
                            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
                            .Hovered.BackgroundColor(EditorTheme.Selected).End()
                            .OnClick(_ => onAdd())
                            .Icon(paper, EditorIcons.Plus_I, EditorTheme.InkDim, size: 18f);
                    }
                    else
                    {
                        paper.Box($"{id}_e{r}_{c}").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
                    }
                }
            }
        }
    }

    /// <summary>Decorative per-index swatch colour (design hsl(i*60+260, 55%, 55%)).</summary>
    private static SColor SwatchColor(int i) => Hsl(i * 60f + 260f, 0.55f, 0.55f);

    private static SColor Hsl(float h, float s, float l)
    {
        h = ((h % 360f) + 360f) % 360f;
        float cc = (1f - MathF.Abs(2f * l - 1f)) * s;
        float x = cc * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float mm = l - cc / 2f;
        float r, g, b;
        if (h < 60f)       { r = cc; g = x;  b = 0f; }
        else if (h < 120f) { r = x;  g = cc; b = 0f; }
        else if (h < 180f) { r = 0f; g = cc; b = x;  }
        else if (h < 240f) { r = 0f; g = x;  b = cc; }
        else if (h < 300f) { r = x;  g = 0f; b = cc; }
        else               { r = cc; g = 0f; b = x;  }
        return SColor.FromArgb(255,
            Math.Clamp((int)((r + mm) * 255f), 0, 255),
            Math.Clamp((int)((g + mm) * 255f), 0, 255),
            Math.Clamp((int)((b + mm) * 255f), 0, 255));
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
        var m = Origami.Current.Metrics;
        // Left accent strip (design .tr-matlist) wrapping the per-submesh fields.
        using (paper.Row($"{id}_wrap").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Margin(16, m.PaddingLarge, 0, m.SpacingLarge).Enter())
        {
            paper.Box($"{id}_strip").Width(2).Height(UnitValue.StretchOne)
                .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            using (paper.Column($"{id}_col").Width(UnitValue.StretchOne).Height(UnitValue.Auto).Enter())
            {
                if (mesh == null)
                {
                    // No mesh assigned show a single material field (will end up as submesh 0).
                    AssetRef<Material> single = materials.Count > 0 ? materials[0] : default;
                    PropertyGridUtils.DrawField(paper, $"{id}_single", label, typeof(AssetRef<Material>), single,
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
                    PropertyGridUtils.DrawField(paper, $"{id}_{i}", slotLabel, typeof(AssetRef<Material>), materials[i],
                        v =>
                        {
                            materials[capturedIndex] = (AssetRef<Material>)v!;
                            _isDirty = true; MarkDetailsDirty();
                        }, 0);
                }
            }
        }
    }

    #endregion

    #region Brush Application

    /// <summary>
    /// Apply a brush stroke at the given terrain UV position.
    /// Called from TerrainSceneEditor during mouse drag.
    /// </summary>
    public static void ApplyBrush(TerrainData data, Float2 terrainUV, float deltaTime,
        out bool heightChanged, out bool splatChanged, out bool detailChanged, out bool holesChanged)
    {
        heightChanged = false;
        splatChanged = false;
        detailChanged = false;
        holesChanged = false;

        if (ActiveTab == TerrainTab.Height)
            ApplyHeightBrush(data, terrainUV, deltaTime, ref heightChanged);
        else if (ActiveTab == TerrainTab.Paint)
            ApplySplatBrush(data, terrainUV, deltaTime, ref splatChanged);
        else if (ActiveTab == TerrainTab.Holes)
            ApplyHolesBrush(data, terrainUV, ref holesChanged);
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

                // Increase target channel, then normalize all layers
                int lc = data.LayerCount;
                float[] weights = new float[lc];
                for (int c = 0; c < lc; c++)
                    weights[c] = data.GetSplat(x, z, c);

                weights[PaintLayer] += delta;

                // Normalize
                float sum = 0;
                for (int c = 0; c < lc; c++) sum += weights[c];
                if (sum > 0)
                    for (int c = 0; c < lc; c++)
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

    private static void ApplyHolesBrush(TerrainData data, Float2 uv, ref bool changed)
    {
        bool shiftHeld = Input.GetKey(KeyCode.ShiftLeft) || Input.GetKey(KeyCode.ShiftRight);
        byte value = shiftHeld ? (byte)255 : (byte)0; // Shift = fill, normal = dig hole

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

                data.SetHole(x, z, value);
                changed = true;
            }
        }
    }

    private static void ApplyDetailBrush(TerrainData data, Float2 uv, float dt, ref bool changed)
    {
        if (ActiveDetailIndex < 0 || ActiveDetailIndex >= data.DetailLayers.Count) return;

        bool shiftHeld = Input.GetKey(KeyCode.ShiftLeft) || Input.GetKey(KeyCode.ShiftRight);
        float sign = shiftHeld ? -1f : 1f;

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
                float delta = BrushStrength * falloff * dt * 3f * sign;

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
