using ImageMagick;
using Prowl.Editor.Assets;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.Utils;
using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using static Prowl.Editor.EditorGUI;
using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.EditorWindows.CustomEditors
{
    /// <summary>
    /// GameObject Custom Editor for the Inspector Window
    /// </summary>
    [CustomEditor(typeof(GameObject))]
    public class GameObjectEditor : ScriptedEditor
    {
        private string _searchText = string.Empty;
        private static MenuItemInfo rootMenuItem;
        private Dictionary<int, ScriptedEditor> compEditors = new();

        [OnAssemblyUnload]
        public static void ClearCache() => rootMenuItem = null;

        public override void OnDisable()
        {
            foreach (var editor in compEditors.Values)
                editor.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            var go = target as GameObject;
            if (go.hideFlags.HasFlag(HideFlags.NotEditable))
                g.Draw2D.DrawText("This GameObject is not editable", g.CurrentNode.LayoutData.InnerRect);

            bool isEnabled = go.enabled;
            if(g.Checkbox("IsEnabledChk", ref isEnabled, 0, 0, out _))
                go.enabled = isEnabled;
            g.Tooltip("Is Enabled");

            GuiStyle style = new();
            style.WidgetColor = GuiStyle.WindowBackground;
            style.Border = GuiStyle.Borders;
            style.WidgetRoundness = 8f;
            style.BorderThickness = 1f;
            string name = go.Name;
            if (g.InputField("NameInput", ref name, 32, InputFieldFlags.None, GuiStyle.ItemHeight, 0, Size.Percentage(1f, -(GuiStyle.ItemHeight * 3)), GuiStyle.ItemHeight, style))
                go.Name = go.Name.Trim();

            var invisStyle = new GuiStyle { WidgetColor = new Color(0, 0, 0, 0), Border = new Color(0, 0, 0, 0) };
            g.Combo("#_TagID", "#_TagPopupID", ref go.tagIndex, TagLayerManager.Instance.tags.ToArray(), Offset.Percentage(1f, -(GuiStyle.ItemHeight * 2)), 0, GuiStyle.ItemHeight, GuiStyle.ItemHeight, invisStyle, FontAwesome6.Tag);
            g.Combo("#_LayerID", "#_LayerPopupID", ref go.layerIndex, TagLayerManager.Instance.layers.ToArray(), Offset.Percentage(1f, -(GuiStyle.ItemHeight)), 0, GuiStyle.ItemHeight, GuiStyle.ItemHeight, invisStyle, FontAwesome6.LayerGroup);

            if (go.IsPrefab)
            {
                // Show buttons to Ping Prefab Asset, Revert Prefab, and Apply Prefab
                using (g.Node("#_PrefabBtns").ExpandWidth().Height(GuiStyle.ItemHeight).Top((GuiStyle.ItemHeight + 5)).Layout(LayoutType.Row).ScaleChildren().Enter())
                {
                    bool pressed, hovered;
                    using (g.ButtonNode("#_SelectBtn", out pressed, out hovered).ExpandHeight().Margin(0, 4).Enter())
                    {
                        if (pressed)
                        {
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10, 9);
                            AssetDatabase.Ping(go.AssetID);
                        }
                        else if (hovered)
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f, 10, 9);
                        else
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Blue, 10, 9);
                        g.Draw2D.DrawText("Select", g.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);
                    }

                    using (g.ButtonNode("#_RevertBtn", out pressed, out hovered).ExpandHeight().Margin(0, 4).Enter())
                    {
                        if (pressed)
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                        else if (hovered)
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f);
                        else
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Red);
                        g.Draw2D.DrawText("Revert", g.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);
                    }

                    using (g.ButtonNode("#_ApplyBtn", out pressed, out hovered).ExpandHeight().Margin(0, 4).Enter())
                    {
                        if (pressed)
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10, 6);
                        else if (hovered)
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f, 10, 6);
                        else
                            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Green, 10, 6);
                        g.Draw2D.DrawText("Apply", g.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);
                    }
                }
            }
            
            var height = (GuiStyle.ItemHeight + 5) * (go.IsPrefab ? 2 : 1);
            using (g.Node("#_InspContent").Top(height).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {

                // Transform
                // Header
                bool opened;
                using (g.OpenCloseNode("#_TransformH", out opened, true).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                {
                    g.Draw2D.DrawText((opened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight), g.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, 8));
                    g.Draw2D.DrawText("Transform", 23, g.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, 7));

                    DragnDrop.Drag(go.Transform, typeof(Transform));
                }

                // Content
                if (opened)
                {
                    using (g.Node("#_TansformC_").ExpandWidth().Layout(LayoutType.Column).FitContentHeight().Enter())
                    {
                        var t = go.Transform;
                        using (ActiveGUI.Node("PosParent", 0).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
                        {
                            // Label
                            using (ActiveGUI.Node("#_Label").ExpandHeight().Clip().Enter())
                            {
                                var pos = ActiveGUI.CurrentNode.LayoutData.Rect.Min;
                                pos.x += 28;
                                pos.y += 5;
                                ActiveGUI.Draw2D.DrawText("Position", pos, GuiStyle.Base8);
                            }

                            // Value
                            var tpos = t.localPosition;
                            using (ActiveGUI.Node("#_Value").ExpandHeight().Enter())
                                Property_Vector3("Position", ref tpos);
                            t.localPosition = tpos;
                        }

                        using (ActiveGUI.Node("RotParent", 0).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
                        {
                            // Label
                            using (ActiveGUI.Node("#_Label").ExpandHeight().Clip().Enter())
                            {
                                var pos = ActiveGUI.CurrentNode.LayoutData.Rect.Min;
                                pos.x += 28;
                                pos.y += 5;
                                ActiveGUI.Draw2D.DrawText("Rotation", pos, GuiStyle.Base8);
                            }

                            // Value
                            var tpos = t.localEulerAngles;
                            using (ActiveGUI.Node("#_Value").ExpandHeight().Enter())
                                Property_Vector3("Rotation", ref tpos);
                            t.localEulerAngles = tpos;
                        }

                        using (ActiveGUI.Node("ScaleParent", 0).ExpandWidth().Height(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
                        {
                            // Label
                            using (ActiveGUI.Node("#_Label").ExpandHeight().Clip().Enter())
                            {
                                var pos = ActiveGUI.CurrentNode.LayoutData.Rect.Min;
                                pos.x += 28;
                                pos.y += 5;
                                ActiveGUI.Draw2D.DrawText("Scale", pos, GuiStyle.Base8);
                            }

                            // Value
                            var tpos = t.localScale;
                            using (ActiveGUI.Node("#_Value").ExpandHeight().Enter())
                                Property_Vector3("Scale", ref tpos);
                            t.localScale = tpos;
                        }
                    }
                }


                // Draw Components
                HashSet<int> editorsNeeded = [];
                List<MonoBehaviour> toDelete = [];
                foreach (var comp in go.GetComponents<MonoBehaviour>()) {
                    if (comp == null) continue;
                    editorsNeeded.Add(comp.InstanceID);
                
                    if (comp.hideFlags.HasFlag(HideFlags.Hide) || comp.hideFlags.HasFlag(HideFlags.HideAndDontSave) || comp.hideFlags.HasFlag(HideFlags.NotEditable))
                        continue;

                    var cType = comp.GetType();


                    // Component
                    // Header
                    bool compOpened;
                    using (g.OpenCloseNode("#_CompH_" + comp.InstanceID, out compOpened, true).ExpandWidth().Height(GuiStyle.ItemHeight).MarginTop(10).Enter())
                    {
                        //g.SeperatorHNode(1f, GuiStyle.Base4 * 0.8f);
                        g.Draw2D.DrawText((compOpened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight), g.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, 8));
                        g.Draw2D.DrawText(GetComponentDisplayName(cType), 23, g.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, 7));
                        isEnabled = comp.Enabled;
                        if (g.Checkbox("IsEnabledChk", ref isEnabled, Offset.Percentage(1f, -25), 0, out var chkNode))
                            comp.Enabled = isEnabled;
                        g.Tooltip("Is Component Enabled?");

                        DragnDrop.Drag(comp, cType);

                        // TODO: Context Menu
                    }

                    // Content
                    if (compOpened)
                    {
                        using (g.Node("#_CompC_" + comp.InstanceID).ExpandWidth().FitContentHeight().Enter())
                        {
                            // Handle Editors for this type if we have any
                            if (compEditors.TryGetValue(comp.InstanceID, out var editor))
                            {
                                editor.OnInspectorGUI();
                                goto EndComponent;
                            }
                            else
                            {
                                var editorType = CustomEditorAttribute.GetEditor(cType);
                                if (editorType != null)
                                {
                                    editor = Activator.CreateInstance(editorType) as ScriptedEditor;
                                    if (editor != null)
                                    {
                                        compEditors[comp.InstanceID] = editor;
                                        editor.target = comp;
                                        editor.OnEnable();
                                        editor.OnInspectorGUI();
                                        goto EndComponent;
                                    }
                                }
                            }

                            // No Editor, Fallback to default Inspector
                            object compRef = comp;
                            if (EditorGUI.PropertyGrid("CompPropertyGrid", ref compRef, TargetFields.Serializable, PropertyGridConfig.NoHeader | PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground))
                                comp.OnValidate();

                            // Draw any Buttons
                            //EditorGui.HandleAttributeButtons(comp);

                            EndComponent:;
                        }
                    }

                    //    HandleComponentContextMenu(go, comp, ref toDelete);
                    //
                    //    ImGui.Indent();
                    //    if (compEditors.TryGetValue(comp.InstanceID, out var editor)) {
                    //        editor.OnInspectorGUI();
                    //        goto EndComponent;
                    //    } else {
                    //        var editorType = CustomEditorAttribute.GetEditor(cType);
                    //        if (editorType != null) {
                    //            editor = Activator.CreateInstance(editorType) as ScriptedEditor;
                    //            if (editor != null) {
                    //                compEditors[comp.InstanceID] = editor;
                    //                editor.target = comp;
                    //                editor.OnEnable();
                    //                editor.OnInspectorGUI();
                    //                goto EndComponent;
                    //            }
                    //        }
                    //    }
                    //
                    //    foreach (var field in RuntimeUtils.GetSerializableFields(comp))
                    //        if (OldPropertyDrawer.Draw(comp, field))
                    //            comp.OnValidate();
                    //    ImGui.Unindent();
                    //
                    //    // Draw any Buttons
                    //    EditorGui.HandleAttributeButtons(comp);
                    //
                    //EndComponent:;
                }
                
                // Handle Deletion
                foreach (var comp in toDelete)
                    go.RemoveComponent(comp);
                
                // Remove any editors that are no longer needed
                HandleUnusedEditors(editorsNeeded);
                
                //HandleAddComponentButton(go);

            }
        }

        private static string GetComponentDisplayName(Type cType)
        {
            var addToMenuAttribute = cType.GetCustomAttribute<AddComponentMenuAttribute>();
            return addToMenuAttribute != null ? Path.GetFileName(addToMenuAttribute.Path) : cType.Name;
        }

        private void HandleUnusedEditors(HashSet<int> editorsNeeded)
        {
            foreach (var key in compEditors.Keys)
                if (!editorsNeeded.Contains(key)) {
                    compEditors[key].OnDisable();
                    compEditors.Remove(key);
                }
        }

        #region Add Component Popup

        //private void HandleAddComponentButton(GameObject? go)
        //{
        //    if (ImGui.Button("Add Component", new System.Numerics.Vector2(ImGui.GetWindowWidth() - 15f, 25f)))
        //        ImGui.OpenPopup("AddComponentContextMenu");
        //
        //    ImGui.PushStyleColor(ImGuiCol.PopupBg, new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 0.6f));
        //    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(6, 6));
        //    if (ImGui.BeginPopup("AddComponentContextMenu")) {
        //        GUIHelper.SearchOld("##searchBox", ref _searchText, ImGui.GetContentRegionAvail().X);
        //
        //        ImGui.Separator();
        //
        //        rootMenuItem ??= GetAddComponentMenuItems();
        //
        //        DrawMenuItems(rootMenuItem, go);
        //
        //        ImGui.EndPopup();
        //    }
        //    ImGui.PopStyleColor();
        //    ImGui.PopStyleVar();
        //}
        //
        //private static void HandleComponentContextMenu(GameObject? go, MonoBehaviour comp, ref List<MonoBehaviour> toDelete)
        //{
        //    if (ImGui.BeginPopupContextItem()) {
        //        if (ImGui.MenuItem("Duplicate")) {
        //            var serialized = Serializer.Serialize(comp);
        //            var copy = Serializer.Deserialize<MonoBehaviour>(serialized);
        //            go.AddComponent(copy);
        //            copy.OnValidate();
        //        }
        //        if (ImGui.MenuItem("Delete")) toDelete.Add(comp);
        //        ImGui.EndPopup();
        //    }
        //}
        //
        //private void DrawMenuItems(MenuItemInfo menuItem, GameObject go)
        //{
        //    bool foundName = false;
        //    bool hasSearch = string.IsNullOrEmpty(_searchText) == false;
        //    foreach (var item in menuItem.Children) {
        //        if (hasSearch && (item.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) == false || item.Type == null)) {
        //            DrawMenuItems(item, go);
        //            if (hasSearch && item.Name.Equals(_searchText, StringComparison.CurrentCultureIgnoreCase))
        //                foundName = true;
        //            continue;
        //        }
        //
        //        if (item.Type != null) {
        //            if (ImGui.MenuItem(item.Name))
        //            {
        //                go.AddComponent(item.Type).OnValidate();
        //            }
        //        } else {
        //            if (ImGui.BeginMenu(item.Name, true)) {
        //                DrawMenuItems(item, go);
        //                ImGui.EndMenu();
        //            }
        //        }
        //    }
        //
        //    if (PlayMode.Current != PlayMode.Mode.Editing) return; // Cannot create scripts during playmode
        //     
        //    // is first and found no component and were searching, lets create a new script
        //    if (hasSearch && !foundName && menuItem == rootMenuItem)
        //    {
        //        if (ImGui.MenuItem("Create Script " + _searchText))
        //        {
        //            FileInfo file = new FileInfo(Project.ProjectAssetDirectory + $"/{_searchText}.cs");
        //            if (File.Exists(file.FullName))
        //                return;
        //            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt");
        //            using StreamReader reader = new StreamReader(stream);
        //            string script = reader.ReadToEnd();
        //            script = script.Replace("%SCRIPTNAME%", EditorUtils.FilterAlpha(_searchText));
        //            File.WriteAllText(file.FullName, script);
        //            AssetDatabase.Ping(file);
        //            // Trigger an update so the script get imported which will recompile all scripts
        //            AssetDatabase.Update();
        //
        //            Type? type = Type.GetType($"{EditorUtils.FilterAlpha(_searchText)}, CSharp, Version=1.0.0.0, Culture=neutral");
        //            if(type != null && type.IsAssignableTo(typeof(MonoBehaviour)))
        //                go.AddComponent(type).OnValidate();
        //            ImGui.EndMenu();
        //        }
        //    }
        //}
        //
        //private MenuItemInfo GetAddComponentMenuItems()
        //{
        //    var componentTypes = AppDomain.CurrentDomain.GetAssemblies()
        //        .SelectMany(assembly => assembly.GetTypes())
        //        .Where(type => type.IsSubclassOf(typeof(MonoBehaviour)) && !type.IsAbstract)
        //        .ToArray();
        //
        //    var items = componentTypes.Select(type => {
        //        string Name = type.Name;
        //        var addToMenuAttribute = type.GetCustomAttribute<AddComponentMenuAttribute>();
        //        if (addToMenuAttribute != null)
        //            Name = addToMenuAttribute.Path;
        //        return (Name, type);
        //    }).ToArray();
        //
        //
        //    // Create a root MenuItemInfo object to serve as the starting point of the tree
        //    MenuItemInfo root = new MenuItemInfo { Name = "Root" };
        //
        //    foreach (var (path, type) in items) {
        //        string[] parts = path.Split('/');
        //
        //        // If first part is 'Hidden' then skip this component
        //        if (parts[0] == "Hidden") continue;
        //
        //        MenuItemInfo currentNode = root;
        //
        //        for (int i = 0; i < parts.Length - 1; i++)  // Skip the last part
        //        {
        //            string part = parts[i];
        //            MenuItemInfo childNode = currentNode.Children.Find(c => c.Name == part);
        //
        //            if (childNode == null) {
        //                childNode = new MenuItemInfo { Name = part };
        //                currentNode.Children.Add(childNode);
        //            }
        //
        //            currentNode = childNode;
        //        }
        //
        //        MenuItemInfo leafNode = new MenuItemInfo {
        //            Name = parts[^1],  // Get the last part
        //            Type = type
        //        };
        //
        //        currentNode.Children.Add(leafNode);
        //    }
        //
        //    SortChildren(root);
        //    return root;
        //}
        //
        //private void SortChildren(MenuItemInfo node)
        //{
        //    node.Children.Sort((x, y) => x.Type == null ? -1 : 1);
        //
        //    foreach (var child in node.Children)
        //        SortChildren(child);
        //}
        
        private class MenuItemInfo
        {
            public string Name;
            public Type Type;
            public List<MenuItemInfo> Children = new();
        
            public MenuItemInfo() { }
        
            public MenuItemInfo(Type type)
            {
                Type = type;
                Name = type.Name;
                var addToMenuAttribute = type.GetCustomAttribute<AddComponentMenuAttribute>();
                if (addToMenuAttribute != null)
                    Name = addToMenuAttribute.Path;
            }
        }

        #endregion


    }
}
