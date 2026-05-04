// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Dockable editor window that drives <see cref="PBRTextureForge"/>: assign source
/// textures into slots (Diffuse / Height / Normal / Metallic / Smoothness / AO / Edge),
/// pick a target slot to generate, tweak its settings, and save the result as a sibling
/// PNG next to the source file.
/// </summary>
[EditorWindow("Tools/PBR Forge")]
public class PBRForgeWindow : DockPanel
{
    public override string Title => "PBR Forge";
    public override string Icon => EditorIcons.WandMagicSparkles;

    // ─── Target slots (outputs we generate/assign) ────────────────────────────────────
    private enum Slot { Diffuse, Height, Normal, Metallic, Smoothness, AO, Edge }
    private static readonly Slot[] SlotsInOrder = (Slot[])Enum.GetValues(typeof(Slot));

    private readonly Dictionary<Slot, Texture2D?> _textures = new();
    private Slot _activeSlot = Slot.Height;

    // Per-generator settings — one per kernel.
    private PBRTextureForge.HeightFromDiffuseSettings _heightSettings = PBRTextureForge.HeightFromDiffuseSettings.Default;
    private PBRTextureForge.NormalFromHeightSettings _normalSettings = PBRTextureForge.NormalFromHeightSettings.Default;
    private PBRTextureForge.EdgeFromNormalSettings _edgeSettings = PBRTextureForge.EdgeFromNormalSettings.Default;
    private PBRTextureForge.AOSettings _aoSettings = PBRTextureForge.AOSettings.Default;
    private PBRTextureForge.MetallicSettings _metallicSettings = PBRTextureForge.MetallicSettings.Default;
    private PBRTextureForge.SmoothnessSettings _smoothnessSettings = PBRTextureForge.SmoothnessSettings.Default;

    // Source-file info for deriving sibling save paths. Set when opened from a texture.
    private string? _sourceRelPath;
    private string? _sourceBaseName;

    public PBRForgeWindow()
    {
        foreach (var s in SlotsInOrder) _textures[s] = null;
    }

    /// <summary>Open a PBR Forge window seeded with the given texture as Diffuse.</summary>
    public static void OpenFor(Texture2D diffuse)
    {
        var panel = new PBRForgeWindow();
        panel._textures[Slot.Diffuse] = diffuse;

        // If the texture is a project asset, remember the path so Save can drop PNG siblings.
        if (!string.IsNullOrEmpty(diffuse.AssetPath) && Project.Current != null)
        {
            var abs = diffuse.AssetPath!;
            if (abs.StartsWith(Project.Current.AssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                panel._sourceRelPath = Path.GetRelativePath(Project.Current.AssetsPath, abs).Replace('\\', '/');
                panel._sourceBaseName = Path.GetFileNameWithoutExtension(abs);
            }
        }

        EditorApplication.Instance?.OpenPanelInstance(panel, 760, 620);
    }

    // ─── OnGUI ────────────────────────────────────────────────────────────────────────
    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        EditorGUI.Header(paper, "pbrforge_hdr",
            _sourceBaseName != null ? $"{Icon}  PBR Forge — {_sourceBaseName}" : $"{Icon}  PBR Forge");

        paper.Box("pbrforge_sp1").Height(6);

        DrawSlotGrid(paper);

        paper.Box("pbrforge_sp2").Height(10);
        EditorGUI.Separator(paper, "pbrforge_sep");

        DrawActiveSlotPanel(paper);
    }

    // ─── Slot grid ────────────────────────────────────────────────────────────────────
    private void DrawSlotGrid(Paper paper)
    {
        const int Columns = 4;
        const float SlotSize = 140f;
        const float Gap = 8f;

        int rowCount = (SlotsInOrder.Length + Columns - 1) / Columns;
        for (int row = 0; row < rowCount; row++)
        {
            using (paper.Row($"pbrforge_row_{row}").Height(SlotSize + 44).RowBetween(Gap).Enter())
            {
                for (int col = 0; col < Columns; col++)
                {
                    int idx = row * Columns + col;
                    if (idx >= SlotsInOrder.Length)
                    {
                        paper.Box($"pbrforge_spacer_{row}_{col}").Width(SlotSize);
                        continue;
                    }
                    DrawSlot(paper, SlotsInOrder[idx], SlotSize);
                }
            }
        }
    }

