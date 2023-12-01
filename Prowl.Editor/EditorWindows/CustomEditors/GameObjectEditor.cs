using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using Prowl.Icons;
using HexaEngine.ImGuiNET;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Prowl.Runtime.Assets;

namespace Prowl.Editor.EditorWindows.CustomEditors
{
    [CustomEditor(typeof(GameObject))]
    public class GameObjectEditor : ScriptedEditor
    {
        private string _searchText = string.Empty;
        private MenuItemInfo rootMenuItem;
        private Dictionary<int, ScriptedEditor> compEditors;

        private void Space() => ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);

        public override void OnEnable()
        {
            var go = target as GameObject;

            // Create all the editors for all the types
            var comps = go.GetComponents<MonoBehaviour>();
            compEditors = new();
            for (int i = 0; i < comps.Count(); i++)
            {
                var comp = comps.ElementAt(i);
                var editorType = CustomEditorAttribute.GetEditor(comp.GetType());
                if (editorType != null)
                {
                    var editor = (ScriptedEditor)Activator.CreateInstance(editorType);
                    editor.target = comp;
                    if (editor != null)
                    {
                        compEditors[comp.InstanceID] = editor;
                        compEditors[comp.InstanceID].OnEnable();
                    }
                }
            }
        }

        public override void OnDisable()
        {
            foreach (var editor in compEditors.Values)
                editor.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            var go = target as GameObject;

            ImGui.PushID(go.GetHashCode());
            // GameObject's Drawer is Hardcoded

            ImGui.SameLine();

            bool isEnabled = go.Enabled;
            ImGui.Checkbox("##GOActive", ref isEnabled);
            if (isEnabled != go.Enabled)
                go.Enabled = isEnabled;
            GUIHelper.Tooltip("Is Enabled");

            ImGui.SameLine();

            string name = go.Name;
            ImGui.SetNextItemWidth(ImGui.GetWindowWidth() - 100);
            if (ImGui.InputText("##GOName", ref name, 0x100))
                go.Name = name;

            //ImGui.SameLine();

            //if (ImGui.Button(go.isStatic ? FontAwesome6.Lock : FontAwesome6.Unlock))
            //    go.isStatic = !go.isStatic;
            //GUIHelper.Tooltip("Is Static");

            ImGui.SetNextItemWidth((ImGui.GetWindowWidth() / 2) - (50));
            ImGui.Combo("Tag", ref go.tagIndex, TagLayerManager.tags.ToArray(), TagLayerManager.tags.Count);
            ImGui.SameLine();
            ImGui.SetNextItemWidth((ImGui.GetWindowWidth() / 2) - (50));
            ImGui.Combo("Layer", ref go.layerIndex, TagLayerManager.layers.ToArray(), TagLayerManager.layers.Count);

            Space();

            if (ImGui.CollapsingHeader(FontAwesome6.LocationArrow + "  Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                PropertyDrawer.Draw(go, typeof(GameObject).GetProperty("Position")!);
                PropertyDrawer.Draw(go, typeof(GameObject).GetProperty("Rotation")!);
                PropertyDrawer.Draw(go, typeof(GameObject).GetProperty("Scale")!);
            }

            Space();

            // Draw Components
            HashSet<int> editorsNeeded = new();
            var components = go.GetComponents<MonoBehaviour>().ToList();
            for (int i = 0; i < components.Count; i++)
            {
                var comp = components[i];
                editorsNeeded.Add(comp.InstanceID);

                if (comp.hideFlags.HasFlag(HideFlags.Hide) || comp.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;

                if (comp.hideFlags.HasFlag(HideFlags.NotEditable)) ImGui.BeginDisabled();

                ImGui.PushID(comp.GetHashCode() + i);
                if (ImGui.CollapsingHeader(comp.GetType().Name, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.OpenOnArrow))
                {
                    if (ImGui.BeginPopupContextItem())
                    {
                        if (ImGui.MenuItem("Duplicate"))
                        {
                            var copy = JsonUtility.Deserialize(JsonUtility.Serialize(comp), comp.GetType());
                            go.AddComponentDirectly(copy as MonoBehaviour);
                        }
                        if (ImGui.MenuItem("Delete")) go.RemoveComponent(comp);
                        ImGui.EndPopup();
                    }

                    if (compEditors.TryGetValue(comp.InstanceID, out var editor))
                    {
                        editor.OnInspectorGUI();
                        continue;
                    }

                    FieldInfo[] fields = comp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    // Private fields need to have the SerializeField attribute
                    fields = fields.Where(field => field.IsPublic || Attribute.IsDefined(field, typeof(SerializeFieldAttribute))).ToArray();

                    foreach (var field in fields)
                    {
                        // Dont render if the field has the Hide attribute
                        if (!Attribute.IsDefined(field, typeof(HideInInspectorAttribute)))
                        {
                            var attributes = field.GetCustomAttributes(true);
                            var imGuiAttributes = attributes.Where(attr => attr is IImGUIAttri).Cast<IImGUIAttri>();

                            foreach (var imGuiAttribute in imGuiAttributes)
                                imGuiAttribute.Draw();

                            // enums are a special case
                            if (field.FieldType.IsEnum)
                            {
                                var currentEnumValue = (Enum)field.GetValue(comp);

                                if (ImGui.BeginCombo(field.FieldType.Name, currentEnumValue.ToString()))
                                {
                                    foreach (var enumValue in Enum.GetValues(field.FieldType))
                                    {
                                        bool isSelected = currentEnumValue.Equals(enumValue);

                                        if (ImGui.Selectable(enumValue.ToString(), isSelected))
                                            field.SetValue(comp, enumValue);

                                        if (isSelected)
                                            ImGui.SetItemDefaultFocus();
                                    }

                                    ImGui.EndCombo();
                                }
                            }
                            else
                            {
                                // Draw the field using PropertyDrawer.Draw
                                PropertyDrawer.Draw(comp, field);
                            }

                            foreach (var imGuiAttribute in imGuiAttributes)
                                imGuiAttribute.End();
                        }
                    }

                    // Draw any Buttons
                    ImGUIButtonAttribute.DrawButtons(comp);
                }
                ImGui.PopID();

                if (comp.hideFlags.HasFlag(HideFlags.NotEditable)) ImGui.EndDisabled();

                Space();
            }

            // Remove any editors that are no longer needed
            var keys = compEditors.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (!editorsNeeded.Contains(key))
                {
                    compEditors[key].OnDisable();
                    compEditors.Remove(key);
                }
            }


            Space();Space();
            Space();Space();

            if (ImGui.Button("Add Component", new System.Numerics.Vector2(ImGui.GetWindowWidth() - 15f, 25f)))
                ImGui.OpenPopup("AddComponentContextMenu");

            if (ImGui.BeginPopup("AddComponentContextMenu"))
            {
                float cursorPosX = ImGui.GetCursorPosX();
                GUIHelper.Search("##searchBox", ref _searchText, ImGui.GetContentRegionAvail().X);

                if (rootMenuItem == null)
                    rootMenuItem = GetAddComponentMenuItems();

                DrawMenuItems(rootMenuItem, go);

                ImGui.EndPopup();
            }
            ImGui.PopID();
        }

        public struct XBounds
        {
            public float start;
            public float end;
        }

        public static XBounds CalculateSectionBoundsX(float padding)
        {
            float windowStart = ImGui.GetWindowSize().X;
            float windowPadding = ImGui.GetStyle().WindowPadding.X;

            return new XBounds
            {
                start = windowStart + windowPadding + padding,
                end = windowStart + windowStart - windowPadding - padding
            };
        }

        public static bool BeginSection(string title)
        {
            ImGui.GetWindowDrawList().ChannelsSplit(2);

            // Draw content above the rectangle
            ImGui.GetWindowDrawList().ChannelsSetCurrent(1);

            var padding = ImGui.GetStyle().WindowPadding;

            float windowWidth = ImGui.GetWindowSize().X;

            var boundsX = CalculateSectionBoundsX(padding.X);

            // Title will be clipped till the middle
            // because I am going to have a collapsing
            // header there
            float midPoint = boundsX.start + (boundsX.end - boundsX.start) / 2.0f;

            // Start from padding position
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + padding.Y);
            ImGui.BeginGroup();
            if (padding.X > 0)
            {
                ImGui.Indent(padding.X);
            }

            //ImGui.PushClipRect(new Vector2(boundsX.start, window.ClipRect.Min.Y),
            //                   new Vector2(midPoint, window.ClipRect.Max.Y), false);
            ImGui.TextUnformatted(title);
            ImGui.PopClipRect();

            // Setting clip rectangle for the group contents;
            // so, that text does not overflow outside this widget
            // the parent window is resized
            //ImGui.PushClipRect(new Vector2(boundsX.start, window.ClipRect.Min.Y),
            //                   new Vector2(boundsX.end, window.ClipRect.Max.Y), false);
            //
            return true;
        }

