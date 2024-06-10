using ImageMagick;
using Prowl.Editor.Assets;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;
using System.ComponentModel;
using System.Reflection;
using static Assimp.Metadata;
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
                gui.Draw2D.DrawText("This GameObject is not editable", gui.CurrentNode.LayoutData.InnerRect);

            bool isEnabled = go.enabled;
            if(gui.Checkbox("IsEnabledChk", ref isEnabled, 0, 0, out _))
                go.enabled = isEnabled;
            gui.Tooltip("Is Enabled");

            GuiStyle style = new();
            style.WidgetColor = GuiStyle.WindowBackground;
            style.Border = GuiStyle.Borders;
            style.WidgetRoundness = 8f;
            style.BorderThickness = 1f;
            string name = go.Name;
            if (gui.InputField("NameInput", ref name, 32, InputFieldFlags.None, GuiStyle.ItemHeight, 0, Size.Percentage(1f, -(GuiStyle.ItemHeight * 3)), GuiStyle.ItemHeight, style))
                go.Name = name.Trim();

            var invisStyle = new GuiStyle { WidgetColor = new Color(0, 0, 0, 0), Border = new Color(0, 0, 0, 0) };
            gui.Combo("#_TagID", "#_TagPopupID", ref go.tagIndex, TagLayerManager.Instance.tags.ToArray(), Offset.Percentage(1f, -(GuiStyle.ItemHeight * 2)), 0, GuiStyle.ItemHeight, GuiStyle.ItemHeight, invisStyle, FontAwesome6.Tag);
            gui.Combo("#_LayerID", "#_LayerPopupID", ref go.layerIndex, TagLayerManager.Instance.layers.ToArray(), Offset.Percentage(1f, -(GuiStyle.ItemHeight)), 0, GuiStyle.ItemHeight, GuiStyle.ItemHeight, invisStyle, FontAwesome6.LayerGroup);

            if (go.IsPrefab)
            {
                // Show buttons to Ping Prefab Asset, Revert Prefab, and Apply Prefab
                using (gui.Node("#_PrefabBtns").ExpandWidth().Height(GuiStyle.ItemHeight).Top((GuiStyle.ItemHeight + 5)).Layout(LayoutType.Row).ScaleChildren().Enter())
                {
                    using (gui.Node("#_SelectBtn").ExpandHeight().Margin(0, 4).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10, 9);
                            AssetDatabase.Ping(go.AssetID);
                        }
                        else if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f, 10, 9);
                        else
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Blue, 10, 9);
                        gui.Draw2D.DrawText("Select", gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);
                    }

                    using (gui.Node("#_RevertBtn").ExpandHeight().Margin(0, 4).Enter())
                    {
                        if (gui.IsNodePressed())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                        else if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f);
                        else
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Red);
                        gui.Draw2D.DrawText("Revert", gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);
                    }

                    using (gui.Node("#_ApplyBtn").ExpandHeight().Margin(0, 4).Enter())
                    {
                        if (gui.IsNodePressed())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 10, 6);
                        else if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f, 10, 6);
                        else
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Green, 10, 6);
                        gui.Draw2D.DrawText("Apply", gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);
                    }
                }
            }
            
            var height = (GuiStyle.ItemHeight + 5) * (go.IsPrefab ? 2 : 1);
            var addComponentHeight = 0.0;
            using (gui.Node("#_InspContent").Top(height).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                addComponentHeight = gui.CurrentNode.LayoutData.Rect.height;

                //if(DragnDrop.Drop<MonoScript>(out var mono))
                //{
                // // TODO: Need a way to know what type this MonoScript is to add it to the GameObject
                //}

                // Transform
                // Header
                bool opened = gui.GetNodeStorage("#_Opened_TransformH", true);
                using (gui.Node("#_TransformH").ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        opened = !opened;
                        gui.SetNodeStorage(gui.CurrentNode.Parent, "#_Opened_TransformH", opened);
                    }

                    gui.Draw2D.DrawText((opened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight), gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, 8));
                    gui.Draw2D.DrawText("Transform", 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, 7));

                    DragnDrop.Drag(go.Transform, typeof(Transform));
                }

                // Content
                if (opened)
                {
                    using (gui.Node("#_TansformC_").ExpandWidth().Layout(LayoutType.Column).FitContentHeight().Enter())
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
                    string openedID = "#_Opened_CompH" + comp.InstanceID;
                    bool compOpened = gui.GetNodeStorage(openedID, true);
                    using (gui.Node("#_CompH_" + comp.InstanceID).ExpandWidth().Height(GuiStyle.ItemHeight).MarginTop(10).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            compOpened = !compOpened;
                            gui.SetNodeStorage(gui.CurrentNode.Parent, openedID, compOpened);
                        }

                        //g.SeperatorHNode(1f, GuiStyle.Base4 * 0.8f);
                        gui.Draw2D.DrawText((compOpened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight), gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, 8));
                        gui.Draw2D.DrawText(GetComponentDisplayName(cType), 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, 7));
                        isEnabled = comp.Enabled;
                        if (gui.Checkbox("IsEnabledChk", ref isEnabled, Offset.Percentage(1f, -25), 0, out var chkNode))
                            comp.Enabled = isEnabled;
                        gui.Tooltip("Is Component Enabled?");

                        DragnDrop.Drag(comp, cType);

                        // TODO: Context Menu
                        if (gui.IsPointerClick(Silk.NET.Input.MouseButton.Right) && gui.IsNodeHovered())
                        {
                            // Popup holder is our parent, since thats the Tree node
                            gui.OpenPopup("RightClickComp", null, gui.CurrentNode.Parent);
                            gui.SetGlobalStorage("RightClickComp", comp.InstanceID);
                        }

                        var popupHolder = gui.CurrentNode;
                        if (gui.BeginPopup("RightClickComp", out var node))
                        {
                            using (node.Width(150).Layout(LayoutType.Column).FitContentHeight().Enter())
                            {
                                var instanceID = gui.GetGlobalStorage<int>("RightClickComp");
                                //if(instanceID == comp.InstanceID)
                                //    DrawContextMenu(go, popupHolder);
                            }
                        }
                    }

                    // Content
                    if (compOpened)
                    {
                        using (gui.Node("#_CompC_" + comp.InstanceID).ExpandWidth().FitContentHeight().Enter())
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

                            // Draw any Buttons - these should be in PropertyGrid probably
                            //EditorGui.HandleAttributeButtons(comp);

                            EndComponent:;
                        }
                    }

                     //HandleComponentContextMenu(go, comp, ref toDelete);
                }
                
                // Handle Deletion
                foreach (var comp in toDelete)
                    go.RemoveComponent(comp);
                
                // Remove any editors that are no longer needed
                HandleUnusedEditors(editorsNeeded);

                using (gui.Node("AddCompBtn").ExpandWidth().Height(GuiStyle.ItemHeight).Top(addComponentHeight + 50).IgnoreLayout().Enter())
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Violet : GuiStyle.Indigo, 10);
                    gui.Draw2D.DrawText("Add Component", gui.CurrentNode.LayoutData.InnerRect, GuiStyle.Base11, false);

                    if (gui.IsNodePressed())
                        gui.OpenPopup("AddComponentPopup", null, gui.CurrentNode);

                    var popupHolder = gui.CurrentNode;
                    if (gui.BeginPopup("AddComponentPopup", out var node))
                    {
                        using (node.Width(150).Layout(LayoutType.Column).FitContentHeight().Enter())
                        {
                            gui.Search("##searchBox", ref _searchText, 0, 0, Size.Percentage(1f));

                            EditorGUI.Separator();

                            rootMenuItem ??= GetAddComponentMenuItems();
                            DrawMenuItems(rootMenuItem, go);
                        }
                    }
                }

                //HandleAddComponentButton(go);


                gui.ScrollV();
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
        
        private void DrawMenuItems(MenuItemInfo menuItem, GameObject go)
        {
            bool foundName = false;
            bool hasSearch = string.IsNullOrEmpty(_searchText) == false;
            foreach (var item in menuItem.Children) {
                if (hasSearch && (item.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) == false || item.Type == null)) {
                    DrawMenuItems(item, go);
                    if (hasSearch && item.Name.Equals(_searchText, StringComparison.CurrentCultureIgnoreCase))
                        foundName = true;
                    continue;
                }
        
                if (item.Type != null) 
                {

                    if (EditorGUI.StyledButton(item.Name))
                        go.AddComponent(item.Type).OnValidate();

                } else {

                    if (EditorGUI.StyledButton(item.Name))
                        Gui.ActiveGUI.OpenPopup(item.Name + "Popup", Gui.ActiveGUI.PreviousNode.LayoutData.Rect.TopRight);

                    // Enter the Button's Node
                    using (Gui.ActiveGUI.PreviousNode.Enter())
                    {
                        // Draw a > to indicate a popup
                        Rect rect = Gui.ActiveGUI.CurrentNode.LayoutData.Rect;
                        rect.x = rect.x + rect.width - 25;
                        rect.width = 20;
                        Gui.ActiveGUI.Draw2D.DrawText(FontAwesome6.ChevronRight, rect, Color.white);
                    }

                    if (Gui.ActiveGUI.BeginPopup(item.Name + "Popup", out var node))
                    {
                        using (node.Width(150).Layout(LayoutType.Column).FitContentHeight().Enter())
                        {
                            DrawMenuItems(item, go);
                        }
                    }
                }
            }
        
            if (PlayMode.Current != PlayMode.Mode.Editing) return; // Cannot create scripts during playmode
             
            // is first and found no component and were searching, lets create a new script
            if (hasSearch && !foundName && menuItem == rootMenuItem)
            {
                if (EditorGUI.StyledButton("Create Script " + _searchText))
                {
                    FileInfo file = new FileInfo(Project.ProjectAssetDirectory + $"/{_searchText}.cs");
                    if (File.Exists(file.FullName))
                        return;
                    using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt");
                    using StreamReader reader = new StreamReader(stream);
                    string script = reader.ReadToEnd();
                    script = script.Replace("%SCRIPTNAME%", EditorUtils.FilterAlpha(_searchText));
                    File.WriteAllText(file.FullName, script);
                    AssetDatabase.Ping(file);
                    // Trigger an update so the script get imported which will recompile all scripts
                    AssetDatabase.Update();
        
                    Type? type = Type.GetType($"{EditorUtils.FilterAlpha(_searchText)}, CSharp, Version=1.0.0.0, Culture=neutral");
                    if(type != null && type.IsAssignableTo(typeof(MonoBehaviour)))
                        go.AddComponent(type).OnValidate();
                }
            }
        }
        
        private MenuItemInfo GetAddComponentMenuItems()
        {
            var componentTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsSubclassOf(typeof(MonoBehaviour)) && !type.IsAbstract)
                .ToArray();
        
            var items = componentTypes.Select(type => {
                string Name = type.Name;
                var addToMenuAttribute = type.GetCustomAttribute<AddComponentMenuAttribute>();
                if (addToMenuAttribute != null)
                    Name = addToMenuAttribute.Path;
                return (Name, type);
            }).ToArray();
        
        
            // Create a root MenuItemInfo object to serve as the starting point of the tree
            MenuItemInfo root = new MenuItemInfo { Name = "Root" };
        
            foreach (var (path, type) in items) {
                string[] parts = path.Split('/');
        
                // If first part is 'Hidden' then skip this component
                if (parts[0] == "Hidden") continue;
        
                MenuItemInfo currentNode = root;
        
                for (int i = 0; i < parts.Length - 1; i++)  // Skip the last part
                {
                    string part = parts[i];
                    MenuItemInfo childNode = currentNode.Children.Find(c => c.Name == part);
        
                    if (childNode == null) {
                        childNode = new MenuItemInfo { Name = part };
                        currentNode.Children.Add(childNode);
                    }
        
                    currentNode = childNode;
                }
        
                MenuItemInfo leafNode = new MenuItemInfo {
                    Name = parts[^1],  // Get the last part
                    Type = type
                };
        
                currentNode.Children.Add(leafNode);
            }
        
            SortChildren(root);
            return root;
        }
        
        private void SortChildren(MenuItemInfo node)
        {
            node.Children.Sort((x, y) => x.Type == null ? -1 : 1);
        
            foreach (var child in node.Children)
                SortChildren(child);
        }
        
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