    private void DrawSlot(Paper paper, Slot slot, float size)
    {
        string id = $"pbrforge_slot_{slot}";
        bool isActive = slot == _activeSlot;

        using (paper.Column(id)
            .Width(size).Height(size + 38)
            .ChildTop(2).ChildBottom(2)
            .Enter())
        {
            // Header label
            paper.Box($"{id}_hdr").Height(18)
                .Text(slot.ToString(), EditorTheme.DefaultFont!)
                .TextColor(isActive ? EditorTheme.Ink500 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleCenter);

            // Thumbnail / empty box — acts as drag target, click = activate.
            var thumb = paper.Box($"{id}_thumb")
                .Width(UnitValue.Stretch()).Height(size - 20)
                .Rounded(4)
                .BackgroundColor(isActive ? PBRForgeUtils.SlotBgHover : PBRForgeUtils.SlotBg)
                .BorderColor(PBRForgeUtils.SlotBorder).BorderWidth(1)
                .OnClick(_ => _activeSlot = slot);
            thumb.DrawTextureInto(paper, _textures[slot]);

            using (thumb.Enter())
            {
                // Drag-drop: accept Texture2D assets into this slot while hovered.
                var dropped = DragDrop.AcceptDrop<AssetDragPayload>(paper.IsParentHovered,
                    dp => dp.AssetType != null && typeof(Texture2D).IsAssignableFrom(dp.AssetType));
                if (dropped != null)
                {
                    var tex = Runtime.AssetDatabase.Get(dropped.AssetGuid) as Texture2D;
                    if (tex != null) _textures[slot] = tex;
                }
            }

            // Footer row: asset picker + clear.
            using (paper.Row($"{id}_ftr").Height(18).RowBetween(4).Enter())
            {
                EditorGUI.Button(paper, $"{id}_pick", "Pick", width: (size - 6) * 0.5f)
                    .OnValueChanged(_ => SelectorModal.Open($"Pick {slot}", typeof(Texture2D),
                        SelectorTabs.Assets,
                        picked => { if (picked is Texture2D t) _textures[slot] = t; }));

                EditorGUI.Button(paper, $"{id}_clr", "Clear", width: (size - 6) * 0.5f)
                    .OnValueChanged(_ => _textures[slot] = null);
            }
        }
    }

    // ─── Active slot settings + generate + save panel ─────────────────────────────────
    private void DrawActiveSlotPanel(Paper paper)
    {
        EditorGUI.Header(paper, "pbrforge_active_hdr", $"Generator: {_activeSlot}");

        // Missing-input check: what does this slot need?
        var missing = GetMissingInputs(_activeSlot);
        if (missing != null)
        {
            paper.Box("pbrforge_missing").Height(22)
                .Text($"⚠  {missing}", EditorTheme.DefaultFont!)
                .TextColor(PBRForgeUtils.MissingInputColor)
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);
        }

        switch (_activeSlot)
        {
            case Slot.Height:     DrawHeightSettings(paper); break;
            case Slot.Normal:     DrawNormalSettings(paper); break;
            case Slot.Edge:       DrawEdgeSettings(paper); break;
            case Slot.AO:         DrawAOSettings(paper); break;
            case Slot.Metallic:   DrawMetallicSettings(paper); break;
            case Slot.Smoothness: DrawSmoothnessSettings(paper); break;
            case Slot.Diffuse:
                EditorGUI.Label(paper, "pbrforge_d_note", "Diffuse is an input — no generator. Drag/Pick one.");
                break;
        }

        paper.Box("pbrforge_btnsp").Height(6);

