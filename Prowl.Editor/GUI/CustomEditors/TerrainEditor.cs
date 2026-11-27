// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.GUI.SceneView.Editors;
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
using TerrainBrush = Prowl.Graphite.Rendering.TerrainBrush;
using TerrainEdited = Prowl.Graphite.Rendering.TerrainEdited;
using TerrainLayers = Prowl.Graphite.Rendering.TerrainLayers;
namespace Prowl.Editor.Inspector;

public enum TerrainTab { Height, Paint, Holes, Details, Trees, Settings }
public enum HeightTool { Raise, Lower, Flatten, Smooth }

/// <summary>The terrain brush values kept between editor sessions.</summary>
public sealed class TerrainBrushSettings
{
    public TerrainTab ActiveTab { get; set; } = TerrainTab.Height;
    public HeightTool ActiveHeightTool { get; set; } = HeightTool.Raise;
    public float BrushSize { get; set; } = 5f;
    public float BrushStrength { get; set; } = 0.5f;
    public float FlattenHeight { get; set; } = 0.5f;
    public float TreeBrushSize { get; set; } = 10f;
    public int TreesPerStroke { get; set; } = 3;
}

[CustomEditor(typeof(TerrainComponent))]
public class TerrainEditor : CustomEditor
{
    private static TerrainBrushSettings Brush => SceneToolManager.Settings<TerrainBrushSettings>();

    // Brush state persists across selection changes and editor sessions
    public static TerrainTab ActiveTab { get => Brush.ActiveTab; set => Brush.ActiveTab = value; }
    public static HeightTool ActiveHeightTool { get => Brush.ActiveHeightTool; set => Brush.ActiveHeightTool = value; }
    public static float BrushSize { get => Brush.BrushSize; set => Brush.BrushSize = value; }
    public static float BrushStrength { get => Brush.BrushStrength; set => Brush.BrushStrength = value; }
    public static float FlattenHeight { get => Brush.FlattenHeight; set => Brush.FlattenHeight = value; }
    public static float TreeBrushSize { get => Brush.TreeBrushSize; set => Brush.TreeBrushSize = value; }
    public static int TreesPerStroke { get => Brush.TreesPerStroke; set => Brush.TreesPerStroke = value; }

    public static int PaintLayer { get; set; }
    public static int ActiveDetailIndex { get; set; }
    public static int ActiveTreePrototype { get; set; }

    // Instance state
    private TerrainComponent? _terrain;

    // A database TerrainData goes in the dirty asset set, embedded data marks the terrain's scene.
    private void MarkDataDirty()
    {
        if (_terrain.IsNotValid()) return;
        if (_terrain.Data.IsValid() && _terrain.Data.IsFromDatabase) EditorUtility.SetDirty(_terrain.Data);
        else EditorUtility.SetDirty(_terrain);
    }

