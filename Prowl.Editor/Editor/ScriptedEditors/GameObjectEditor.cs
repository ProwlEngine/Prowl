// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Cloning;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

using static Prowl.Editor.EditorGUI;
using static Prowl.Runtime.GUI.Gui;

namespace Prowl.Editor.EditorWindows.CustomEditors;

/// <summary>
/// GameObject Custom Editor for the Inspector Window
/// </summary>
[CustomEditor(typeof(GameObject))]
public class GameObjectEditor : ScriptedEditor
{
    private string _searchText = string.Empty;
    private static MenuItemInfo rootMenuItem;
    private readonly Dictionary<int, ScriptedEditor> compEditors = new();

    [OnAssemblyUnload]
    public static void ClearCache() => rootMenuItem = null;

    public override void OnDisable()
    {
        foreach (var editor in compEditors.Values)
            editor.OnDisable();
    }

    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        var go = target as GameObject;
        if (go.hideFlags.HasFlag(HideFlags.NotEditable))
            gui.Draw2D.DrawText("This GameObject is not editable", gui.CurrentNode.LayoutData.InnerRect);

        bool isEnabled = go.enabled;
        if (gui.Checkbox("IsEnabledChk", ref isEnabled, 0, 0, out _, GetInputStyle()))
        {
            go.enabled = isEnabled;
            Prefab.OnFieldChange(go, nameof(GameObject.enabled));
        }
        gui.Tooltip("Is Enabled");

        WidgetStyle style = GetInputStyle();
        style.Roundness = 8f;
        style.BorderThickness = 1f;
        string name = go.Name;
        if (gui.InputField("NameInput", ref name, 32, InputFieldFlags.None, ItemSize, 0, Size.Percentage(1f, -(ItemSize * 3)), ItemSize, style))
        {
            go.Name = name.Trim();
            Prefab.OnFieldChange(go, nameof(GameObject.Name));
        }

        var invisStyle = GetInputStyle() with { BGColor = new Color(0, 0, 0, 0), BorderColor = new Color(0, 0, 0, 0) };
        int tagIndex = go.tagIndex;
        if (gui.Combo("#_TagID", "#_TagPopupID", ref tagIndex, TagLayerManager.Instance.tags.ToArray(), Offset.Percentage(1f, -(ItemSize * 2)), 0, ItemSize, ItemSize, invisStyle, null, FontAwesome6.Tag))
        {
            go.tagIndex = (byte)tagIndex;
            Prefab.OnFieldChange(go, nameof(GameObject.tagIndex));
        }
        int layerIndex = go.layerIndex;
        if (gui.Combo("#_LayerID", "#_LayerPopupID", ref layerIndex, TagLayerManager.Instance.layers.ToArray(), Offset.Percentage(1f, -(ItemSize)), 0, ItemSize, ItemSize, invisStyle, null, FontAwesome6.LayerGroup))
        {
            go.layerIndex = (byte)layerIndex;
            Prefab.OnFieldChange(go, nameof(GameObject.layerIndex));
        }

        var btnRoundness = (float)EditorStylePrefs.Instance.ButtonRoundness;

