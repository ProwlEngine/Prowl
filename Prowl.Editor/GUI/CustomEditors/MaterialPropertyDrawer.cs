// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.Graphite.ShaderDef;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Graphite.Rendering.PropertyIDText;

using KeywordBinding = Prowl.Graphite.Rendering.KeywordBinding;
using Material = Prowl.Runtime.Resources.Material;
using MaterialProperty = Prowl.Graphite.Rendering.MaterialProperty;
using MaterialPropertyType = Prowl.Graphite.Rendering.MaterialPropertyType;
using PassType = Prowl.Graphite.Rendering.PassType;
using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Draws a material from its shader's metadata: the variant parent, the surface settings, every property shaped by its
/// attributes (Header, Space, Range, Enum, Toggle, ShowIf, HideInInspector), the keyword axes no property drives, and
/// the queue and shadow settings. Rows a material sets itself carry a purple marker and a revert button; the rest show
/// what the parent or the shader gives.
/// </summary>
public static class MaterialPropertyDrawer
{
    /// <summary>Draws the whole material. <paramref name="onChanged"/> runs after every edit.</summary>
    public static void DrawMaterial(Paper paper, string id, Material material, Action? onChanged = null)
    {
        Shader? shader = material.Shader;
        if (shader.IsNotValid()) return;

        DrawParent(paper, id, material, onChanged);
        DrawSurface(paper, id, material, shader, onChanged);

        Origami.Header(paper, $"{id}_h_props", "Properties").Underline().Show();
        foreach (MaterialProperty prop in shader.Properties)
            DrawPropertyRow(paper, $"{id}_p_{prop.Name.Text()}", material, prop, onChanged);

        DrawKeywords(paper, id, material, shader, onChanged);
        DrawAdvanced(paper, id, material, onChanged);
    }

    // ============================================================
    // Parent, surface, keywords, advanced
    // ============================================================

    private static void DrawParent(Paper paper, string id, Material material, Action? onChanged)
    {
        PropertyGridUtils.DrawField(paper, $"{id}_parent", "Variant Of", typeof(Material), material.Parent, value =>
        {
            Material? parent = value as Material;
            if (parent == material) return;
            try { material.Parent = parent; }
            catch (InvalidOperationException) { return; }
            onChanged?.Invoke();
        }, 0);
    }

    private static void DrawSurface(Paper paper, string id, Material material, Shader shader, Action? onChanged)
    {
        bool standard = HasAxis(shader, Prowl.Graphite.Rendering.SurfaceSettings.SurfaceAxis);
        bool overridden = material.IsSurfaceOverridden;
        SurfaceSettings? current = material.Surface;
        if (!standard && current == null && !shader.HasPass(PassType.Forward)) return;

        Origami.Header(paper, $"{id}_h_surface", "Surface").Underline().Show();
        if (!standard)
        {
            OverrideRow(paper, $"{id}_surface_use", overridden, () => { material.Surface = null; onChanged?.Invoke(); }, () =>
                EditorGUI.Row(paper, $"{id}_surface_use", "Override Render State", () =>
                    Origami.Checkbox(paper, $"{id}_surface_use_v", current != null, on =>
                    {
                        material.Surface = on ? new SurfaceSettings() : null;
                        onChanged?.Invoke();
                    }).Show()));
            if (current == null) return;
        }

        SurfaceSettings surface = current ?? new SurfaceSettings();
        void Set(SurfaceSettings value) { material.Surface = value; onChanged?.Invoke(); }
        void Revert() { material.Surface = null; onChanged?.Invoke(); }

        OverrideRow(paper, $"{id}_surface_type", overridden, Revert, () =>
            EditorGUI.Row(paper, $"{id}_surface_type", "Type", () =>
                Origami.EnumDropdown(paper, $"{id}_surface_type_v", surface.Type, v => Set(surface with { Type = v })).Show()));
        if (surface.Type == SurfaceType.Transparent)
        {
            OverrideRow(paper, $"{id}_surface_blend", overridden, Revert, () =>
                EditorGUI.Row(paper, $"{id}_surface_blend", "Blend", () =>
                    Origami.EnumDropdown(paper, $"{id}_surface_blend_v", surface.Blend, v => Set(surface with { Blend = v })).Show()));
        }
        OverrideRow(paper, $"{id}_surface_cull", overridden, Revert, () =>
            EditorGUI.Row(paper, $"{id}_surface_cull", "Cull", () =>
                Origami.EnumDropdown(paper, $"{id}_surface_cull_v", surface.Cull, v => Set(surface with { Cull = v })).Show()));
    }

