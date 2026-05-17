// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
namespace Prowl.Editor.Inspector;

/// <summary>
/// Draws material-property override rows shared between the Material asset inspector and
/// the shader-graph editor's Properties foldout. Extracted so both call sites render
/// identical-looking fields (purple override marker + revert button) without duplicating
/// the per-type branching logic. Values are read live overrides come from the material,
/// defaults from the shader so property changes in the graph propagate immediately to
/// every material without a re-import step.
/// </summary>
public static class MaterialPropertyDrawer
{
    /// <summary>Wrap a single shader property in a row with an override indicator on the
    /// left (purple bar when <c>material.IsOverridden(prop.Name)</c> is true) and a
    /// revert-to-default button on the right. <paramref name="onChanged"/> fires after
    /// any mutation so callers can flag dirty / refresh preview renders.</summary>
    public static void DrawPropertyRow(Paper paper, string id, Material material,
        ShaderProperty prop, Action? onChanged = null)
    {
        bool overridden = material.IsOverridden(prop.Name);

        using (paper.Row($"{id}_row")
            .Height(EditorTheme.RowHeight)
            .Margin(0, EditorTheme.Spacing)
            .Enter())
        {
            // Thin purple bar on the left edge always present (transparent when not
            // overridden) so fields line up regardless of state.
            paper.Box($"{id}_marker")
                .Width(3).Height(EditorTheme.RowHeight - 4)
                .Margin(2, 4, 2, 2)
                .BackgroundColor(overridden ? EditorTheme.Purple400 : System.Drawing.Color.Transparent)
                .Rounded(1.5f);

            using (paper.Box($"{id}_field").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
            {
                DrawProperty(paper, id, material, prop, onChanged);
            }

            if (overridden)
            {
                Origami.Button(paper, $"{id}_revert", EditorIcons.ArrowRotateLeft, () =>
                {
                    material.RevertProperty(prop.Name);
                    // Drop the stored value too otherwise it'd still get uploaded
                    // by ApplyMaterialUniformsWithDefaults even though the flag is gone.
                    material._properties.RemoveProperty(prop.Name);
                    onChanged?.Invoke();
                }).Width(24).Show();
            }
        }
    }

    /// <summary>Draw just the editor field for a property (no marker / revert chrome).
    /// Reads override from the material when set, otherwise the shader's current default.
    /// </summary>
    public static void DrawProperty(Paper paper, string id, Material material,
        ShaderProperty prop, Action? onChanged = null)
    {
        string label = !string.IsNullOrEmpty(prop.DisplayName) ? prop.DisplayName : prop.Name;
        var ps = material._properties;

        switch (prop.PropertyType)
        {
            case ShaderPropertyType.Float:
            {
                float val = ps.HasFloat(prop.Name) ? ps.GetFloat(prop.Name) : (float)prop.Value.X;
                // Range(min, max) Float -> slider, matching the shader author's intent.
                // Non-ranged Floats still draw as plain number fields.
                if (prop.HasRange)
                {
                    InspectorRow.Draw(paper, id, label, () =>
                        Origami.Slider(paper, $"{id}_v", val,
                            v => { material.SetFloat(prop.Name, v); onChanged?.Invoke(); },
                            (float)prop.Range.X, (float)prop.Range.Y).Format("F2").Show());
                }
                else
                {
                    InspectorRow.Draw(paper, id, label, () =>
                        Origami.NumericField<float>(paper, $"{id}_v", val,
                            v => { material.SetFloat(prop.Name, v); onChanged?.Invoke(); }).Show());
                }
                break;
            }
            case ShaderPropertyType.Int:
            {
                int val = ps.HasInt(prop.Name) ? ps.GetInt(prop.Name) : (int)prop.Value.X;
                InspectorRow.Draw(paper, id, label, () =>
                    Origami.NumericField<int>(paper, $"{id}_v", val,
                        v => { material.SetInt(prop.Name, v); onChanged?.Invoke(); }).Show());
                break;
            }
            case ShaderPropertyType.Color:
            {
                var val = ps.HasColor(prop.Name)
                    ? ps.GetColor(prop.Name)
                    : new Prowl.Vector.Color((float)prop.Value.X, (float)prop.Value.Y, (float)prop.Value.Z, (float)prop.Value.W);
                InspectorRow.Draw(paper, id, label, () =>
                    Origami.ColorField(paper, $"{id}_cf", val, v => { material.SetColor(prop.Name, new Prowl.Vector.Color(v.R, v.G, v.B, v.A)); onChanged?.Invoke(); }).Show());
                break;
            }
            case ShaderPropertyType.Vector2:
            {
                var val = ps.HasVector2(prop.Name) ? ps.GetVector2(prop.Name) : new Float2((float)prop.Value.X, (float)prop.Value.Y);
                InspectorRow.Draw(paper, id, label, () =>
                    Origami.Float2Field(paper, $"{id}_vf", val, v => { material.SetVector(prop.Name, v); onChanged?.Invoke(); }).Show());
                break;
            }
            case ShaderPropertyType.Vector3:
            {
                var val = ps.HasVector3(prop.Name) ? ps.GetVector3(prop.Name) : new Float3((float)prop.Value.X, (float)prop.Value.Y, (float)prop.Value.Z);
                InspectorRow.Draw(paper, id, label, () =>
                    Origami.Float3Field(paper, $"{id}_vf", val, v => { material.SetVector(prop.Name, v); onChanged?.Invoke(); }).Show());
                break;
            }
            case ShaderPropertyType.Vector4:
            {
                var val = ps.HasVector4(prop.Name) ? ps.GetVector4(prop.Name) : prop.Value;
                InspectorRow.Draw(paper, id, label, () =>
                    Origami.Float4Field(paper, $"{id}_vf", val, v => { material.SetVector(prop.Name, v); onChanged?.Invoke(); }).Show());
                break;
            }
            case ShaderPropertyType.Texture2D:
            {
                var val = ps.HasTexture(prop.Name) ? ps.GetTexture(prop.Name) : prop.Texture2DValue;
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture2D));
                PropertyGridUtils.DrawField(paper, id, label, typeof(Texture2D), val,
                    newVal => { material.SetTexture(prop.Name, newVal as Texture2D); onChanged?.Invoke(); }, 0);
                break;
            }
            case ShaderPropertyType.Texture3D:
            {
                var val = ps.HasTexture3D(prop.Name) ? ps.GetTexture3D(prop.Name) : prop.Texture3DValue;
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture3D));
                PropertyGridUtils.DrawField(paper, id, label, typeof(Texture3D), val,
                    newVal => { material.SetTexture3D(prop.Name, newVal as Texture3D); onChanged?.Invoke(); }, 0);
                break;
            }
        }
    }
}
