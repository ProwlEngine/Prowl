using ImageMagick;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;

namespace Prowl.Editor
{

    public class HierarchyWindow : EditorWindow
    {
        const double entryHeight = 30;
        const double entryPadding = 4;

        private string _searchText = "";
        private GameObject? m_RenamingGO = null;
        public static SelectHandler<WeakReference> SelectHandler { get; private set; } = new((item) => !item.IsAlive || (item.Target is EngineObject eObj && eObj.IsDestroyed), (a, b) => ReferenceEquals(a.Target, b.Target));

        private const float PingDuration = 3f;
        private static float pingTimer = 0;
        private static WeakReference pingedGO;

        public HierarchyWindow() : base()
        {
            Title = FontAwesome6.FolderTree + " Hierarchy";
            SelectHandler.OnSelectObject += (obj) => {
                // Reset ping timer on selection changed
                pingTimer = 0;
                pingedGO = null;
            };
        }

        public static void Ping(GameObject go)
        {
            pingTimer = PingDuration;
            pingedGO = new WeakReference(go);
        }

        protected override void Draw()
        {
            pingTimer -= Time.deltaTimeF;
            if (pingTimer < 0) pingTimer = 0;

            SelectHandler.StartFrame();

            gui.CurrentNode.Layout(LayoutType.Column);
            gui.CurrentNode.ScaleChildren();
            gui.CurrentNode.Padding(0, 10, 10, 10);


            using (gui.Node("Search").Width(Size.Percentage(1f)).MaxHeight(entryHeight).Clip().Enter())
            {
                gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f, -entryHeight), entryHeight);

                using (gui.Node("CreateGOBtn").Left(Offset.Percentage(1f, -entryHeight + 3)).Scale(entryHeight).Enter())
                {
                    gui.Draw2D.DrawText(FontAwesome6.CirclePlus, 30, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base4);

                    if (gui.IsNodePressed())
                        gui.OpenPopup("CreateGameObject");

                    var test = gui.CurrentNode;
                    if (gui.BeginPopup("CreateGameObject", out var node))
                    {
                        using (node.Width(150).Layout(LayoutType.Column).Spacing(5).Padding(5).FitContentHeight().Enter())
                        {
                            DrawContextMenu(null, test);
                        }
                    }
                }

            }


            using (gui.Node("Tree").Width(Size.Percentage(1f)).MarginTop(5).Clip().Enter())
            {
                //gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 4);

                var dropInteract = gui.GetInteractable();
                HandleDrop(null);

                if (!SelectHandler.SelectedThisFrame && dropInteract.TakeFocus())
                    SelectHandler.Clear();

                if(IsFocused)
                    if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                        DuplicateSelected();

                if (gui.IsPointerClick(Silk.NET.Input.MouseButton.Right) && dropInteract.IsHovered())
                {
                    // POpup holder is our parent, since thats the Tree node
                    gui.OpenPopup("RightClickGameObject");
                    gui.SetGlobalStorage("RightClickGameObject", -1);
                }

                double height = 0;
                int id = 0;
                for (int i = 0; i < SceneManager.AllGameObjects.Count; i++)
                {
                    var go = SceneManager.AllGameObjects[i];
                    if (go.parent == null)
                        DrawGameObject(ref id, go, 0, false);
                    height += entryHeight;
                }

                var popupHolder = gui.CurrentNode;
                if (gui.BeginPopup("RightClickGameObject", out var node))
                {
                    using (node.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                    {
                        var instanceID = gui.GetGlobalStorage<int>("RightClickGameObject");
                        GameObject? go = null;
                        if(instanceID != -1)
                            go = EngineObject.FindObjectByID<GameObject>(instanceID);
                        DrawContextMenu(go, popupHolder);
                    }
                }

                gui.ScrollV();
            }
        }