    private static void DrawKeywords(Paper paper, string id, Material material, Shader shader, Action? onChanged)
    {
        List<VariantSpace> free = [];
        foreach (VariantSpace axis in shader.Keywords)
        {
            if (axis.Name == Prowl.Graphite.Rendering.SurfaceSettings.SurfaceAxis || IsDriven(shader, axis.Name)) continue;
            free.Add(axis);
        }
        if (free.Count == 0) return;

        Origami.Foldout(paper, $"{id}_keywords", "Keywords").Body(() =>
        {
            foreach (VariantSpace axis in free)
            {
                string name = axis.Name;
                string rowId = $"{id}_kw_{name}";
                string value = material.GetKeyword(name);
                if (value.Length == 0) value = axis.Values[0];
                OverrideRow(paper, rowId, material.IsKeywordOverridden(name), () => { material.RevertKeyword(name); onChanged?.Invoke(); }, () =>
                {
                    if (axis.IsToggle)
                    {
                        EditorGUI.Row(paper, rowId, Pretty(name), () =>
                            Origami.Checkbox(paper, $"{rowId}_v", value == VariantSpace.ToggleValues[1], on =>
                            {
                                material.SetKeyword(name, on);
                                onChanged?.Invoke();
                            }).Show());
                        return;
                    }
                    int index = Math.Max(0, IndexOf(axis.Values, value));
                    EditorGUI.Row(paper, rowId, Pretty(name), () =>
                        Origami.Dropdown(paper, $"{rowId}_v", index, i =>
                        {
                            material.SetKeyword(name, axis.Values[i]);
                            onChanged?.Invoke();
                        }, axis.Values).Show());
                });
            }
        });
    }

    private static void DrawAdvanced(Paper paper, string id, Material material, Action? onChanged)
    {
        Origami.Foldout(paper, $"{id}_advanced", "Advanced").Body(() =>
        {
            OverrideRow(paper, $"{id}_queue", material.IsRenderQueueOverridden, () => { material.RenderQueue = -1; onChanged?.Invoke(); }, () =>
                EditorGUI.Row(paper, $"{id}_queue", "Render Queue", () =>
                    Origami.NumericField<int>(paper, $"{id}_queue_v", material.RenderQueue, v =>
                    {
                        material.RenderQueue = Math.Max(0, v);
                        onChanged?.Invoke();
                    }).Show()));
            EditorGUI.Row(paper, $"{id}_shadows", "Cast Shadows", () =>
                Origami.Checkbox(paper, $"{id}_shadows_v", material.CastShadows, v =>
                {
                    material.CastShadows = v;
                    onChanged?.Invoke();
                }).Show());
        });
    }

    // ============================================================
    // Properties
    // ============================================================

    /// <summary>
    /// One property with its override marker and revert button, shaped by its attributes. Hidden properties and those
    /// whose ShowIf keyword is off draw nothing.
    /// </summary>
    public static void DrawPropertyRow(Paper paper, string id, Material material, MaterialProperty prop, Action? onChanged = null)
    {
        if (prop.HasAttribute("HideInInspector") || !ShowIf(material, prop)) return;

        if (TryGetArguments(prop, "Header", out string[] header) && header.Length > 0)
            Origami.Label(paper, $"{id}_header", Unquote(header[0])).Show();
        else if (prop.HasAttribute("Space"))
            paper.Box($"{id}_space").Height(EditorTheme.Spacing * 3);

        string name = prop.Name.Text();
        OverrideRow(paper, id, material.IsOverridden(name), () => { material.RevertProperty(name); onChanged?.Invoke(); },
            () => DrawProperty(paper, id, material, prop, onChanged));

        if (prop.Type == MaterialPropertyType.Texture2D && prop.HasAttribute("Map") && material.GetTexture(name).IsValid())
            DrawMapSettings(paper, id, material, prop, onChanged);
    }

