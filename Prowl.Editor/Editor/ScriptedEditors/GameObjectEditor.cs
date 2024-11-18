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
        foreach (ScriptedEditor editor in compEditors.Values)
            editor.OnDisable();
    }

    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        var go = target as GameObject;
        if (go.hideFlags.HasFlag(HideFlags.NotEditable))
            gui.Draw2D.DrawText("This GameObject is not editable", gui.CurrentNode.LayoutData.InnerRect);

        bool isEnabled = go.enabled;
        if (gui.Checkbox("IsEnabledChk", ref isEnabled, 0, 0, out _, InputStyle))
        {
            UndoRedoManager.RecordAction(new ChangeFieldOnGameObjectAction(go, nameof(GameObject.enabled), isEnabled));
            //UndoRedoManager.SetMember(go, nameof(GameObject.enabled), isEnabled);
            go.enabled = isEnabled;
            Prefab.OnFieldChange(go, nameof(GameObject.enabled));
        }
        gui.Tooltip("Is Enabled");

        WidgetStyle style = InputStyle;
        style.Roundness = 8f;
        style.BorderThickness = 1f;
        string name = go.Name;
        if (gui.InputField("NameInput", ref name, 32, InputFieldFlags.None, ItemSize, 0, Size.Percentage(1f, -(ItemSize * 4)), ItemSize, style))
        {
            UndoRedoManager.RecordAction(new ChangeFieldOnGameObjectAction(go, nameof(GameObject.Name), name.Trim()));
            //UndoRedoManager.SetMember(go, nameof(GameObject.Name), name.Trim());
            go.Name = name.Trim();
            Prefab.OnFieldChange(go, nameof(GameObject.Name));
        }

        WidgetStyle invisStyle = InputStyle with { BorderColor = new Color(0, 0, 0, 0) };
        int tagIndex = go.tagIndex;
        if (gui.Combo("#_TagID", "#_TagPopupID", ref tagIndex, TagLayerManager.Instance.tags.ToArray(), Offset.Percentage(1f, -(ItemSize * 3)), 0, ItemSize, ItemSize, EditorGUI.InputStyle, null, FontAwesome6.Tag))
        {
            UndoRedoManager.RecordAction(new ChangeFieldOnGameObjectAction(go, nameof(GameObject.tagIndex), (byte)tagIndex));
            //UndoRedoManager.SetMember(go, nameof(GameObject.tagIndex), (byte)tagIndex);
            go.tagIndex = (byte)tagIndex;
            Prefab.OnFieldChange(go, nameof(GameObject.tagIndex));
        }
        int layerIndex = go.layerIndex;
        if (gui.Combo("#_LayerID", "#_LayerPopupID", ref layerIndex, TagLayerManager.Instance.layers.ToArray(), Offset.Percentage(1f, -(ItemSize * 2)), 0, ItemSize, ItemSize, EditorGUI.InputStyle, null, FontAwesome6.LayerGroup))
        {
            UndoRedoManager.RecordAction(new ChangeFieldOnGameObjectAction(go, nameof(GameObject.layerIndex), (byte)layerIndex));
            //UndoRedoManager.SetMember(go, nameof(GameObject.layerIndex), (byte)layerIndex);
            Prefab.OnFieldChange(go, nameof(GameObject.layerIndex));
        }

        bool isStatic = go.isStatic;
        if (gui.Checkbox("IsStaticChk", ref isStatic, Offset.Percentage(1f, -(ItemSize)), 0, out _, InputStyle))
        {
            var prop = go.GetType().GetProperty(nameof(GameObject.isStatic), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            UndoRedoManager.RecordAction(new ChangeFieldOnGameObjectAction(go, prop, isStatic));
            //UndoRedoManager.SetMember(go, go.GetType().GetProperty(nameof(GameObject.isStatic), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), isStatic);
            Prefab.OnFieldChange(go, nameof(GameObject.isStatic));
        }
        gui.Tooltip("Is Static");

        float btnRoundness = (float)EditorStylePrefs.Instance.ButtonRoundness;

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
                        go.PrefabLink!.ClearChanges();
                        PrefabLink.ApplyAllLinks([go], null, true);
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

                        Prefab prefab = go.PrefabLink!.Prefab.Res!;
                        if (!object.ReferenceEquals(prefab, null))
                        {
                            prefab.Inject(go);
                            if (go.PrefabLink == null)
                                go.LinkToPrefab(prefab);

                            // Save prefab asset
                            AssetDatabase.SaveAsset(prefab);

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

        double height = (ItemSize + 5) * (isPrefab ? 2 : 1) + 10;
        double addComponentHeight = 0.0;
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

                Rect rect = gui.CurrentNode.LayoutData.InnerRect;
                double textSizeY = Font.DefaultFont.CalcTextSize("Transform", 20).y;
                double centerY = (rect.height / 2) - (textSizeY / 2);
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

                    Transform t = go.Transform.DeepClone();
                    bool transformChanged = false;
                    using (ActiveGUI.Node("PosParent", 0).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        if (DrawProperty(0, "Position", t, nameof(t.localPosition)))
                        {
                            transformChanged = true;
                            Prefab.OnFieldChange(go, "_transform");
                        }
                    }

                    using (ActiveGUI.Node("RotParent", 0).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        if (DrawProperty(1, "Rotation", t, nameof(t.localEulerAngles)))
                        {
                            transformChanged = true;
                            Prefab.OnFieldChange(go, "_transform");
                        }
                    }

                    using (ActiveGUI.Node("ScaleParent", 0).ExpandWidth().Height(ItemSize).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        if (DrawProperty(2, "Scale", t, nameof(t.localScale)))
                        {
                            transformChanged = true;
                            Prefab.OnFieldChange(go, "_transform");
                        }
                    }

                    if (transformChanged)
                    {
                        UndoRedoManager.RecordAction(new ChangeTransformAction(go, t.localPosition, t.localRotation, t.localScale));
                        //UndoRedoManager.SetMember(go, "_transform", t);
                    }
                }
            }

            gui.Node("#_TransformPadding").ExpandWidth().Height(10);

            // Draw Components
            HashSet<int> editorsNeeded = [];

            IEnumerable<MonoBehaviour> allComps = go.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour comp in allComps)
            {
                if (comp == null) continue;
                editorsNeeded.Add(comp.InstanceID);

                if (comp.hideFlags.HasFlag(HideFlags.Hide) || comp.hideFlags.HasFlag(HideFlags.HideAndDontSave) || comp.hideFlags.HasFlag(HideFlags.NotEditable))
                    continue;

                Type cType = comp.GetType();

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

                    string displayName = GetComponentDisplayName(cType);
                    string cname = displayName;
                    if (comp.IsOnPrefabInstance)
                    {
                        if (comp.IsPrefabSource)
                        {
                            if (comp.HasPrefabMod)
                                cname += "*";
                            cname += "   " + FontAwesome6.CircleCheck;
                        }
                        else
                            cname += " - Unsaved!";
                    }

                    double textSizeY = Font.DefaultFont.CalcTextSize(cname, 20).y;
                    double centerY = (rect.height / 2) - (textSizeY / 2);
                    gui.Draw2D.DrawText(compOpened ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(8, centerY + 3));
                    gui.Draw2D.DrawText(cname, 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(29, centerY + 3), Color.black * 0.8f);
                    gui.Draw2D.DrawText(cname, 23, gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(28, centerY + 2));

                    isEnabled = comp.Enabled;
                    if (gui.Checkbox("IsEnabledChk", ref isEnabled, Offset.Percentage(1f, -30), 0, out LayoutNode? chkNode, InputStyle))
                    {
                        //comp.Enabled = isEnabled;
                        UndoRedoManager.RecordAction(new ChangeFieldOnComponentAction(comp, typeof(MonoBehaviour).GetProperty(nameof(MonoBehaviour.Enabled)), isEnabled));

                    }

                    gui.Tooltip("Is Component Enabled?");


                    if (gui.IsPointerClick(MouseButton.Right) && gui.IsNodeHovered())
                    {
                        // Popup holder is our parent, since thats the Tree node
                        gui.OpenPopup("RightClickComp", null);
                        gui.SetGlobalStorage("RightClickComp", comp.InstanceID);
                    }

                    if (gui.BeginPopup("RightClickComp", out LayoutNode? node, false, EditorGUI.InputStyle))
                    {
                        using (node!.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                        {
                            int instanceID = gui.GetGlobalStorage<int>("RightClickComp");
                            if (instanceID == comp.InstanceID)
                                HandleComponentContextMenu(go, comp);
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

                        foreach ((object target, FieldInfo field) change in subChanges.AllChanges)
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

                LayoutNode popupHolder = gui.CurrentNode;
                if (gui.BeginPopup("AddComponentPopup", out LayoutNode? node, false, EditorGUI.InputStyle))
                {
                    using (node!.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                    {
                        gui.Search("##searchBox", ref _searchText, 0, 0, Size.Percentage(1f), null, EditorGUI.InputFieldStyle);

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
        Color compColor = EditorStylePrefs.RandomPastelColor(cType.GetHashCode());
        if (compOpened || animState > 0)
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, compColor, (float)EditorStylePrefs.Instance.ButtonRoundness, 3);
        else
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, compColor, (float)EditorStylePrefs.Instance.ButtonRoundness);
        return animState;
    }

    private static string GetComponentDisplayName(Type cType)
    {
        AddComponentMenuAttribute? addToMenuAttribute = cType.GetCustomAttribute<AddComponentMenuAttribute>();
        return addToMenuAttribute != null ? Path.GetFileName(addToMenuAttribute.Path) : cType.Name;
    }

    private void HandleUnusedEditors(HashSet<int> editorsNeeded)
    {
        foreach (int key in compEditors.Keys)
            if (!editorsNeeded.Contains(key))
            {
                compEditors[key].OnDisable();
                compEditors.Remove(key);
            }
    }

    #region Add Component Popup

    private static void HandleComponentContextMenu(GameObject? go, MonoBehaviour comp)
    {
        bool closePopup = false;

        if (go == null) return;

        if (StyledButton("Duplicate"))
        {
            UndoRedoManager.RecordAction(new AddComponentAction(go.Identifier, comp.DeepClone()));
            //MonoBehaviour cloned = comp.DeepClone();
            //go.AddComponent(cloned);
            //cloned.OnValidate();
            closePopup = true;
        }

        if (StyledButton("Delete"))
        {
            UndoRedoManager.RecordAction(new RemoveComponentAction(go.Identifier, comp.Identifier));
            //go!.RemoveComponent(comp);
            closePopup = true;
        }

        if (closePopup)
            ActiveGUI.CloseAllPopups();
    }

    private void DrawMenuItems(MenuItemInfo menuItem, GameObject go)
    {
        bool foundName = false;
        bool hasSearch = string.IsNullOrEmpty(_searchText) == false;
        foreach (MenuItemInfo item in menuItem.Children)
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
                    UndoRedoManager.RecordAction(new AddComponentAction<MonoBehaviour>(go.Identifier, item.Type));
                    //MonoBehaviour comp = go.AddComponent(item.Type);
                    //comp.OnValidate();
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

                if (ActiveGUI.BeginPopup(item.Name + "Popup", out LayoutNode? node, false, EditorGUI.InputStyle))
                {
                    double largestWidth = 0;
                    foreach (MenuItemInfo child in item.Children)
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
                FileInfo file = new(Path.Combine(Project.Active!.AssetDirectory.FullName, $"/{_searchText}.cs"));
                if (File.Exists(file.FullName))
                    return;

                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt")!;
                using StreamReader reader = new(stream);
                string script = reader.ReadToEnd();
                script = script.Replace("%SCRIPTNAME%", EditorUtils.FilterAlpha(_searchText));
                File.WriteAllText(file.FullName, script);
                AssetDatabase.Ping(file);
                // Trigger an update so the script get imported which will recompile all scripts
                AssetDatabase.Update();

                Type? type = Type.GetType($"{EditorUtils.FilterAlpha(_searchText)}, CSharp, Version=1.0.0.0, Culture=neutral");
                if (type != null && type.IsAssignableTo(typeof(MonoBehaviour)))
                {
                    UndoRedoManager.RecordAction(new AddComponentAction<MonoBehaviour>(go.Identifier, type));
                    //MonoBehaviour comp = go.AddComponent(type);
                    //comp.OnValidate();
                }
            }
        }
    }

    private static MenuItemInfo GetAddComponentMenuItems()
    {
        Type[] componentTypes = AppDomain.CurrentDomain.GetAssemblies()
                                      .SelectMany(assembly => assembly.GetTypes())
                                      .Where(type => type.IsSubclassOf(typeof(MonoBehaviour)) && !type.IsAbstract)
                                      .ToArray();

        (string Name, Type type)[] items = componentTypes.Select(type =>
        {
            string Name = type.Name;
            AddComponentMenuAttribute? addToMenuAttribute = type.GetCustomAttribute<AddComponentMenuAttribute>();
            if (addToMenuAttribute != null)
                Name = addToMenuAttribute.Path;
            return (Name, type);
        }).ToArray();


        // Create a root MenuItemInfo object to serve as the starting point of the tree
        MenuItemInfo root = new() { Name = "Root" };

        foreach ((string path, Type type) in items)
        {
            string[] parts = path.Split('/');

            // If first part is 'Hidden' then skip this component
            if (parts[0] == "Hidden") continue;

            MenuItemInfo currentNode = root;

            for (int i = 0; i < parts.Length - 1; i++)  // Skip the last part
            {
                string part = parts[i];
                MenuItemInfo? childNode = currentNode.Children.Find(c => c.Name == part);

                if (childNode == null)
                {
                    childNode = new MenuItemInfo { Name = part };
                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }

            MenuItemInfo leafNode = new()
            {
                Name = parts[^1],  // Get the last part
                Type = type
            };

            currentNode.Children.Add(leafNode);
        }

        SortChildren(root);
        return root;
    }

    private static void SortChildren(MenuItemInfo node)
    {
        node.Children.Sort((x, y) => x.Type == null ? -1 : 1);

        foreach (MenuItemInfo child in node.Children)
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
            AddComponentMenuAttribute? addToMenuAttribute = type.GetCustomAttribute<AddComponentMenuAttribute>();
            if (addToMenuAttribute != null)
                Name = addToMenuAttribute.Path;
        }
    }

    #endregion


}
