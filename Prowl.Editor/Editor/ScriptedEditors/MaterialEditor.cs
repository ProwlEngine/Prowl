// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

using TerraFX.Interop.Vulkan;

namespace Prowl.Editor.ScriptedEditors;

[CustomEditor(typeof(Material))]
public class MaterialEditor : ScriptedEditor
{
    public Action onChange;

    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        var mat = (Material)target;
        mat ??= Material.CreateDefaultMaterial();

        gui.CurrentNode.Layout(LayoutType.Column);
        gui.CurrentNode.ScaleChildren();

        bool changed = false;
        using (gui.Node("Shader").ExpandWidth().MaxHeight(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
        {
            AssetRef<Shader> assetref = mat.Shader;
            changed |= EditorGUI.DrawProperty(0, "Shader", ref assetref);
            if (changed) changes.Add(mat, nameof(Material.Shader));
            mat.Shader = assetref;
        }

        if (mat.Shader.IsAvailable)
        {
            using (gui.Node("Properties").ExpandWidth().Layout(LayoutType.Column).Enter())
            {
                int id = 1;
                foreach (ShaderProperty property in mat.Shader.Res.Properties)
                {
                    using (gui.Node("prop", id++).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        changed |= DrawProperty(property, mat, changes);
                    }
                }
            }
        }

        if (changed)
        {
            onChange?.Invoke();
        }
    }


    private bool DrawProperty(ShaderProperty property, Material mat, EditorGUI.FieldChanges changes)
    {
        bool changed = false;


        if (!mat.GetProperty(property.Name, out ShaderProperty value))
            return false;

        switch (property.PropertyType)
        {
            case ShaderPropertyType.Float:
                float f = (float)value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref f);
                value = f;

                break;

            case ShaderPropertyType.Vector2:
                Vector2 v2 = value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref v2);
                value = v2;

                break;

            case ShaderPropertyType.Vector3:
                Vector3 v3 = value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref v3);
                value = v3;

                break;

            case ShaderPropertyType.Vector4:
                Vector4 v4 = value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref v4);
                value = v4;

                break;

            case ShaderPropertyType.Color:
                Color color = value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref color);
                value = color;

                break;

            case ShaderPropertyType.Texture2D:
                AssetRef<Texture2D> tex2D = value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref tex2D);
                value = tex2D;

                break;

            case ShaderPropertyType.Texture3D:
                AssetRef<Texture3D> tex3D = value;
                changed = EditorGUI.DrawProperty(0, property.DisplayName, ref tex3D);
                value = tex3D;

                break;
        }

        if (changed)
        {
            mat.SetProperty(property.Name, value);
            changes.Add(mat, "_propertyLookup");
        }

        return changed;
    }
}