    private static readonly string[] s_channels = ["R", "G", "B", "A"];
    private static readonly string[] s_uvSets = ["UV0", "UV1"];

    /// <summary>
    /// A set map's sampling, as Unity lays it out: tiling and offset, rotation in degrees, and the UV set, with the channel
    /// for maps of scalar inputs.
    /// </summary>
    private static void DrawMapSettings(Paper paper, string id, Material material, MaterialProperty prop, Action? onChanged)
    {
        string name = prop.Name.Text();
        void Revert(string setting) { material.RevertProperty(name + setting); onChanged?.Invoke(); }

        Float4 st = material.GetTextureScaleOffset(name);
        bool stSet = material.IsOverridden(name + "_ST");
        OverrideRow(paper, $"{id}_tiling", stSet, () => Revert("_ST"), () =>
            EditorGUI.Row(paper, $"{id}_tiling", "Tiling", () =>
                Origami.Float2Field(paper, $"{id}_tiling_v", new Float2(st.X, st.Y), v =>
                {
                    material.SetTextureScaleOffset(name, new Float4(v.X, v.Y, st.Z, st.W));
                    onChanged?.Invoke();
                }).Show()));
        OverrideRow(paper, $"{id}_offset", stSet, () => Revert("_ST"), () =>
            EditorGUI.Row(paper, $"{id}_offset", "Offset", () =>
                Origami.Float2Field(paper, $"{id}_offset_v", new Float2(st.Z, st.W), v =>
                {
                    material.SetTextureScaleOffset(name, new Float4(st.X, st.Y, v.X, v.Y));
                    onChanged?.Invoke();
                }).Show()));

        float degrees = material.GetTextureRotation(name) * (180f / MathF.PI);
        OverrideRow(paper, $"{id}_rotation", material.IsOverridden(name + "_Rotation"), () => Revert("_Rotation"), () =>
            EditorGUI.Row(paper, $"{id}_rotation", "Rotation", () =>
                Origami.NumericField<float>(paper, $"{id}_rotation_v", degrees, v =>
                {
                    material.SetTextureRotation(name, v * (MathF.PI / 180f));
                    onChanged?.Invoke();
                }).Show()));

        bool scalar = IsScalarMap(material.Shader!, name);
        bool samplingSet = material.IsOverridden(name + "_UVSet") || (scalar && material.IsOverridden(name + "_Channel"));
        OverrideRow(paper, $"{id}_sampling", samplingSet, () => { Revert("_UVSet"); if (scalar) Revert("_Channel"); }, () =>
            EditorGUI.Row(paper, $"{id}_sampling", scalar ? "Channel, UV" : "UV", () =>
            {
                using (paper.Row($"{id}_sampling_row").Height(EditorTheme.RowHeight).Gap(4).Enter())
                {
                    if (scalar)
                    {
                        Origami.Dropdown(paper, $"{id}_channel_v", Math.Clamp(material.GetTextureChannel(name), 0, 3), i =>
                        {
                            material.SetTextureChannel(name, i);
                            onChanged?.Invoke();
                        }, s_channels).Width(52).Show();
                    }
                    Origami.Dropdown(paper, $"{id}_uv_v", Math.Clamp(material.GetTextureUVSet(name), 0, 1), i =>
                    {
                        material.SetTextureUVSet(name, i);
                        onChanged?.Invoke();
                    }, s_uvSets).Width(64).Show();
                }
            }));
    }

    // A map of a number input reads one channel: X_map of a Float or Int X, and the height map
    private static bool IsScalarMap(Shader shader, string map)
    {
        if (map == "geometry_height_map") return true;
        if (!map.EndsWith("_map", StringComparison.Ordinal)) return false;
        MaterialProperty? input = shader.FindProperty(map[..^4]);
        return input is { Type: MaterialPropertyType.Float or MaterialPropertyType.Int };
    }