    public override void OnGUI(Paper paper, string id, object target)
    {
        ActiveInstance = this;

        _terrain = (TerrainComponent)target;
        TerrainComponent terrain = _terrain;
        TerrainData? terrainData = terrain.Data;
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Terrain Data asset ref (outside tabs - required before any tab content)
        PropertyGridUtils.DrawField(paper, $"{id}_data", "Terrain Data", typeof(TerrainData), terrain.Data,
            v => { Undo.RecordObject(terrain, "Terrain Data"); terrain.Data = v as TerrainData; }, 0);

        if (terrainData.IsNotValid())
        {
            DrawEmptyState(paper, $"{id}_empty", font);
            return;
        }

        // Dirty banner with an inline save-to-asset action.
        if (terrainData.IsFromDatabase && EditorUtility.IsDirty(terrainData))
            DrawDirtyBanner(paper, $"{id}_dirty", font, terrainData);

        // Main card: a bordered, clipped panel laid out as [ rail | content ].
        using (paper.Row($"{id}_main").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Margin(8, 8, 6, 0).Rounded(Origami.Current.Metrics.ContainerRounding).Clip()
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
            .Margin(8, 8, 8, 8).Padding(20, 20, 26, 24).Rounded(Origami.Current.Metrics.ContainerRounding)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1).Enter())
        {
            paper.Box($"{id}_ic").Width(58).Height(58).Rounded(Origami.Current.Metrics.Rounding)
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
    private static void DrawDirtyBanner(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        using (paper.Row(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto).MinHeight(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingLarge)
            .Padding(9, 9, 6, 6).Rounded(m.ContainerRounding).Gap(m.SpacingMedium)
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
        (IOrigamiIcon? icon, string? name) = ModeInfo(ActiveTab);
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        using (paper.Column(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto).Margin(0, 0, 0, 6).Enter())
        {
            using (paper.Row($"{id}_r").Width(UnitValue.StretchOne).Height(24)
                .Padding(12, 12, 2, 8).Gap(7).IsNotInteractable().Enter())
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

    private static void DrawSculpt(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
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
        HintPill(paper, $"{id}_hint", font, "Target height is a share of the terrain's height");

        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private void DrawPaint(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        OrigamiMetrics m = Origami.Current.Metrics;

        // Clamp the persisted paint layer to the current layer set.
        if (PaintLayer >= data.LayerCount) PaintLayer = data.LayerCount - 1;
        if (PaintLayer < 0) PaintLayer = 0;

        DrawSectionActions(paper, $"{id}_lh", "Splat Layers", () =>
        {
            MiniButton(paper, $"{id}_add", EditorIcons.Plus_I, () =>
            {
                data.AddLayer(new TerrainLayer());
                MarkDataDirty();
            }, enabled: data.LayerCount < TerrainLayers.MaxLayers);
            MiniButton(paper, $"{id}_rem", EditorIcons.Trash_I, () =>
            {
                data.RemoveLayer(PaintLayer);
                PaintLayer = Math.Clamp(PaintLayer, 0, Math.Max(0, data.LayerCount - 1));
                MarkDataDirty();
            }, danger: true, enabled: data.LayerCount > 1);
        });

        // Layer list.
        using (paper.Column($"{id}_layers").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Padding(8, 8, 0, 0).Gap(2).Enter())
        {
            for (int i = 0; i < data.LayerCount; i++)
            {
                int idx = i;
                bool selected = PaintLayer == i;
                Texture2D? albedo = data.Layers[i].Albedo;
                string lname = albedo.IsValid() ? albedo.Name : $"Layer {i}";
                using (paper.Row($"{id}_l{i}").Width(UnitValue.StretchOne).Height(28).Rounded(m.Rounding).Padding(8, 8, 0, 0).Gap(8)
                    .BackgroundColor(selected ? EditorTheme.Selected : SColor.Transparent)
                    .Hovered.BackgroundColor(selected ? EditorTheme.Selected : EditorTheme.Hover).End()
                    .OnClick(_ => PaintLayer = idx).Enter())
                {
                    if (selected)
                        paper.Box($"{id}_l{i}_bar").Width(2).Height(UnitValue.StretchOne)
                            .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .BackgroundColor(EditorTheme.Accent).IsNotInteractable();
                    Guid albedoId = data.Layers[i].Albedo.IsValid() ? data.Layers[i].Albedo.AssetID : Guid.Empty;
                    Texture2D? albThumb = EditorAssetBackend.Instance?.GetThumbnailTexture(albedoId);
                    ElementBuilder swBox = paper.Box($"{id}_l{i}_sw").Width(16).Height(16).Rounded(m.SmallRounding)
                        .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                        .BorderColor(EditorTheme.WithAlpha(SColor.White, 38)).BorderWidth(1).IsNotInteractable();
                    if (albThumb.IsValid())
                        swBox.Clip().OnPostLayout((h, rect) => paper.Draw(ref h, (canvas, rr) =>
                            canvas.DrawImageRounded(albThumb, (float)rr.Min.X, (float)rr.Min.Y,
                                (float)rr.Size.X, (float)rr.Size.Y, m.SmallRounding)));
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
            .Text($"{data.LayerCount} / {TerrainLayers.MaxLayers} layers", font)
            .TextColor(EditorTheme.InkDim).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

        // Selected layer settings.
        if (data.LayerCount > 0)
        {
            TerrainLayer sl = data.Layers[PaintLayer];
            EditorGUI.SectionHeader(paper, $"{id}_slh", $"Layer {PaintLayer}");
            PropertyGridUtils.DrawField(paper, $"{id}_alb", "Albedo", typeof(Texture2D), sl.Albedo,
                v => { sl.Albedo = v as Texture2D; MarkDataDirty(); }, 0);
            PropertyGridUtils.DrawField(paper, $"{id}_nrm", "Normal Map", typeof(Texture2D), sl.NormalMap,
                v => { sl.NormalMap = v as Texture2D; MarkDataDirty(); }, 0);
            EditorGUI.Row(paper, $"{id}_til", "Tiling", () =>
                Origami.NumericField<float>(paper, $"{id}_til_v", sl.Tiling,
                    v => { sl.Tiling = MathF.Max(0.01f, v); MarkDataDirty(); }).Min(0.01f).Show());
            EditorGUI.Row(paper, $"{id}_rgh", "Roughness", () =>
                Origami.Slider(paper, $"{id}_rgh_v", sl.Roughness, v => { sl.Roughness = v; MarkDataDirty(); }, 0f, 1f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_hb", "Height Blend", () =>
                Origami.Slider(paper, $"{id}_hb_v", sl.HeightBlend, v => { sl.HeightBlend = v; MarkDataDirty(); }, 0f, 1f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_mv", "Macro Variation", () =>
                Origami.Slider(paper, $"{id}_mv_v", sl.MacroVariation, v => { sl.MacroVariation = v; MarkDataDirty(); }, 0f, 1f).Format("F2").Show());
            EditorGUI.SettingsToggle(paper, $"{id}_hex", "Hex Tiling", sl.HexTiling,
                v => { sl.HexTiling = v; MarkDataDirty(); }, separator: false);
        }

        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private static void DrawHoles(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
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
                DetailPrototype p = data.DetailPrototypes[i];
                if (p.RenderMode == DetailRenderMode.Mesh)
                {
                    Mesh? mesh = p.Mesh;
                    return mesh.IsValid() ? mesh.Name : "Empty";
                }
                Texture2D? tex = p.Texture;
                return tex.IsValid() ? tex.Name : "Empty";
            },
            i => data.DetailPrototypes[i].RenderMode == DetailRenderMode.Mesh ? EditorIcons.Cube_I : EditorIcons.Seedling_I,
            () => { data.AddDetailPrototype(new DetailPrototype()); MarkDetailsDirty(); },
            i =>
            {
                DetailPrototype p = data.DetailPrototypes[i];
                if (p.RenderMode == DetailRenderMode.Mesh)
                {
                    return p.Mesh.IsValid() ? p.Mesh.AssetID : Guid.Empty;
                }

                return p.Texture.IsValid() ? p.Texture.AssetID : Guid.Empty;
            });

        if (ActiveDetailIndex >= 0 && ActiveDetailIndex < data.DetailPrototypes.Count)
        {
            DetailPrototype dp = data.DetailPrototypes[ActiveDetailIndex];
            EditorGUI.SectionHeader(paper, $"{id}_sh", "Detail Settings");

            EditorGUI.Row(paper, $"{id}_mode", "Render Mode", () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", dp.RenderMode, v => { dp.RenderMode = v; MarkDetailsDirty(); }).Show());

            if (dp.RenderMode == DetailRenderMode.Mesh)
            {
                PropertyGridUtils.DrawField(paper, $"{id}_mesh", "Mesh", typeof(Mesh), dp.Mesh,
                    v => { dp.Mesh = v as Mesh; MarkDetailsDirty(); }, 0);
                DrawPrototypeMaterials(paper, $"{id}_mats", "Materials", dp.Mesh, dp.Materials);
            }
            else
            {
                PropertyGridUtils.DrawField(paper, $"{id}_tex", "Texture", typeof(Texture2D), dp.Texture,
                    v => { dp.Texture = v as Texture2D; MarkDetailsDirty(); }, 0);
            }

            EditorGUI.Row(paper, $"{id}_dens", "Density", () =>
                Origami.Slider(paper, $"{id}_dens_v", dp.Density, v => { dp.Density = v; MarkDetailsDirty(); }, 0f, 50f).Format("F1").Show());
            if (dp.RenderMode == DetailRenderMode.Grass)
                EditorGUI.Row(paper, $"{id}_range", "Range", () =>
                    Origami.Slider(paper, $"{id}_range_v", dp.Range, v => { dp.Range = v; MarkDetailsDirty(); }, 5f, 400f).Format("F0").Show());

            EditorGUI.Row(paper, $"{id}_minw", "Min Width", () =>
                Origami.Slider(paper, $"{id}_minw_v", dp.MinWidth, v => { dp.MinWidth = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_maxw", "Max Width", () =>
                Origami.Slider(paper, $"{id}_maxw_v", dp.MaxWidth, v => { dp.MaxWidth = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_minh", "Min Height", () =>
                Origami.Slider(paper, $"{id}_minh_v", dp.MinHeight, v => { dp.MinHeight = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_maxh", "Max Height", () =>
                Origami.Slider(paper, $"{id}_maxh_v", dp.MaxHeight, v => { dp.MaxHeight = v; MarkDetailsDirty(); }, 0.1f, 5f).Format("F2").Show());
            EditorGUI.Row(paper, $"{id}_noise", "Noise Size", () =>
                Origami.Slider(paper, $"{id}_noise_v", dp.NoiseSize, v => { dp.NoiseSize = v; MarkDetailsDirty(); }, 0.5f, 50f).Format("F1").Show());
            if (dp.RenderMode == DetailRenderMode.Grass)
            {
                EditorGUI.Row(paper, $"{id}_stiff", "Stiffness", () =>
                    Origami.Slider(paper, $"{id}_stiff_v", dp.Stiffness, v => { dp.Stiffness = v; MarkDetailsDirty(); }, 0f, 1f).Format("F2").Show());
                EditorGUI.Row(paper, $"{id}_gt", "Ground Tint", () =>
                    Origami.Slider(paper, $"{id}_gt_v", dp.GroundTint, v => { dp.GroundTint = v; MarkDetailsDirty(); }, 0f, 1f).Format("F2").Show());
                PropertyGridUtils.DrawField(paper, $"{id}_rc", "Root Color", typeof(Prowl.Vector.Color), dp.RootColor,
                    v => { dp.RootColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
                PropertyGridUtils.DrawField(paper, $"{id}_tc", "Tip Color", typeof(Prowl.Vector.Color), dp.TipColor,
                    v => { dp.TipColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
            }
            else
            {
                EditorGUI.SettingsToggle(paper, $"{id}_atn", "Align To Normal", dp.AlignToNormal,
                    v => { dp.AlignToNormal = v; MarkDetailsDirty(); }, separator: false);
                PropertyGridUtils.DrawField(paper, $"{id}_hc", "Healthy Color", typeof(Prowl.Vector.Color), dp.HealthyColor,
                    v => { dp.HealthyColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
                PropertyGridUtils.DrawField(paper, $"{id}_dc", "Dry Color", typeof(Prowl.Vector.Color), dp.DryColor,
                    v => { dp.DryColor = (Prowl.Vector.Color)v!; MarkDetailsDirty(); }, 0);
            }
        }

        DrawBrushBlock(paper, $"{id}_brush", font);
    }

    private void DrawTrees(Paper paper, string id, Prowl.Scribe.FontFile font, TerrainData data)
    {
        DrawSectionActions(paper, $"{id}_ph", "Tree Prototypes", () =>
        {
            MiniButton(paper, $"{id}_padd", EditorIcons.Plus_I,
                () => { data.TreePrototypes.Add(new TreePrototype()); MarkDataDirty(); });
            MiniButton(paper, $"{id}_prem", EditorIcons.Trash_I, () =>
            {
                if (ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Count)
                {
                    var preProtos = new List<TreePrototype>(data.TreePrototypes);
                    TerrainSnapshot before = data.Capture();
                    data.RemoveTreePrototype(ActiveTreePrototype);
                    var postProtos = new List<TreePrototype>(data.TreePrototypes);
                    TerrainSnapshot after = data.Capture();

                    if (_terrain.IsValid())
                        TerrainDataUndo.Register("Remove Tree Prototype", _terrain, data,
                            d => { d.TreePrototypes.Clear(); d.TreePrototypes.AddRange(preProtos); d.Restore(before); },
                            d => { d.TreePrototypes.Clear(); d.TreePrototypes.AddRange(postProtos); d.Restore(after); });

                    ActiveTreePrototype = Math.Clamp(ActiveTreePrototype, 0, Math.Max(0, data.TreePrototypes.Count - 1));
                    MarkDataDirty();
                }
            }, danger: true, enabled: data.TreePrototypes.Count > 0);
        });

        DrawProtoGrid(paper, $"{id}_grid", font, data.TreePrototypes.Count, ActiveTreePrototype,
            i => ActiveTreePrototype = i,
            i =>
            {
                Mesh? mesh = data.TreePrototypes[i].Mesh;
                return mesh.IsValid() ? mesh.Name : "Empty";
            },
            _ => EditorIcons.Leaf_I,
            () => { data.TreePrototypes.Add(new TreePrototype()); MarkDataDirty(); },
            i => data.TreePrototypes[i].Mesh.IsValid() ? data.TreePrototypes[i].Mesh.AssetID : Guid.Empty);

        if (data.TreePrototypes.Count > 0 && ActiveTreePrototype >= 0 && ActiveTreePrototype < data.TreePrototypes.Count)
        {
            TreePrototype proto = data.TreePrototypes[ActiveTreePrototype];
            EditorGUI.SectionHeader(paper, $"{id}_pth", "Prototype");
            PropertyGridUtils.DrawField(paper, $"{id}_mesh", "Mesh", typeof(Mesh), proto.Mesh,
                v => { proto.Mesh = v as Mesh; MarkDataDirty(); }, 0);
            DrawPrototypeMaterials(paper, $"{id}_mats", "Materials", proto.Mesh, proto.Materials);
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
        EditorGUI.SectionHeader(paper, $"{id}_mh", "Material", first: true);
        PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(Material), terrain.Material,
            v => { Undo.RecordObject(terrain, "Material"); terrain.Material = v as Material; }, 0);

        EditorGUI.SectionHeader(paper, $"{id}_dh", "Dimensions");
        EditorGUI.Row(paper, $"{id}_size", "Terrain Size", () =>
            Origami.NumericField<float>(paper, $"{id}_size_v", data.Size, v => { data.Size = MathF.Max(1f, v); MarkDataDirty(); }).Min(1f).Show());
        EditorGUI.Row(paper, $"{id}_height", "Terrain Height", () =>
            Origami.NumericField<float>(paper, $"{id}_height_v", data.Height, v => { data.Height = MathF.Max(0.1f, v); MarkDataDirty(); }).Min(0.1f).Show());

        string[] hmOptions = ["33", "65", "129", "257", "513", "1025", "2049", "4097"];
        int hmCurrent = Array.IndexOf(hmOptions, data.HeightmapResolution.ToString());
        if (hmCurrent < 0) hmCurrent = 4;
        EditorGUI.Row(paper, $"{id}_hmres", "Heightmap", () =>
            Origami.Dropdown(paper, $"{id}_hmres_v", hmCurrent, v =>
            {
                int newRes = int.Parse(hmOptions[v]);
                if (newRes != data.HeightmapResolution)
                    Origami.Confirm("Resample Terrain?",
                        $"Changing the heightmap to {newRes} samples resamples the heights, paint, holes and details, and clears undo for them.",
                        () => { data.ResizeHeightmap(newRes); MarkDataDirty(); });
            }, hmOptions).Show());

        EditorGUI.SectionHeader(paper, $"{id}_lh", "Level of Detail");
        EditorGUI.Row(paper, $"{id}_lodq", "LOD Quality", () =>
            Origami.Slider(paper, $"{id}_lodq_v", terrain.LODQuality,
                v => { Undo.RecordObject(terrain, "LOD Quality"); terrain.LODQuality = MathF.Max(1.5f, v); }, 1.5f, 6f).Format("F1").Show());

        EditorGUI.SectionHeader(paper, $"{id}_vh", "Vegetation");
        EditorGUI.Row(paper, $"{id}_detaildist", "Mesh Detail Distance", () =>
            Origami.Slider(paper, $"{id}_detaildist_v", terrain.DetailDistance,
                v => { Undo.RecordObject(terrain, "Detail Distance"); terrain.DetailDistance = MathF.Max(1f, v); }, 10f, 1000f).Format("F0").Show());
        EditorGUI.Row(paper, $"{id}_treedist", "Tree View Distance", () =>
            Origami.Slider(paper, $"{id}_treedist_v", terrain.TreeDistance,
                v => { Undo.RecordObject(terrain, "Tree View Distance"); terrain.TreeDistance = MathF.Max(1f, v); }, 50f, 2000f).Format("F0").Show());

        EditorGUI.SectionHeader(paper, $"{id}_shh", "Shadows");
        EditorGUI.Row(paper, $"{id}_cast", "Shadow Casting", () =>
            Origami.EnumDropdown(paper, $"{id}_cast_v", terrain.ShadowCasting, v => { Undo.RecordObject(terrain, "Shadow Casting"); terrain.ShadowCasting = v; }).Show());
        EditorGUI.Row(paper, $"{id}_recv", "Receive Shadows", () =>
            Origami.Checkbox(paper, $"{id}_recv_v", terrain.ReceiveShadows, v => { Undo.RecordObject(terrain, "Receive Shadows"); terrain.ReceiveShadows = v; }).Show());
    }

    /// <summary>Size / Strength / Falloff sliders (design .tr-brushrow).</summary>
    private static void DrawBrushBlock(Paper paper, string id, Prowl.Scribe.FontFile font, string title = "Brush")
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        EditorGUI.SectionHeader(paper, $"{id}_h", title);
        using (paper.Column($"{id}_col").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
        {
            EditorGUI.Row(paper, $"{id}_size", "Size", () =>
                Origami.Slider(paper, $"{id}_size_v", BrushSize, v => BrushSize = v, 1f, 500f).Format("F0").Show());
            EditorGUI.Row(paper, $"{id}_str", "Strength", () =>
                Origami.Slider(paper, $"{id}_str_v", BrushStrength, v => BrushStrength = v, 0f, 1f).Format("F2").Show());
        }
    }

    /// <summary>Uppercase accent section header with right-aligned action buttons (design .tr-sec-h + .tr-sec-actions).</summary>
    private static void DrawSectionActions(Paper paper, string id, string text, Action drawActions)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        Scribe.FontFile? semi = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        using (paper.Row(id).Width(UnitValue.StretchOne).Height(22)
            .Margin(m.PaddingLarge, m.PaddingLarge, 14, 4).Gap(4).Enter())
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
        using (paper.Box($"{id}_w").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).Enter())
        {
            ButtonBuilder b = Origami.IconButton(paper, id, icon, onClick).Small().Width(20).Height(20).Soft().Disabled(!enabled);
            if (danger) b.Danger();
            b.Show();
        }
    }

    /// <summary>Blue info pill (design .tr-hint).</summary>
    private static void HintPill(Paper paper, string id, Prowl.Scribe.FontFile font, string text)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        using (paper.Row(id).Width(UnitValue.StretchOne).Height(UnitValue.Auto).MinHeight(32)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.Spacing)
            .Padding(10, 10, 8, 8).Rounded(m.ContainerRounding).Gap(7)
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
    private static void DrawProtoGrid(Paper paper, string id, Prowl.Scribe.FontFile font, int count,
        int selected, Action<int> onSelect, Func<int, string> getName, Func<int, IOrigamiIcon> getIcon,
        Action onAdd, Func<int, Guid> getThumbGuid)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        const int cols = 4;
        int total = count + 1; // include the add tile
        int rows = (total + cols - 1) / cols;
        for (int r = 0; r < rows; r++)
        {
            using (paper.Row($"{id}_r{r}").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
                .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(6).Enter())
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx < count)
                    {
                        int capture = idx;
                        bool sel = selected == idx;
                        SColor tint = SwatchColor(idx);
                        using (paper.Column($"{id}_t{idx}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                            .Padding(3, 3, 6, 6).Gap(5).Rounded(m.ContainerRounding)
                            .BackgroundColor(sel ? EditorTheme.Selected : SColor.Transparent)
                            .Hovered.BackgroundColor(sel ? EditorTheme.Selected : EditorTheme.Hover).End()
                            .BorderColor(sel ? EditorTheme.WithAlpha(EditorTheme.Accent, 102) : SColor.Transparent).BorderWidth(1)
                            .OnClick(_ => onSelect(capture)).Enter())
                        {
                            Texture2D? thumb = EditorAssetBackend.Instance?.GetThumbnailTexture(getThumbGuid(idx));
                            ElementBuilder thumbBox = paper.Box($"{id}_t{idx}_th").Width(46).Height(46).Rounded(m.Rounding)
                                .Margin(UnitValue.Stretch(), UnitValue.Stretch(), 0, 0)
                                .BackgroundColor(EditorTheme.WithAlpha(tint, 34))
                                .BorderColor(EditorTheme.WithAlpha(tint, 85)).BorderWidth(1)
                                .IsNotInteractable();
                            if (thumb.IsValid())
                                thumbBox.Clip().OnPostLayout((h, rect) => paper.Draw(ref h, (canvas, rr) =>
                                    canvas.DrawImageRounded(thumb, (float)rr.Min.X, (float)rr.Min.Y,
                                        (float)rr.Size.X, (float)rr.Size.Y, m.Rounding)));
                            else
                                thumbBox.Icon(paper, getIcon(idx), tint, size: 20f);
                            paper.Box($"{id}_t{idx}_n").Width(UnitValue.StretchOne).Height(12).IsNotInteractable()
                                .Text(getName(idx), font).TextColor(EditorTheme.Ink400)
                                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter).TextTruncate();
                        }
                    }
                    else if (idx == count)
                    {
                        paper.Box($"{id}_add").Width(UnitValue.Stretch()).Height(70).Rounded(m.ContainerRounding)
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
    private void DrawPrototypeMaterials(Paper paper, string id, string label, Mesh? mesh, List<Material?> materials)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        // Left accent strip (design .tr-matlist) wrapping the per-submesh fields.
        using (paper.Row($"{id}_wrap").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
            .Margin(16, m.PaddingLarge, 0, m.SpacingLarge).Enter())
        {
            paper.Box($"{id}_strip").Width(2).Height(UnitValue.StretchOne)
                .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            using (paper.Column($"{id}_col").Width(UnitValue.StretchOne).Height(UnitValue.Auto).Enter())
            {
                if (mesh.IsNotValid())
                {
                    // No mesh assigned show a single material field (will end up as submesh 0).
                    AssetRef<Material> single = materials.Count > 0 ? materials[0] : default;
                    PropertyGridUtils.DrawField(paper, $"{id}_single", label, typeof(Material), single,
                        v =>
                        {
                            var val = v as Material;
                            if (materials.Count == 0) materials.Add(val); else materials[0] = val;
                            MarkDataDirty(); MarkDetailsDirty();
                        }, 0);
                    return;
                }

                int subCount = mesh.SubMeshCount;
                // One slot per submesh is shown, and the list only grows when a missing slot is assigned.
                for (int i = 0; i < subCount; i++)
                {
                    int capturedIndex = i;
                    string slotLabel = subCount > 1 ? $"{label} [{i}]" : label;
                    Material? current = i < materials.Count ? materials[i] : null;
                    PropertyGridUtils.DrawField(paper, $"{id}_{i}", slotLabel, typeof(Material), current,
                        v =>
                        {
                            while (materials.Count <= capturedIndex) materials.Add(null);
                            materials[capturedIndex] = v as Material;
                            MarkDataDirty(); MarkDetailsDirty();
                        }, 0);
                }
            }
        }
    }

    #endregion

    #region Brush Application

    /// <summary>
    /// Applies the active tab's brush at a point across the terrain, for one frame of a held stroke. Shift lowers,
    /// fills holes or thins details. Returns whether anything changed.
    /// </summary>
    public static bool ApplyBrush(TerrainData data, Float2 terrainUV, float deltaTime, bool shift)
    {
        Float2 at = terrainUV * data.Size;
        float rate = BrushStrength * deltaTime;
        TerrainEdited edited = ActiveTab switch
        {
            TerrainTab.Height => ActiveHeightTool switch
            {
                HeightTool.Raise or HeightTool.Lower => data.Edit(TerrainBrush.Raise, at, BrushSize,
                    rate * 1.5f * data.Height * (ActiveHeightTool == HeightTool.Lower ^ shift ? -1f : 1f)),
                HeightTool.Flatten => data.Edit(TerrainBrush.Flatten, at, BrushSize, MathF.Min(rate * 1.5f, 1f), FlattenHeight * data.Height),
                _ => data.Edit(TerrainBrush.Smooth, at, BrushSize, MathF.Min(rate * 1.5f, 1f)),
            },
            TerrainTab.Paint => data.Edit(TerrainBrush.Paint, at, BrushSize, MathF.Min(rate * 5f, 1f), layer: PaintLayer),
            TerrainTab.Holes => data.Edit(TerrainBrush.Hole, at, BrushSize, shift ? -1f : 1f),
            TerrainTab.Details => data.Edit(TerrainBrush.Detail, at, BrushSize, Math.Clamp(rate * 3f, 0f, 1f) * (shift ? -1f : 1f), layer: ActiveDetailIndex),
            _ => new TerrainEdited(default, default, []),
        };
        return edited.Pages.Count > 0;
    }

    #endregion

    #region Save

    private static void SaveToAsset(TerrainData data)
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db != null && data.AssetID != Guid.Empty)
        {
            if (db.SaveAsset(data)) EditorUtility.ClearAssetDirty(data.AssetID);
            Debug.Log("[TerrainEditor] Saved terrain data.");
        }
        else
        {
            Debug.LogWarning("[TerrainEditor] Cannot save TerrainData has no asset ID.");
        }
    }

    /// <summary>Places trees of the active prototype at random within the tree brush around a point across the terrain, returning how many.</summary>
    public static int PlaceTrees(TerrainData data, Float2 uv)
    {
        if (data.TreePrototypes.Count == 0 || ActiveTreePrototype >= data.TreePrototypes.Count)
            return 0;

        var rng = new Random((int)(uv.X * 10000) ^ (int)(uv.Y * 10000) ^ Environment.TickCount);
        float brushRadiusUV = TreeBrushSize / data.Size;
        List<TreeInstance> placed = [];
        for (int i = 0; i < TreesPerStroke; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float radius = (float)(rng.NextDouble() * brushRadiusUV);
            Float2 pos = uv + new Float2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            if (pos.X < 0 || pos.X > 1 || pos.Y < 0 || pos.Y > 1) continue;

            float scale = 0.8f + (float)rng.NextDouble() * 0.4f;
            placed.Add(new TreeInstance
            {
                Position = pos,
                PrototypeIndex = ActiveTreePrototype,
                Rotation = (float)(rng.NextDouble() * Math.PI * 2),
                WidthScale = scale,
                HeightScale = scale,
            });
        }
        if (placed.Count > 0)
            data.AddTrees(placed);
        return placed.Count;
    }

    /// <summary>Removes the trees within the tree brush around a point across the terrain, returning how many.</summary>
    public static int RemoveTrees(TerrainData data, Float2 uv) => data.RemoveTrees(uv, TreeBrushSize / data.Size);

    /// <summary>Mark the terrain data as dirty (called from TerrainSceneEditor after brush strokes).</summary>
    public void MarkDirty() => MarkDataDirty();

    private void MarkDetailsDirty() => MarkDataDirty();

    // Static accessor for the scene editor to find the active TerrainEditor instance
    internal static TerrainEditor? ActiveInstance { get; set; }

    #endregion

    #region Editor Callbacks

    /// <summary>Auto-save all TerrainData assets when the scene is saved.</summary>
    [OnSceneSaved]
    static void OnSceneSaved()
    {
        if (EditorSceneManager.EditedWorld.IsNotValid()) return;
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null) return;

        foreach (GameObject go in EditorSceneManager.EditedWorld.Objects.Where(o => o.EnabledInHierarchy))
        {
            TerrainComponent? terrain = go.GetComponent<Runtime.Terrain.TerrainComponent>();
            if (terrain.IsNotValid()) continue;

            TerrainData? terrainData = terrain.Data;
            if (terrainData.IsNotValid() || terrainData.AssetID == Guid.Empty) continue;

            try
            {
                if (db.SaveAsset(terrainData)) EditorUtility.ClearAssetDirty(terrainData.AssetID);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save TerrainData '{terrainData.Name}': {ex.Message}");
            }
        }
    }

    #endregion
}