        private void DrawContextMenu(GameObject? parent, LayoutNode popupHolder)
        {
            EditorGUI.Text("Create");

            bool closePopup = false;
            if (EditorGUI.StyledButton("New GameObject"))
            {
                var go = new GameObject("New GameObject");
                if(parent != null)
                    go.SetParent(parent);
                go.Transform.localPosition = Vector3.zero;
                SelectHandler.SetSelection(new WeakReference(go));
                closePopup = true;
            }

            closePopup |= MenuItem.DrawMenuRoot("Template");

            if (parent != null)
            {
                EditorGUI.Separator();
                EditorGUI.Text("GameObject");

                SelectHandler.SelectIfNot(new WeakReference(parent));
                if (EditorGUI.StyledButton("Rename"))
                {
                    m_RenamingGO = parent;
                    closePopup = true;
                }
                if (EditorGUI.StyledButton("Duplicate"))
                {
                    DuplicateSelected();
                    closePopup = true;
                }
                if (EditorGUI.StyledButton("Delete"))
                {
                    parent.Destroy();
                    closePopup = true;
                }

                if (SelectHandler.Count > 1 && EditorGUI.StyledButton("Delete All"))
                {
                    SelectHandler.Foreach((go) => {
                        (go.Target as GameObject).Destroy();
                    });
                    SelectHandler.Clear();
                    closePopup = true;
                }

                if (SelectHandler.Count > 0 && EditorGUI.StyledButton("Align With View"))
                {
                    SelectHandler.Foreach((go) => {
                        Camera cam = SceneViewWindow.LastFocusedCamera;
                        (go.Target as GameObject).Transform.position = cam.GameObject.Transform.position;
                        (go.Target as GameObject).Transform.rotation = cam.GameObject.Transform.rotation;
                    });
                    closePopup = true;
                }

                if (SelectHandler.Count == 1 && EditorGUI.StyledButton("Align View With"))
                {
                    Camera cam = SceneViewWindow.LastFocusedCamera;
                    cam.GameObject.Transform.position = parent.Transform.position;
                    cam.GameObject.Transform.rotation = parent.Transform.rotation;
                    SceneViewWindow.SetCamera(parent.Transform.position, parent.Transform.rotation);
                    closePopup = true;
                }
            }

            if (parent == null)
            {
                EditorGUI.Separator();
                EditorGUI.Text("Scene");
                closePopup |= MenuItem.DrawMenuRoot("Scene");
            }

            if (closePopup)
                gui.ClosePopup(popupHolder);
        }

        public void DrawGameObject(ref int index, GameObject entity, uint depth, bool isPartOfPrefab)
        {
            if (entity == null) return;
            if (entity.hideFlags.HasFlag(HideFlags.Hide) || entity.hideFlags.HasFlag(HideFlags.HideAndDontSave)) return;

            if (!string.IsNullOrEmpty(_searchText) && !entity.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < entity.children.Count; i++)
                    DrawGameObject(ref index, entity.children[i], depth, isPartOfPrefab);
                return;
            }

            bool drawChildren = false;
            bool isPrefab = entity.IsPrefab;
            double left = depth * entryHeight;
            ulong goNodeID = 0;
            using (gui.Node(entity.GetHashCode().ToString()).Left(left).Top(index * (entryHeight + entryPadding)).ExpandWidth(-(left + gui.VScrollBarWidth())).Height(entryHeight).Margin(2, 0).Enter())
            {
                goNodeID = gui.CurrentNode.ID;
                float colMult = entity.enabledInHierarchy ? 1 : 0.5f;
                bool isSelected = SelectHandler.IsSelected(new WeakReference(entity));

                double maxwidth = gui.CurrentNode.LayoutData.InnerRect.width;
                var rect = gui.CurrentNode.LayoutData.InnerRect;
                rect.width = maxwidth;
                rect.height = entryHeight;

                // Interaction
                SelectHandler.AddSelectableAtIndex(index, new WeakReference(entity));
                var interact = gui.GetInteractable(rect);
                if (interact.TakeFocus())
                    SelectHandler.Select(index, new WeakReference(entity));

                bool justStartedRename = false;
                if (SelectHandler.Count == 1 && gui.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left) && interact.IsHovered())
                {
                    justStartedRename = true;
                    m_RenamingGO = entity;
                }
                else if (gui.IsPointerClick(Silk.NET.Input.MouseButton.Right) && interact.IsHovered())
                {
                    // POpup holder is our parent, since thats the Tree node
                    gui.OpenPopup("RightClickGameObject", null, gui.CurrentNode.Parent);
                    gui.SetGlobalStorage("RightClickGameObject", entity.InstanceID);
                }

                if (IsFocused)
                    if (isSelected && Input.GetKeyDown(Key.Delete))
                        entity.Destroy();

                // Drag n Drop
                // Dropping uses the current nodes rect by default
                HandleDrop(entity);
                DragnDrop.Drag(entity);

                var col = (interact.IsHovered() ? GuiStyle.Base5 : GuiStyle.Base4 * 0.8f) * colMult;
                gui.Draw2D.DrawRectFilled(rect, (isSelected ? GuiStyle.Indigo : col), 8);
                gui.Draw2D.DrawRectFilled(rect.Min, new Vector2(entryHeight, entryHeight), GuiStyle.Borders, 8, 9);
                if (isPrefab || isPartOfPrefab || !entity.enabledInHierarchy)
                {
                    var lineColor = (isPrefab ? GuiStyle.Orange : GuiStyle.Yellow);
                    if(!entity.enabledInHierarchy)
                        lineColor = GuiStyle.Red;
                    gui.Draw2D.DrawLine(new Vector2(rect.x + entryHeight + 1, rect.y - 1), new Vector2(rect.x + entryHeight + 1, rect.y + entryHeight - 1), lineColor, 3);
                }

