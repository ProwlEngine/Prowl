// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Assets;
using Prowl.Runtime;

namespace Prowl.Editor.ScriptedEditors;

[CustomEditor(typeof(Material))]
public class MaterialEditor : ScriptedEditor
{
    private Action onChange;
    public static Guid assignedGUID;
    public static ushort assignedFileID;
    public static ulong guidAssignedToID = 0;
    public static ulong Selected = 0;

    public MaterialEditor() { }

    public MaterialEditor(Material mat, Action onChange)
    {
        target = mat;
        this.onChange = onChange;
    }

    public override void OnInspectorGUI()
    {
        /*
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        var mat = (Material)target;
        mat ??= new Material();

        g.CurrentNode.Layout(LayoutType.Column);
        g.CurrentNode.ScaleChildren();

        bool changed = false;
        using (g.Node("Shader").ExpandWidth().MaxHeight(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
        {
            IAssetRef assetref = mat.Shader;
            changed |= EditorGUI.DrawProperty(0, "Shader", ref assetref);
            mat.Shader = new(assetref.AssetID, assetref.FileID);
        }

        using (g.Node("Properties").ExpandWidth().MaxHeight(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
        {
            EditorGUI.Text("Under construction");
        }

        if (mat.Shader.IsAvailable)
        {
            using (g.Node("Properties").ExpandWidth().Layout(LayoutType.Column).Enter())
            {
                int id = 1;
                foreach (var property in mat.Shader.Res.Properties)
                {
                    using (g.Node("prop", id++).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        int index = 0;
                        switch (property.Type)
                        {
                            case Shader.Property.PropertyType.FLOAT:
                                // Value
                                float f = mat.PropertyBlock.GetFloat(property.Name);
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref f);
                                if (changed) mat.PropertyBlock.SetFloat(property.Name, f);

                                break;
                            case Shader.Property.PropertyType.INTEGER:
                                int i = mat.PropertyBlock.GetInt(property.Name);
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref i);
                                if (changed) mat.PropertyBlock.SetInt(property.Name, i);
                                break;
                            case Shader.Property.PropertyType.VEC2:
                                Vector2 v2 = mat.PropertyBlock.GetVector2(property.Name).ToFloat();
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref v2);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, v2);
                                break;
                            case Shader.Property.PropertyType.VEC3:
                                Vector3 v3 = mat.PropertyBlock.GetVector3(property.Name).ToFloat();
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref v3);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, v3);
                                break;
                            case Shader.Property.PropertyType.VEC4:
                                Vector4 v4 = mat.PropertyBlock.GetVector4(property.Name).ToFloat();
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref v4);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, v4);
                                break;
                            case Shader.Property.PropertyType.COLOR:
                                Color c = mat.PropertyBlock.GetVector4(property.Name).ToFloat();
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref c);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, c);
                                break;

                            case Shader.Property.PropertyType.TEXTURE2D:

                                AssetRef<Texture2D> tex2D = mat.PropertyBlock.GetTexture(property.Name);
                                changed |= EditorGUI.DrawProperty(index++, property.DisplayName, ref tex2D);
                                if (changed) mat.PropertyBlock.SetTexture(property.Name, tex2D);
                                break;
                        }
                    }
                }
            }
        }

        if (changed)
        {
            onChange?.Invoke();
        }
        */
    }

}