    /// <summary>The range a property's Range(min, max) attribute gives, if it has one.</summary>
    public static bool TryGetRange(MaterialProperty prop, out float min, out float max)
    {
        min = max = 0;
        return TryGetArguments(prop, "Range", out string[] args) && args.Length == 2
            && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min)
            && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max);
    }

    /// <summary>Draws just the field of a property, with the value the material draws with.</summary>
    public static void DrawProperty(Paper paper, string id, Material material, MaterialProperty prop, Action? onChanged = null)
    {
        string name = prop.Name.Text();
        string label = !string.IsNullOrEmpty(prop.DisplayName) ? prop.DisplayName : name;

        switch (prop.Type)
        {
            case MaterialPropertyType.Float:
            case MaterialPropertyType.Int:
                DrawNumber(paper, id, material, prop, name, label, onChanged);
                break;
            case MaterialPropertyType.Color:
                {
                    Color val = material.GetColor(name);
                    EditorGUI.Row(paper, id, label, () =>
                        Origami.ColorField(paper, $"{id}_cf", val, v => { material.SetColor(name, new Color(v.R, v.G, v.B, v.A)); onChanged?.Invoke(); }).Show());
                    break;
                }
            case MaterialPropertyType.Float2:
                {
                    Float4 v4 = material.GetVector(name);
                    EditorGUI.Row(paper, id, label, () =>
                        Origami.Float2Field(paper, $"{id}_vf", new Float2(v4.X, v4.Y), v => { material.SetVector(name, v); onChanged?.Invoke(); }).Show());
                    break;
                }
            case MaterialPropertyType.Float3:
                {
                    Float4 v4 = material.GetVector(name);
                    EditorGUI.Row(paper, id, label, () =>
                        Origami.Float3Field(paper, $"{id}_vf", new Float3(v4.X, v4.Y, v4.Z), v => { material.SetVector(name, v); onChanged?.Invoke(); }).Show());
                    break;
                }
            case MaterialPropertyType.Float4:
                {
                    Float4 val = material.GetVector(name);
                    EditorGUI.Row(paper, id, label, () =>
                        Origami.Float4Field(paper, $"{id}_vf", val, v => { material.SetVector(name, v); onChanged?.Invoke(); }).Show());
                    break;
                }
            case MaterialPropertyType.Texture2D:
                PropertyGridUtils.DrawField(paper, id, label, typeof(Texture2D), material.GetTexture(name),
                    v => { material.SetTexture(name, v as Texture2D); onChanged?.Invoke(); }, 0);
                break;
            case MaterialPropertyType.Texture3D:
                PropertyGridUtils.DrawField(paper, id, label, typeof(Texture3D), material.GetTexture3D(name),
                    v => { material.SetTexture3D(name, v as Texture3D); onChanged?.Invoke(); }, 0);
                break;
            case MaterialPropertyType.TextureCube:
                PropertyGridUtils.DrawField(paper, id, label, typeof(Cubemap), material.GetTextureCube(name),
                    v => { material.SetTextureCube(name, v as Cubemap); onChanged?.Invoke(); }, 0);
                break;
        }
    }

    private static void DrawNumber(Paper paper, string id, Material material, MaterialProperty prop, string name, string label, Action? onChanged)
    {
        bool isInt = prop.Type == MaterialPropertyType.Int;
        void Set(float v)
        {
            if (isInt) material.SetInt(name, (int)MathF.Round(v));
            else material.SetFloat(name, v);
            onChanged?.Invoke();
        }

        if (TryGetArguments(prop, "Enum", out string[] names) && names.Length > 0)
        {
            int index = Math.Clamp(material.GetInt(name), 0, names.Length - 1);
            EditorGUI.Row(paper, id, label, () => Origami.Dropdown(paper, $"{id}_v", index, i => Set(i), names).Show());
            return;
        }

        if (TryGetArguments(prop, "KeywordEnum", out string[] axisArgs) && axisArgs.Length == 1 && FindAxis(material.Shader!, axisArgs[0]) is { } axis)
        {
            int index = Math.Clamp(material.GetInt(name), 0, axis.Values.Count - 1);
            EditorGUI.Row(paper, id, label, () => Origami.Dropdown(paper, $"{id}_v", index, i => Set(i), axis.Values).Show());
            return;
        }

        // A toggle that is only a switch: an integer, or a float without a range
        if (prop.HasAttribute("Toggle") && (isInt || !TryGetRange(prop, out _, out _)))
        {
            bool on = material.GetFloat(name) != 0;
            EditorGUI.Row(paper, id, label, () => Origami.Checkbox(paper, $"{id}_v", on, v => Set(v ? 1 : 0)).Show());
            return;
        }

        if (TryGetRange(prop, out float min, out float max))
        {
            float val = material.GetFloat(name);
            EditorGUI.Row(paper, id, label, () =>
                Origami.Slider(paper, $"{id}_v", val, Set, min, max).Format(isInt ? "F0" : "F2").Show());
            return;
        }

        if (isInt)
            EditorGUI.Row(paper, id, label, () => Origami.NumericField<int>(paper, $"{id}_v", material.GetInt(name), v => Set(v)).Show());
        else
            EditorGUI.Row(paper, id, label, () => Origami.NumericField<float>(paper, $"{id}_v", material.GetFloat(name), Set).Show());
    }

    // ============================================================
    // Helpers
    // ============================================================

    // A row with the override marker on the left and, when overridden, a revert button on the right
    private static void OverrideRow(Paper paper, string id, bool overridden, Action revert, Action draw)
    {
        using (paper.Row($"{id}_row")
            .Height(UnitValue.Auto).MinHeight(EditorTheme.RowHeight)
            .Margin(0, EditorTheme.Spacing)
            .Enter())
        {
            paper.Box($"{id}_marker")
                .Width(3).Height(EditorTheme.RowHeight - 4)
                .Margin(2, 4, 2, 2)
                .BackgroundColor(overridden ? EditorTheme.Purple400 : System.Drawing.Color.Transparent)
                .Rounded(1.5f);

            using (paper.Box($"{id}_field").Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(EditorTheme.RowHeight).Enter())
                draw();

            if (overridden)
            {
                Origami.IconButton(paper, $"{id}_revert", EditorIcons.ArrowRotateLeft_I, revert)
                    .Small().Width(24).Height(EditorTheme.RowHeight).Soft().Show();
            }
        }
    }

    // ShowIf(AXIS) shows the property while the toggle is on, ShowIf(AXIS, VALUE) while the axis has that value
    private static bool ShowIf(Material material, MaterialProperty prop)
    {
        if (!TryGetArguments(prop, "ShowIf", out string[] args) || args.Length == 0) return true;
        string value = material.GetKeyword(args[0]);
        if (args.Length == 1) return value == VariantSpace.ToggleValues[1];
        if (value.Length == 0 && material.Shader is { } shader && FindAxis(shader, args[0]) is { } axis) value = axis.Values[0];
        return string.Equals(value, args[1], StringComparison.Ordinal);
    }

    private static bool TryGetArguments(MaterialProperty prop, string attribute, out string[] args)
    {
        foreach (string text in prop.Attributes)
        {
            if (!text.StartsWith(attribute, StringComparison.Ordinal)) continue;
            if (text.Length == attribute.Length) { args = []; return true; }
            if (text[attribute.Length] != '(' || !text.EndsWith(')')) continue;
            args = text[(attribute.Length + 1)..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return true;
        }
        args = [];
        return false;
    }

    private static string Unquote(string text) => text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static bool HasAxis(Shader shader, string name) => FindAxis(shader, name) != null;

    private static VariantSpace? FindAxis(Shader shader, string name)
    {
        foreach (VariantSpace axis in shader.Keywords)
            if (axis.Name == name) return axis;
        return null;
    }

    // Whether a property drives the axis, so the property row is the control for it
    private static bool IsDriven(Shader shader, string axis)
    {
        if (shader.GpuShader is not { } gpu) return false;
        foreach (KeywordBinding binding in gpu.KeywordBindings)
            if (binding.Axis == axis) return true;
        return false;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i] == value) return i;
        return -1;
    }

    // _PARALLAXMAP to Parallax Map
    private static string Pretty(string axis)
    {
        string trimmed = axis.TrimStart('_');
        if (trimmed.Length == 0) return axis;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.Replace('_', ' ').ToLowerInvariant());
    }
}
