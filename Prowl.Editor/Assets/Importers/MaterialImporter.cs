using HexaEngine.ImGuiNET;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Editor.ImGUI.Widgets;
using Prowl.Runtime.Utils;
using System.Numerics;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(Material), ".mat")]
    public class MaterialImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Load the Texture into a TextureData Object and serialize to Asset Folder
            Material? mat;
            try
            {
                string json = File.ReadAllText(assetPath.FullName);
                var tag = StringTagConverter.Read(json);
                mat = TagSerializer.Deserialize<Material>(tag);
            }
            catch
            {
                // something went wrong, lets just create a new material and save it
                mat = new Material();
                string json = StringTagConverter.Write((CompoundTag)TagSerializer.Serialize(mat));
                File.WriteAllText(json, assetPath.FullName);
            }

            ctx.SetMainObject(mat);

            ImGuiNotify.InsertNotification("Material Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

    [CustomEditor(typeof(MaterialImporter))]
    public class MaterialEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (MaterialImporter)(target as MetaFile).importer;

            try
            {
                var tag = StringTagConverter.ReadFromFile((target as MetaFile).AssetPath);
                Material mat = TagSerializer.Deserialize<Material>(tag);

                PropertyDrawer.Draw(mat, typeof(Material).GetField("Shader")!);
                bool changed = false;
                if (mat.Shader.IsAvailable)
                {
                    int id = 0;
                    foreach (var property in mat.Shader.Res.Properties)
                    {
                        ImGui.PushID(id++);
                        switch (property.Type)
                        {
                            case Shader.Property.PropertyType.FLOAT:
                                float f = mat.PropertyBlock.GetFloat(property.Name);
                                changed |= ImGui.DragFloat(property.DisplayName, ref f, 0.01f);
                                if (changed) mat.PropertyBlock.SetFloat(property.Name, f);
                                break;
                            case Shader.Property.PropertyType.INTEGER:
                                int i = mat.PropertyBlock.GetInt(property.Name);
                                changed |= ImGui.DragInt(property.DisplayName, ref i, 1);
                                if (changed) mat.PropertyBlock.SetInt(property.Name, i);
                                break;
                            case Shader.Property.PropertyType.VEC2:
                                var v2 = mat.PropertyBlock.GetVector2(property.Name).ToFloat();
                                changed |= ImGui.DragFloat2(property.DisplayName, ref v2, 0.01f);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, v2);
                                break;
                            case Shader.Property.PropertyType.VEC3:
                                var v3 = mat.PropertyBlock.GetVector3(property.Name).ToFloat();
                                changed |= ImGui.DragFloat3(property.DisplayName, ref v3, 0.01f);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, v3);
                                break;
                            case Shader.Property.PropertyType.VEC4:
                                var v4 = mat.PropertyBlock.GetVector4(property.Name).ToFloat();
                                changed |= ImGui.DragFloat4(property.DisplayName, ref v4, 0.01f);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, v4);
                                break;
                            case Shader.Property.PropertyType.COLOR:
                                var c = mat.PropertyBlock.GetVector4(property.Name).ToFloat();
                                changed |= ImGui.ColorEdit4(property.DisplayName, ref c);
                                if (changed) mat.PropertyBlock.SetVector(property.Name, c);
                                break;

                            case Shader.Property.PropertyType.TEXTURE2D:
                                var texNullable = mat.PropertyBlock.GetTexture(property.Name);
                                if (texNullable == null) break;
                                var tex = texNullable.Value;

                                ImDrawListPtr drawList = ImGui.GetWindowDrawList();

                                ImGui.Columns(2);
                                ImGui.Text(property.DisplayName);
                                ImGui.SetColumnWidth(0, 70);
                                ImGui.NextColumn();

                                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
                                ImGui.PushID(property.Name);

                                var pos = ImGui.GetCursorPos();
                                string path;

                                if (tex.IsExplicitNull)
                                {
                                    path = "(Null)";
                                    drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new System.Numerics.Vector4(0.9f, 0.1f, 0.1f, 0.3f)));
                                    if (ImGui.Selectable("##" + path, false, new System.Numerics.Vector2(50, 50)))
                                        AssetDatabase.Ping(tex.AssetID);
                                    GUIHelper.Tooltip(path);
                                }
                                else if (tex.IsRuntimeResource)
                                {
                                    path = "(Runtime)" + tex.Name;
                                    drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.9f, 0.3f)));
                                    if (ImGui.Selectable("##" + path, false, new System.Numerics.Vector2(50, 50)))
                                        AssetDatabase.Ping(tex.AssetID);
                                        GUIHelper.Tooltip(path);
                                    }
                                else if (AssetDatabase.Contains(tex.AssetID))
                                {
                                    path = AssetDatabase.GUIDToAssetPath(tex.AssetID);
                                    var thumbnail = Application.AssetProvider.LoadAsset<Texture2D>(tex);
                                    var cPos = ImGui.GetCursorScreenPos();
                                    ImGui.SetCursorScreenPos(new System.Numerics.Vector2(cPos.X, cPos.Y + 50));
                                    ImGui.Image(new ImTextureID((nint)thumbnail.Handle), new System.Numerics.Vector2(50, -50));
                                    ImGui.SetCursorScreenPos(cPos);
                                    if (ImGui.Selectable("##" + path, false, new System.Numerics.Vector2(50, 50)))
                                        AssetDatabase.Ping(tex.AssetID);
                                    GUIHelper.Tooltip(path);
                                }

                                // DragDrop code
                                if(DragnDrop.ReceiveAsset<Texture2D>(out var droppedTex))
                                {
                                    tex.AssetID = droppedTex.AssetID;
                                    changed = true;
                                }

                                ImGui.PopID();
                                ImGui.PopItemWidth();
                                ImGui.Columns(1);

                                mat.PropertyBlock.SetTexture(property.Name, tex);

                                break;

                        }
                        ImGui.PopID();
                    }
                }

                if (changed)
                {
                    StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(mat), (target as MetaFile).AssetPath);
                    AssetDatabase.Reimport(AssetDatabase.FileToRelative((target as MetaFile).AssetPath));
                }
            }
            catch
            {
                ImGui.LabelText("Failed to Deserialize Material", "The material file is invalid.");
            }
        }
    }

}