        using (paper.Row("pbrforge_gen_row").Height(28).RowBetween(8).Enter())
        {
            bool canGenerate = missing == null;
            EditorGUI.Button(paper, "pbrforge_gen", canGenerate ? "Generate" : "Generate (inputs missing)", width: 180)
                .OnValueChanged(_ => { if (canGenerate) RunGenerate(_activeSlot); });

            bool hasOutput = _textures[_activeSlot] != null;
            if (hasOutput)
            {
                EditorGUI.Button(paper, "pbrforge_save", $"{EditorIcons.FloppyDisk}  Save as PNG", width: 150)
                    .OnValueChanged(_ => SaveSlot(_activeSlot));
            }
        }
    }

    /// <summary>Returns null when the slot can be generated, or a red-label string when not.</summary>
    private string? GetMissingInputs(Slot slot)
    {
        switch (slot)
        {
            case Slot.Diffuse:
                return "Diffuse is the source — drag or Pick an image";
            case Slot.Height:
                if (_textures[Slot.Diffuse] != null) return null;            // Diffuse → Height
                if (_textures[Slot.Normal] != null) return null;             // Normal → Height
                return "Missing Diffuse or Normal";
            case Slot.Normal:
                if (_textures[Slot.Height] != null) return null;             // Height → Normal (+opt Diffuse)
                return "Missing Height (Diffuse optional for shape recognition)";
            case Slot.Edge:
                return _textures[Slot.Normal] == null ? "Missing Normal" : null;
            case Slot.AO:
                return _textures[Slot.Normal] == null ? "Missing Normal (Height optional)" : null;
            case Slot.Metallic:
                return _textures[Slot.Diffuse] == null ? "Missing Diffuse" : null;
            case Slot.Smoothness:
                return _textures[Slot.Diffuse] == null ? "Missing Diffuse (Metallic optional)" : null;
            default:
                return null;
        }
    }

    // ─── Per-slot settings panels ─────────────────────────────────────────────────────
    private void DrawHeightSettings(Paper paper)
    {
        if (_textures[Slot.Diffuse] != null)
        {
            EditorGUI.Label(paper, "pbrforge_h_mode", "Source: Diffuse (multi-octave frequency decomposition)");
            EditorGUI.FloatField(paper, "pbrforge_h_contrast", _heightSettings.FinalContrast, label: "Final Contrast")
                .OnValueChanged(v => _heightSettings.FinalContrast = v);
            EditorGUI.FloatField(paper, "pbrforge_h_bias", _heightSettings.FinalBias, label: "Final Bias")
                .OnValueChanged(v => _heightSettings.FinalBias = v);
            EditorGUI.FloatField(paper, "pbrforge_h_gain", _heightSettings.FinalGain, label: "Final Gain (1 = identity)")
                .OnValueChanged(v => _heightSettings.FinalGain = v);
            EditorGUI.FloatField(paper, "pbrforge_h_spread", _heightSettings.SpreadBoost, label: "Spread Boost")
                .OnValueChanged(v => _heightSettings.SpreadBoost = v);
        }
        else
        {
            // Normal-only fallback uses HeightFromNormalSettings but we stash it in _normalSettings
            // via a shim: use SpreadBoost from height settings, expose contrast separately.
            EditorGUI.Label(paper, "pbrforge_h_mode", "Source: Normal (directional spiral integration)");
            EditorGUI.FloatField(paper, "pbrforge_hn_contrast", _heightSettings.FinalContrast, label: "Final Contrast")
                .OnValueChanged(v => _heightSettings.FinalContrast = v);
            EditorGUI.FloatField(paper, "pbrforge_hn_bias", _heightSettings.FinalBias, label: "Final Bias")
                .OnValueChanged(v => _heightSettings.FinalBias = v);
        }
    }

    private void DrawNormalSettings(Paper paper)
    {
        EditorGUI.FloatField(paper, "pbrforge_n_slope", _normalSettings.SlopeContrast, label: "Slope Contrast (20 default)")
            .OnValueChanged(v => _normalSettings.SlopeContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_n_final", _normalSettings.FinalContrast, label: "Final Contrast")
            .OnValueChanged(v => _normalSettings.FinalContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_n_ang", _normalSettings.Angularity, label: "Angularity")
            .OnValueChanged(v => _normalSettings.Angularity = v);
        EditorGUI.FloatField(paper, "pbrforge_n_angi", _normalSettings.AngularIntensity, label: "Angular Intensity")
            .OnValueChanged(v => _normalSettings.AngularIntensity = v);
        EditorGUI.FloatField(paper, "pbrforge_n_shape", _normalSettings.ShapeRecognition, label: "Shape Recognition (needs Diffuse)")
            .OnValueChanged(v => _normalSettings.ShapeRecognition = v);
        EditorGUI.Toggle(paper, "pbrforge_n_ogl", "OpenGL Y (uncheck for DirectX)", _normalSettings.OpenGLNormalY)
            .OnValueChanged(v => _normalSettings.OpenGLNormalY = v);
    }

    private void DrawEdgeSettings(Paper paper)
    {
        EditorGUI.FloatField(paper, "pbrforge_e_slope", _edgeSettings.SlopeContrast, label: "Slope Contrast")
            .OnValueChanged(v => _edgeSettings.SlopeContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_e_edge", _edgeSettings.EdgeAmount, label: "Edge Amount")
            .OnValueChanged(v => _edgeSettings.EdgeAmount = v);
        EditorGUI.FloatField(paper, "pbrforge_e_crev", _edgeSettings.CreviceAmount, label: "Crevice Amount")
            .OnValueChanged(v => _edgeSettings.CreviceAmount = v);
        EditorGUI.FloatField(paper, "pbrforge_e_pinch", _edgeSettings.Pinch, label: "Pinch (sharpen)")
            .OnValueChanged(v => _edgeSettings.Pinch = v);
        EditorGUI.FloatField(paper, "pbrforge_e_pillow", _edgeSettings.Pillow, label: "Pillow (round)")
            .OnValueChanged(v => _edgeSettings.Pillow = v);
        EditorGUI.FloatField(paper, "pbrforge_e_final", _edgeSettings.FinalContrast, label: "Final Contrast")
            .OnValueChanged(v => _edgeSettings.FinalContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_e_bias", _edgeSettings.FinalBias, label: "Final Bias")
            .OnValueChanged(v => _edgeSettings.FinalBias = v);
    }

    private void DrawAOSettings(Paper paper)
    {
        bool hasHeight = _textures[Slot.Height] != null;
        EditorGUI.Label(paper, "pbrforge_ao_mode", hasHeight
            ? "Source: Normal + Height (both contributions)"
            : "Source: Normal only (Height optional for depth term)");
        EditorGUI.FloatField(paper, "pbrforge_ao_spread", _aoSettings.Spread, label: "Spread (pixels)")
            .OnValueChanged(v => _aoSettings.Spread = v);
        EditorGUI.FloatField(paper, "pbrforge_ao_depth", _aoSettings.Depth, label: "Depth (height scale)")
            .OnValueChanged(v => _aoSettings.Depth = v);
        EditorGUI.FloatField(paper, "pbrforge_ao_blend", _aoSettings.DepthBlend, label: "Depth Blend (0=normals, 1=depth)")
            .OnValueChanged(v => _aoSettings.DepthBlend = v);
        EditorGUI.FloatField(paper, "pbrforge_ao_final", _aoSettings.FinalContrast, label: "Final Contrast")
            .OnValueChanged(v => _aoSettings.FinalContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_ao_bias", _aoSettings.FinalBias, label: "Final Bias")
            .OnValueChanged(v => _aoSettings.FinalBias = v);
        EditorGUI.IntField(paper, "pbrforge_ao_iter", _aoSettings.Iterations, "Iterations (more = smoother)")
            .OnValueChanged(v => _aoSettings.Iterations = Math.Max(1, v));
    }

    private void DrawMetallicSettings(Paper paper)
    {
        EditorGUI.FloatField(paper, "pbrforge_m_r", _metallicSettings.MetalColor.X, label: "Metal Color R")
            .OnValueChanged(v => _metallicSettings.MetalColor = new Float4(v, _metallicSettings.MetalColor.Y, _metallicSettings.MetalColor.Z, 1));
        EditorGUI.FloatField(paper, "pbrforge_m_g", _metallicSettings.MetalColor.Y, label: "Metal Color G")
            .OnValueChanged(v => _metallicSettings.MetalColor = new Float4(_metallicSettings.MetalColor.X, v, _metallicSettings.MetalColor.Z, 1));
        EditorGUI.FloatField(paper, "pbrforge_m_b", _metallicSettings.MetalColor.Z, label: "Metal Color B")
            .OnValueChanged(v => _metallicSettings.MetalColor = new Float4(_metallicSettings.MetalColor.X, _metallicSettings.MetalColor.Y, v, 1));
        EditorGUI.FloatField(paper, "pbrforge_m_hw", _metallicSettings.HueWeight, label: "Hue Weight")
            .OnValueChanged(v => _metallicSettings.HueWeight = v);
        EditorGUI.FloatField(paper, "pbrforge_m_sw", _metallicSettings.SatWeight, label: "Sat Weight")
            .OnValueChanged(v => _metallicSettings.SatWeight = v);
        EditorGUI.FloatField(paper, "pbrforge_m_lw", _metallicSettings.LumWeight, label: "Lum Weight")
            .OnValueChanged(v => _metallicSettings.LumWeight = v);
        EditorGUI.FloatField(paper, "pbrforge_m_lo", _metallicSettings.MaskLow, label: "Mask Low")
            .OnValueChanged(v => _metallicSettings.MaskLow = v);
        EditorGUI.FloatField(paper, "pbrforge_m_hi", _metallicSettings.MaskHigh, label: "Mask High")
            .OnValueChanged(v => _metallicSettings.MaskHigh = v);
        EditorGUI.FloatField(paper, "pbrforge_m_ov", _metallicSettings.BlurOverlay, label: "Overlay Strength")
            .OnValueChanged(v => _metallicSettings.BlurOverlay = v);
        EditorGUI.FloatField(paper, "pbrforge_m_fc", _metallicSettings.FinalContrast, label: "Final Contrast")
            .OnValueChanged(v => _metallicSettings.FinalContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_m_fb", _metallicSettings.FinalBias, label: "Final Bias")
            .OnValueChanged(v => _metallicSettings.FinalBias = v);
    }

    private void DrawSmoothnessSettings(Paper paper)
    {
        EditorGUI.FloatField(paper, "pbrforge_s_base", _smoothnessSettings.BaseSmoothness, label: "Base Smoothness")
            .OnValueChanged(v => _smoothnessSettings.BaseSmoothness = v);
        EditorGUI.FloatField(paper, "pbrforge_s_metal", _smoothnessSettings.MetalSmoothness, label: "Metal Smoothness (when masked)")
            .OnValueChanged(v => _smoothnessSettings.MetalSmoothness = v);
        EditorGUI.FloatField(paper, "pbrforge_s_ov", _smoothnessSettings.BlurOverlay, label: "Overlay Strength")
            .OnValueChanged(v => _smoothnessSettings.BlurOverlay = v);
        EditorGUI.FloatField(paper, "pbrforge_s_fc", _smoothnessSettings.FinalContrast, label: "Final Contrast")
            .OnValueChanged(v => _smoothnessSettings.FinalContrast = v);
        EditorGUI.FloatField(paper, "pbrforge_s_fb", _smoothnessSettings.FinalBias, label: "Final Bias")
            .OnValueChanged(v => _smoothnessSettings.FinalBias = v);
        EditorGUI.Label(paper, "pbrforge_s_note",
            "(3 colour-sample controls are in the engine but hidden from the v1 UI.)");
    }

    // ─── Actions ──────────────────────────────────────────────────────────────────────
    private void RunGenerate(Slot slot)
    {
        try
        {
            Texture2D? result = null;
            switch (slot)
            {
                case Slot.Height:
                    // Prefer Diffuse (richer frequency pyramid); fall back to Normal.
                    if (_textures[Slot.Diffuse] != null)
                        result = PBRTextureForge.GenerateHeightFromDiffuse(_textures[Slot.Diffuse]!, _heightSettings);
                    else if (_textures[Slot.Normal] != null)
                        result = PBRTextureForge.GenerateHeightFromNormal(_textures[Slot.Normal]!,
                            new PBRTextureForge.HeightFromNormalSettings
                            {
                                Spread = 50, SpreadBoost = 1, SamplesPerIter = 50, Iterations = 99,
                                FinalContrast = _heightSettings.FinalContrast,
                                FinalBias = _heightSettings.FinalBias,
                                OpenGLNormalY = true,
                            });
                    break;

                case Slot.Normal:
                    result = PBRTextureForge.GenerateNormalFromHeight(
                        _textures[Slot.Height]!,
                        _textures[Slot.Diffuse],   // optional
                        _normalSettings);
                    break;

                case Slot.Edge:
                    result = PBRTextureForge.GenerateEdgeFromNormal(_textures[Slot.Normal]!, _edgeSettings);
                    break;

                case Slot.AO:
                    result = PBRTextureForge.GenerateAO(
                        _textures[Slot.Normal]!,
                        _textures[Slot.Height],    // optional
                        _aoSettings);
                    break;

                case Slot.Metallic:
                    result = PBRTextureForge.GenerateMetallic(_textures[Slot.Diffuse]!, _metallicSettings);
                    break;

                case Slot.Smoothness:
                    result = PBRTextureForge.GenerateSmoothness(
                        _textures[Slot.Diffuse]!,
                        _textures[Slot.Metallic],  // optional
                        _smoothnessSettings);
                    break;
            }

            if (result != null)
            {
                // Replace whatever was in the slot with the freshly generated texture.
                _textures[slot]?.Dispose();
                _textures[slot] = result;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"PBR Forge: {slot} generation failed — {ex.Message}");
        }
    }

    private void SaveSlot(Slot slot)
    {
        var tex = _textures[slot];
        if (tex == null || Project.Current == null) return;

        // Derive sibling path: same folder as source, base name + "_{slot}.png".
        string folder;
        string baseName;
        if (_sourceRelPath != null)
        {
            folder = Path.GetDirectoryName(Path.Combine(Project.Current.AssetsPath, _sourceRelPath)) ?? Project.Current.AssetsPath;
            baseName = _sourceBaseName ?? "pbr";
        }
        else
        {
            folder = Project.Current.AssetsPath;
            baseName = "pbr";
        }

        string outPath = Path.Combine(folder, $"{baseName}_{slot.ToString().ToLowerInvariant()}.png");

        try
        {
            PBRTextureForge.SavePng(tex, outPath);
            EditorAssetDatabase.Instance?.ProcessFileChanges();
            Debug.Log($"PBR Forge: saved {outPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"PBR Forge: save failed — {ex.Message}");
        }
    }
}