        bool isPrefab = go.PrefabLink != null;
        if (isPrefab)
        {
            // Show buttons to Ping Prefab Asset, Revert Prefab, and Apply Prefab
            using (gui.Node("#_PrefabBtns").ExpandWidth().Height(ItemSize).Top((ItemSize + 5)).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                using (gui.Node("#_SelectBtn").ExpandHeight().Margin(0, 4).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, btnRoundness, 9);
                        AssetDatabase.Ping(go.PrefabLink!.Prefab.AssetID);
                    }
                    else if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted * 0.8f, btnRoundness, 9);
                    else
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Blue, btnRoundness, 9);
                    gui.Draw2D.DrawText("Select", gui.CurrentNode.LayoutData.InnerRect, Color.white, false);
                }

                using (gui.Node("#_RevertBtn").ExpandHeight().Margin(0, 4).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted);

                        // Clear all changes and re-apply Prefabs
                        //go.PrefabLink!.ApplyPrefab();
                        go.PrefabLink.ClearChanges();
                        PrefabLink.ApplyAllLinks([go]);
                    }
                    else if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted * 0.8f);
                    else
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Warning);
                    gui.Draw2D.DrawText("Revert", gui.CurrentNode.LayoutData.InnerRect, Color.white, false);
                }

                using (gui.Node("#_ApplyBtn").ExpandHeight().Margin(0, 4).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, btnRoundness, 6);

                        Prefab? prefab = go.PrefabLink!.Prefab.Res;
                        if (prefab != null)
                        {
                            prefab.Inject(go);
                            if (go.PrefabLink == null)
                                go.LinkToPrefab(prefab);

                            // Save prefab asset
#warning TODO: This should be consolidated into a single method: AssetDatabase.SaveAsset()
                            //AssetDatabase.SaveAsset(prefab);
                            if(AssetDatabase.TryGetFile(prefab.AssetID, out FileInfo? fileInfo))
                            {
                                StringTagConverter.WriteToFile(Serializer.Serialize(prefab), fileInfo);

                                AssetDatabase.Update();
                                AssetDatabase.Ping(fileInfo);
                            }

                            Prefab.OnFieldChange(prefab, null);
                            Prefab.OnFieldChange(go, Prefab.PrefabLinkInfo);
                        }
                    }
                    else if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted * 0.8f, btnRoundness, 6);
                    else
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Green, btnRoundness, 6);
                    gui.Draw2D.DrawText("Apply", gui.CurrentNode.LayoutData.InnerRect, Color.white, false);
                }
            }
        }

        var height = (ItemSize + 5) * (isPrefab ? 2 : 1) + 10;
        var addComponentHeight = 0.0;
        using (gui.Node("#_InspContent").Top(height).ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Clip().Enter())
        {
            addComponentHeight = gui.CurrentNode.LayoutData.Rect.height;

            //if(DragnDrop.Drop<MonoScript>(out var mono))
            //{
            // // TODO: Need a way to know what type this MonoScript is to add it to the GameObject
            //}

            // Transform
            // Header
            bool opened = gui.GetNodeStorage("#_Opened_TransformH", true);
            float animState = 0;
            using (gui.Node("#_TransformH").ExpandWidth().Height(ItemSize).Enter())
            {
                animState = DrawCompHeader(typeof(Transform), opened);

                if (gui.IsNodePressed())
                {
                    opened = !opened;
                    gui.SetNodeStorage(gui.CurrentNode.Parent, "#_Opened_TransformH", opened);
                }

                var rect = gui.CurrentNode.LayoutData.InnerRect;
                var textSizeY = Font.DefaultFont.CalcTextSize("Transform", 20).y;
                var centerY = (rect.height / 2) - (textSizeY / 2);
                gui.Draw2D.DrawText((opened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight), gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, centerY + 3));
                gui.Draw2D.DrawText(FontAwesome6.MapLocation + " Transform", 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(29, centerY + 3), Color.black * 0.8f);
                gui.Draw2D.DrawText(FontAwesome6.MapLocation + " Transform", 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, centerY + 2));

                DragnDrop.Drag(go.Transform, typeof(Transform));
            }

            // Content
            if (opened || animState > 0)
            {
                using (gui.Node("#_TansformC_").ExpandWidth().Layout(LayoutType.Column).Spacing(5).Padding(10).FitContentHeight(animState).Enter())
                {
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne * 0.6f, btnRoundness, 12);

                    var t = go.Transform;
                    using (ActiveGUI.Node("PosParent", 0).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        var tpos = t.localPosition;
                        if (DrawProperty(0, "Position", ref tpos))
                        {
                            t.localPosition = tpos;
                            Prefab.OnFieldChange(go, "_transform");
                        }
                    }

                    using (ActiveGUI.Node("RotParent", 0).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        var tpos = t.localEulerAngles;
                        if (DrawProperty(1, "Rotation", ref tpos))
                        {
                            t.localEulerAngles = tpos;
                            Prefab.OnFieldChange(go, "_transform");
                        }
                    }

                    using (ActiveGUI.Node("ScaleParent", 0).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        var tpos = t.localScale;
                        if (DrawProperty(2, "Scale", ref tpos))
                        {
                            t.localScale = tpos;
                            Prefab.OnFieldChange(go, "_transform");
                        }
                    }
                }
            }

            gui.Node("#_TransformPadding").ExpandWidth().Height(10);

            // Draw Components
            HashSet<int> editorsNeeded = [];

            var allComps = go.GetComponents<MonoBehaviour>();
            foreach (var comp in allComps)
            {
                if (comp == null) continue;
                editorsNeeded.Add(comp.InstanceID);

                if (comp.hideFlags.HasFlag(HideFlags.Hide) || comp.hideFlags.HasFlag(HideFlags.HideAndDontSave) || comp.hideFlags.HasFlag(HideFlags.NotEditable))
                    continue;

                var cType = comp.GetType();

                // Component
                // Header
                string openedID = "#_Opened_CompH" + comp.InstanceID;
                bool compOpened = gui.GetNodeStorage(openedID, true);
                float animStateC = 0;
                using (gui.Node("#_CompH_" + comp.InstanceID).ExpandWidth().Height(ItemSize).Enter())
                {
                    animStateC = DrawCompHeader(cType, compOpened);

                    if (gui.IsNodePressed())
                    {
                        compOpened = !compOpened;
                        gui.SetNodeStorage(gui.CurrentNode.Parent, openedID, compOpened);
                    }

                    DragnDrop.Drag(comp, comp!.GetType());

                    Rect rect = gui.CurrentNode.LayoutData.InnerRect;
                    string cname = GetComponentDisplayName(cType);
                    double textSizeY = Font.DefaultFont.CalcTextSize(cname, 20).y;
                    double centerY = (rect.height / 2) - (textSizeY / 2);
                    gui.Draw2D.DrawText(compOpened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, centerY + 3));
                    gui.Draw2D.DrawText(cname, 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(29, centerY + 3), Color.black * 0.8f);
                    gui.Draw2D.DrawText(cname, 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, centerY + 2));

                    isEnabled = comp.Enabled;
                    if (gui.Checkbox("IsEnabledChk", ref isEnabled, Offset.Percentage(1f, -30), 0, out LayoutNode? chkNode, GetInputStyle()))
                        comp.Enabled = isEnabled;

                    gui.Tooltip("Is Component Enabled?");


                    if (gui.IsPointerClick(MouseButton.Right) && gui.IsNodeHovered())
                    {
                        // Popup holder is our parent, since thats the Tree node
                        gui.OpenPopup("RightClickComp", null);
                        gui.SetGlobalStorage("RightClickComp", comp.InstanceID);
                    }

                    var popupHolder = gui.CurrentNode;
                    if (gui.BeginPopup("RightClickComp", out var node))
                    {
                        using (node.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                        {
                            var instanceID = gui.GetGlobalStorage<int>("RightClickComp");
                            if (instanceID == comp.InstanceID)
                                HandleComponentContextMenu(go, comp, popupHolder);
                        }
                    }
                }

                // Content
                if (compOpened || animStateC > 0)
                {
                    using (gui.Node("#_CompC_" + comp.InstanceID).ExpandWidth().FitContentHeight(animStateC).Padding(10).Enter())
                    {
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne * 0.6f, btnRoundness, 12);

                        EditorGUI.FieldChanges subChanges = new();
                        // Handle Editors for this type if we have any
                        if (compEditors.TryGetValue(comp.InstanceID, out ScriptedEditor? editor))
                        {
                            editor.OnInspectorGUI(subChanges);
                        }
                        else
                        {
                            editor = CreateEditor(comp);
                            if (editor != null)
                            {
                                compEditors[comp.InstanceID] = editor;
                                editor.OnInspectorGUI(subChanges);
                            }
                        }

                        foreach (var change in subChanges.AllChanges)
                        {
                            Prefab.OnFieldChange(change.target, change.field.Name);
                            // Propagate changes to the main FieldChanges
                            changes.Add(change.target, change.field);
                        }

                        // ScriptedEditor.CreateEditor should provide a fallback default instead of providing
                        // No Editor, Fallback to default Inspector
                        // object compRef = comp;
                        // if (PropertyGrid("CompPropertyGrid", ref compRef, TargetFields.Serializable | TargetFields.Properties, PropertyGridConfig.NoHeader | PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground))
                        //     comp.OnValidate();

                        // Draw any Buttons - these should be in PropertyGrid probably
                        //EditorGui.HandleAttributeButtons(comp);
                    }
                }

                gui.Node("#_CompPadding", comp.InstanceID).ExpandWidth().Height(10);

                //HandleComponentContextMenu(go, comp, ref toDelete);
            }


            // Remove any editors that are no longer needed
            HandleUnusedEditors(editorsNeeded);

            using (gui.Node("AddCompBtn").ExpandWidth().Height(ItemSize).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? EditorStylePrefs.Violet : EditorStylePrefs.Instance.Highlighted, btnRoundness);
                gui.Draw2D.DrawText("Add Component", gui.CurrentNode.LayoutData.InnerRect, Color.white, false);

                if (gui.IsNodePressed())
                    gui.OpenPopup("AddComponentPopup", null, gui.CurrentNode);

                var popupHolder = gui.CurrentNode;
                if (gui.BeginPopup("AddComponentPopup", out var node))
                {
                    using (node.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                    {
                        gui.Search("##searchBox", ref _searchText, 0, 0, Size.Percentage(1f));

                        Separator();

                        rootMenuItem ??= GetAddComponentMenuItems();
                        DrawMenuItems(rootMenuItem, go);
                    }
                }
            }

            //HandleAddComponentButton(go);

        }
    }

    private float DrawCompHeader(Type cType, bool compOpened)
    {
        float animState = gui.AnimateBool(compOpened, 0.1f, EaseType.Linear);
        var compColor = EditorStylePrefs.RandomPastelColor(cType.GetHashCode());
        if (compOpened || animState > 0)
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, compColor, (float)EditorStylePrefs.Instance.ButtonRoundness, 3);
        else
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, compColor, (float)EditorStylePrefs.Instance.ButtonRoundness);
        return animState;
    }

    private static string GetComponentDisplayName(Type cType)
    {
        var addToMenuAttribute = cType.GetCustomAttribute<AddComponentMenuAttribute>();
        return addToMenuAttribute != null ? Path.GetFileName(addToMenuAttribute.Path) : cType.Name;
    }

    private void HandleUnusedEditors(HashSet<int> editorsNeeded)
    {
        foreach (var key in compEditors.Keys)
            if (!editorsNeeded.Contains(key))
            {
                compEditors[key].OnDisable();
                compEditors.Remove(key);
            }
    }

    #region Add Component Popup

    private static void HandleComponentContextMenu(GameObject? go, MonoBehaviour comp, LayoutNode popupHolder)
    {
        bool closePopup = false;
        //if (StyledButton("Duplicate"))
        //{
        //    MonoBehaviour cloned = comp.DeepClone();
        //    go.AddComponent(cloned);
        //    cloned.OnValidate();
        //    closePopup = true;
        //}

        if (StyledButton("Delete"))
        {
            go.RemoveComponent(comp);
            closePopup = true;
        }

        if (closePopup)
            ActiveGUI.ClosePopup(popupHolder);
    }

    private void DrawMenuItems(MenuItemInfo menuItem, GameObject go)
    {
        bool foundName = false;
        bool hasSearch = string.IsNullOrEmpty(_searchText) == false;
        foreach (var item in menuItem.Children)
        {
            if (hasSearch && (item.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) == false || item.Type == null))
            {
                DrawMenuItems(item, go);
                if (hasSearch && item.Name.Equals(_searchText, StringComparison.CurrentCultureIgnoreCase))
                    foundName = true;
                continue;
            }

            if (item.Type != null)
            {

                if (StyledButton(item.Name))
                {
                    if (go.GetComponent(item.Type) != null)
                    {
                        Debug.LogError($"Component {item.Type.Name} already exists on GameObject");
                        return;
                    }

                    var comp = go.AddComponent(item.Type);
                    comp.OnValidate();
                }

            }
            else
            {

                if (StyledButton(item.Name))
                    ActiveGUI.OpenPopup(item.Name + "Popup", ActiveGUI.PreviousNode.LayoutData.Rect.TopRight);

                // Enter the Button's Node
                using (ActiveGUI.PreviousNode.Enter())
                {
                    // Draw a > to indicate a popup
                    Rect rect = ActiveGUI.CurrentNode.LayoutData.Rect;
                    rect.x = rect.x + rect.width - 25;
                    rect.width = 20;
                    ActiveGUI.Draw2D.DrawText(FontAwesome6.ChevronRight, rect, Color.white);
                }

                if (ActiveGUI.BeginPopup(item.Name + "Popup", out var node))
                {
                    double largestWidth = 0;
                    foreach (var child in item.Children)
                    {
                        double width = Font.DefaultFont.CalcTextSize(child.Name, 0).x + 30;
                        if (width > largestWidth)
                            largestWidth = width;
                    }

                    using (node.Width(largestWidth).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
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
            if (StyledButton("Create Script " + _searchText))
            {
                FileInfo file = new FileInfo(Path.Combine(Project.Active!.AssetDirectory.FullName, $"/{_searchText}.cs"));
                if (File.Exists(file.FullName))
                    return;

                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt")!;
                using StreamReader reader = new StreamReader(stream);
                string script = reader.ReadToEnd();
                script = script.Replace("%SCRIPTNAME%", EditorUtils.FilterAlpha(_searchText));
                File.WriteAllText(file.FullName, script);
                AssetDatabase.Ping(file);
                // Trigger an update so the script get imported which will recompile all scripts
                AssetDatabase.Update();

                Type? type = Type.GetType($"{EditorUtils.FilterAlpha(_searchText)}, CSharp, Version=1.0.0.0, Culture=neutral");
                if (type != null && type.IsAssignableTo(typeof(MonoBehaviour)))
                {
                    if (go.GetComponent(type) != null)
                    {
                        Debug.LogError($"Script {type.Name} already exists on GameObject");
                        return;
                    }

                    MonoBehaviour comp = go.AddComponent(type);
                    comp.OnValidate();
                }
            }
        }
    }

    private MenuItemInfo GetAddComponentMenuItems()
    {
        var componentTypes = AppDomain.CurrentDomain.GetAssemblies()
                                      .SelectMany(assembly => assembly.GetTypes())
                                      .Where(type => type.IsSubclassOf(typeof(MonoBehaviour)) && !type.IsAbstract)
                                      .ToArray();

        var items = componentTypes.Select(type =>
        {
            string Name = type.Name;
            var addToMenuAttribute = type.GetCustomAttribute<AddComponentMenuAttribute>();
            if (addToMenuAttribute != null)
                Name = addToMenuAttribute.Path;
            return (Name, type);
        }).ToArray();


        // Create a root MenuItemInfo object to serve as the starting point of the tree
        MenuItemInfo root = new MenuItemInfo { Name = "Root" };

        foreach (var (path, type) in items)
        {
            string[] parts = path.Split('/');

            // If first part is 'Hidden' then skip this component
            if (parts[0] == "Hidden") continue;

            MenuItemInfo currentNode = root;

            for (int i = 0; i < parts.Length - 1; i++)  // Skip the last part
            {
                string part = parts[i];
                MenuItemInfo childNode = currentNode.Children.Find(c => c.Name == part);

                if (childNode == null)
                {
                    childNode = new MenuItemInfo { Name = part };
                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }

            MenuItemInfo leafNode = new MenuItemInfo
            {
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
        public readonly List<MenuItemInfo> Children = new();

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