        public static void EndSection()
        {
            var padding = ImGui.GetStyle().WindowPadding;

            ImGui.PopClipRect();
            if (padding.X > 0)
            {
                ImGui.Unindent(padding.X);
            }
            ImGui.EndGroup();

            // Essentially, the content is drawn with padding
            // while the rectangle is drawn without padding
            var boundsX = CalculateSectionBoundsX(0.0f);

            // GetItemRectMin is going to include the padding
            // as well; so, remove it
            var panelMin = new Vector2(boundsX.start, ImGui.GetItemRectMin().Y - padding.Y);
            var panelMax = new Vector2(boundsX.end, ImGui.GetItemRectMax().Y + padding.Y);

            // Draw rectangle below
            ImGui.GetWindowDrawList().ChannelsSetCurrent(0);
            ImGui.GetWindowDrawList().AddRectFilled(panelMin, panelMax,
                ImGui.GetColorU32(ImGuiCol.ChildBg),
                ImGui.GetStyle().ChildRounding);
            ImGui.GetWindowDrawList().ChannelsMerge();

            // Since the rectangle is bigger than the box, move the cursor;
            // so, it starts outside the box
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + padding.Y);

            // Then, add default spacing
            ImGui.Spacing();
        }

        public void DrawMenuItems(MenuItemInfo menuItem, GameObject go)
        {
            foreach (var item in menuItem.Children)
            {
                if (string.IsNullOrEmpty(_searchText) == false && (item.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) == false || item.Type == null))
                {
                    DrawMenuItems(item, go);
                    continue;
                }


                if (item.Type != null)
                {
                    if (ImGui.MenuItem(item.Name))
                        go.AddComponent(item.Type);
                }
                else
                {
                    if (ImGui.BeginMenu(item.Name, true))
                    {
                        DrawMenuItems(item, go);
                        ImGui.EndMenu();
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
            MenuItemInfo root = new MenuItemInfo
            {
                Name = "Root"
            };

            foreach (var (path, type) in items)
            {
                string[] parts = path.Split('/');
                MenuItemInfo currentNode = root;

                for (int i = 0; i < parts.Length - 1; i++)  // Skip the last part
                {
                    string part = parts[i];

                    MenuItemInfo childNode = currentNode.Children.Find(c => c.Name == part);

                    if (childNode == null)
                    {
                        childNode = new MenuItemInfo
                        {
                            Name = part
                        };

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

        public class MenuItemInfo
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

    }
}