                using (gui.Node("VisibilityBtn").TopLeft(6).Scale(20).Enter())
                {
                    if (gui.IsNodePressed())
                        entity.enabled = !entity.enabled;
                    gui.Draw2D.DrawText(entity.enabled ? FontAwesome6.Eye : FontAwesome6.EyeSlash, 20, gui.CurrentNode.LayoutData.Rect, entity.enabledInHierarchy ? GuiStyle.Base11 : GuiStyle.Base4);
                }

                // if were pinging we need to open the tree to the pinged object
                if (pingTimer > 0 && pingedGO != null && pingedGO.Target is GameObject go)
                {
                    if (entity.IsParentOf(go)) // Set the tree open
                        gui.SetStorage(entity.InstanceID.ToString(), true);
                    else if (entity.InstanceID == go.InstanceID)
                    {
                        // Draw a ping effect
                        // TODO: Scroll to Rect
                        var pingRect = rect;
                        pingRect.Expand(MathF.Sin(pingTimer) * 6f);
                        gui.Draw2D.DrawRect(pingRect, GuiStyle.Yellow, 2f, 4f);
                    }
                }

                if (entity.children.Count > 0)
                {
                    bool expanded = gui.GetNodeStorage<bool>(entity.InstanceID.ToString());
                    using (gui.Node("VisibilityBtn").TopLeft(maxwidth - entryHeight, 5).Scale(20).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            expanded = !expanded;
                            gui.SetNodeStorage(gui.CurrentNode.Parent, entity.InstanceID.ToString(), expanded);
                        }
                        gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.Rect, entity.enabledInHierarchy ? GuiStyle.Base11 : GuiStyle.Base4);
                    }
                    drawChildren = expanded;
                }

                // Name
                var name = entity.Name;
                if (m_RenamingGO == entity)
                {
                    var inputRect = new Rect(rect.x + 33, rect.y + 4, maxwidth - (entryHeight * 2.25), 30 - 8);
                    gui.Draw2D.DrawRectFilled(inputRect, GuiStyle.WindowBackground, 8);
                    gui.InputField("RenameInput", ref name, 64, Gui.InputFieldFlags.None, 30, 0, maxwidth - (entryHeight * 2.25), null, null, true);
                    if (justStartedRename)
                        gui.FocusPreviousInteractable();
                    if (!gui.PreviousInteractableIsFocus())
                        m_RenamingGO = null;
                    entity.Name = name;
                    justStartedRename = false;
                }
                else
                {
                    var textRect = rect;
                    textRect.width -= entryHeight;
                    gui.Draw2D.DrawText(UIDrawList.DefaultFont, name, 20, new Vector2(rect.x + 40, rect.y + 7), GuiStyle.Base11, 0, textRect);
                }

                index++;
            }

            // Open
            if (drawChildren)
            {
                gui.PushID(goNodeID);
                for (int i = 0; i < entity.children.Count; i++)
                    DrawGameObject(ref index, entity.children[i], depth + 1, isPartOfPrefab || isPrefab);
                gui.PopID();
            }
        }

        private void HandleDrop(GameObject? entity)
        {
            if (DragnDrop.Drop<GameObject>(out var original))
            {
                GameObject go = original;
                if (!SceneManager.Has(original)) // If its not already in the scene, Instantiate it
                    go = (GameObject)EngineObject.Instantiate(original, true);
                go.SetParent(entity); // null is root
                SelectHandler.SetSelection(new WeakReference(go));
            }
            else if (DragnDrop.Drop<Prefab>(out var prefab))
            {
                SelectHandler.SetSelection(new WeakReference(prefab.Instantiate()));
            }
            else if (DragnDrop.Drop<Scene>(out var scene))
            {
                SceneManager.LoadScene(scene);
            }
        }

        public static void DuplicateSelected()
        {
            var newGO = new List<WeakReference>();
            SelectHandler.Foreach((go) => {
                // Duplicating, Easiest way to duplicate is to Serialize then Deserialize
                var serialized = Serializer.Serialize(go.Target);
                var deserialized = Serializer.Deserialize<GameObject>(serialized);
                deserialized.SetParent((go.Target as GameObject).parent);
                newGO.Add(new WeakReference(deserialized));
            });
            SelectHandler.Clear();
            SelectHandler.SetSelection(newGO.ToArray());
        }

    }
}