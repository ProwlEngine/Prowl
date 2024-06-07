using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering.OpenGL;

namespace Prowl.Editor.ScriptedEditors
{
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
            var mat = (Material)target;
            mat ??= new Material();

            var g = Gui.ActiveGUI;

            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.ScaleChildren();

            bool changed = false;
            using (g.Node("Shader").ExpandWidth().MaxHeight(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                // Label
                using (g.Node("#_Label").ExpandHeight().Clip().Enter())
                {
                    var pos = g.CurrentNode.LayoutData.Rect.Min;
                    pos.x += 28;
                    pos.y += 5;
                    g.Draw2D.DrawText("Shader", pos, GuiStyle.Base8);
                }

                // Value
                IAssetRef assetref = mat.Shader;
                using (g.Node("#_Value").ExpandHeight().Enter())
                {
                    changed |= EditorGUI.Property_Asset("Rotation", ref assetref);
                }
                mat.Shader = new(assetref.AssetID, assetref.FileID);
            }

            if (mat.Shader.IsAvailable)
            {
                using (g.Node("Properties").ExpandWidth().Layout(LayoutType.Column).Enter())
                {
                    int id = 0;
                    foreach (var property in mat.Shader.Res.Properties)
                    {
                        using (g.Node("prop", id++).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
                        {
                            // Label
                            using (g.Node("#_Label").ExpandHeight().Clip().Enter())
                            {
                                var pos = g.CurrentNode.LayoutData.Rect.Min;
                                pos.x += 28;
                                pos.y += 5;
                                g.Draw2D.DrawText(property.DisplayName, pos, GuiStyle.Base8);
                            }

                            using (g.Node("#_Value").ExpandHeight().Enter())
                            {
                                switch (property.Type)
                                {
                                    case Shader.Property.PropertyType.FLOAT:
                                        // Value
                                        float f = mat.PropertyBlock.GetFloat(property.Name);
                                        changed |= EditorGUI.Property_Float(property.DisplayName, ref f);
                                        if (changed) mat.PropertyBlock.SetFloat(property.Name, f);

                                        break;
                                    case Shader.Property.PropertyType.INTEGER:
                                        int i = mat.PropertyBlock.GetInt(property.Name);
                                        changed |= EditorGUI.PropertyIntegar(property.DisplayName, ref i);
                                        if (changed) mat.PropertyBlock.SetInt(property.Name, i);
                                        break;
                                    case Shader.Property.PropertyType.VEC2:
                                        Vector2 v2 = mat.PropertyBlock.GetVector2(property.Name).ToFloat();
                                        changed |= EditorGUI.Property_Vector2(property.DisplayName, ref v2);
                                        if (changed) mat.PropertyBlock.SetVector(property.Name, v2);
                                        break;
                                    case Shader.Property.PropertyType.VEC3:
                                        Vector3 v3 = mat.PropertyBlock.GetVector3(property.Name).ToFloat();
                                        changed |= EditorGUI.Property_Vector3(property.DisplayName, ref v3);
                                        if (changed) mat.PropertyBlock.SetVector(property.Name, v3);
                                        break;
                                    case Shader.Property.PropertyType.VEC4:
                                        Vector4 v4 = mat.PropertyBlock.GetVector4(property.Name).ToFloat();
                                        changed |= EditorGUI.Property_Vector4(property.DisplayName, ref v4);
                                        if (changed) mat.PropertyBlock.SetVector(property.Name, v4);
                                        break;
                                    case Shader.Property.PropertyType.COLOR:
                                        Color c = mat.PropertyBlock.GetVector4(property.Name).ToFloat();
                                        changed |= EditorGUI.Property_Color(property.DisplayName, ref c);
                                        if (changed) mat.PropertyBlock.SetVector(property.Name, c);
                                        break;

                                    case Shader.Property.PropertyType.TEXTURE2D:
                                        var texNullable = mat.PropertyBlock.GetTexture(property.Name);
                                        if (texNullable == null) break;
                                        var tex = texNullable.Value;

                                        using (g.Node("_Tex").ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
                                        {
                                            ulong assetDrawerID = g.CurrentNode.ID;
                                            if (guidAssignedToID != 0 && guidAssignedToID == assetDrawerID)
                                            {
                                                tex.AssetID = assignedGUID;
                                                tex.FileID = assignedFileID;
                                                assignedGUID = Guid.Empty;
                                                assignedFileID = 0;
                                                guidAssignedToID = 0;
                                                changed = true;
                                            }

                                            g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, GuiStyle.Borders, 1, 2);

                                            bool p = false;
                                            bool h = false;
                                            using (g.ButtonNode("Selector", out p, out h).MaxWidth(GuiStyle.ItemHeight).ExpandHeight().Enter())
                                            {
                                                var pos = g.CurrentNode.LayoutData.GlobalContentPosition;
                                                pos += new Vector2(8, 8);
                                                g.Draw2D.DrawText(FontAwesome6.MagnifyingGlass, pos, GuiStyle.Base11 * (h ? 1f : 0.8f));
                                                if (p)
                                                {
                                                    Selected = assetDrawerID;
                                                    new AssetSelectorWindow(tex.InstanceType, (guid, fileid) => { assignedGUID = guid; guidAssignedToID = assetDrawerID; assignedFileID = fileid; });
                                                }
                                            }

                                            using (g.ButtonNode("Asset", out p, out h).ExpandHeight().Clip().Enter())
                                            {
                                                var pos = g.CurrentNode.LayoutData.GlobalContentPosition;
                                                pos += new Vector2(0, 8);
                                                if (tex.IsExplicitNull || tex.IsRuntimeResource)
                                                {
                                                    string text = tex.IsExplicitNull ? "(Null)" : "(Runtime)" + tex.Name;
                                                    var col = GuiStyle.Base11 * (h ? 1f : 0.8f);
                                                    if (tex.IsExplicitNull)
                                                        col = GuiStyle.Red * (h ? 1f : 0.8f);
                                                    g.Draw2D.DrawText(text, pos, col);
                                                    if (p)
                                                        Selected = assetDrawerID;
                                                }
                                                else if (AssetDatabase.TryGetFile(tex.AssetID, out var assetPath))
                                                {
                                                    g.Draw2D.DrawText(AssetDatabase.ToRelativePath(assetPath), pos, GuiStyle.Base11 * (h ? 1f : 0.8f));
                                                    if (p)
                                                    {
                                                        Selected = assetDrawerID;
                                                        AssetDatabase.Ping(tex.AssetID);
                                                    }
                                                }

                                                if (h && g.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                                                    GlobalSelectHandler.Select(tex);


                                                // Drag and drop support
                                                if (DragnDrop.Drop(out var instance, tex.InstanceType))
                                                {
                                                    // SetInstance() will also set the AssetID if the instance is an asset
                                                    tex.SetInstance(instance);
                                                    changed = true;
                                                }

                                                if (Selected == assetDrawerID && g.IsKeyDown(Silk.NET.Input.Key.Delete))
                                                {
                                                    tex.AssetID = Guid.Empty;
                                                    tex.FileID = 0;
                                                    changed = true;
                                                }
                                            }

                                            mat.PropertyBlock.SetTexture(property.Name, tex);
                                        }


                                        break;

                                }
                            }
                        }
                    }
                }
            }

            if (changed)
            {
                onChange?.Invoke();
            }

        }

    }
}
