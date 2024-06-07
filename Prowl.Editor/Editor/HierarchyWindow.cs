using Prowl.Editor.Preferences;
using Prowl.Editor;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Input;
using Hexa.NET.ImGui;
using Prowl.Editor.EditorWindows;
using Prowl.Runtime.GUI.Layout;

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

            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.ScaleChildren();
            g.CurrentNode.Padding(0, 10, 10, 10);


            using (g.Node("Search").Width(Size.Percentage(1f)).MaxHeight(entryHeight).Clip().Enter())
            {
                g.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f, -entryHeight), entryHeight);

                using (g.Node("CreateGOBtn").Left(Offset.Percentage(1f, -entryHeight + 3)).Scale(entryHeight).Enter())
                {
                    g.Draw2D.DrawText(FontAwesome6.CirclePlus, 30, g.CurrentNode.LayoutData.Rect, g.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base4);

                    if (g.IsNodePressed())
                        g.OpenPopup("CreateGameObject");

                    var test = g.CurrentNode;
                    if (g.BeginPopup("CreateGameObject", out var node))
                    {
                        using (node.Width(150).Layout(LayoutType.Column).FitContentHeight().Enter())
                        {
                            DrawContextMenu(null, test);
                        }
                    }
                }

            }


            using (g.Node("Tree").Width(Size.Percentage(1f)).MarginTop(5).Clip().Enter())
            {
                //g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.5f, 10, 12);

                var dropInteract = g.GetInteractable();
                HandleDrop(null);

                if (!SelectHandler.SelectedThisFrame && dropInteract.TakeFocus())
                    SelectHandler.Clear();

                if(IsFocused)
                    if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                        DuplicateSelected();


                double height = 0;
                int id = 0;
                for (int i = 0; i < SceneManager.AllGameObjects.Count; i++)
                {
                    var go = SceneManager.AllGameObjects[i];
                    if (go.parent == null)
                        DrawGameObject(ref id, go, 0, false);
                    height += entryHeight;
                }

                var popupHolder = g.CurrentNode;
                if (g.BeginPopup("RightClickGameObject", out var node))
                {
                    using (node.Width(150).Layout(LayoutType.Column).FitContentHeight().Enter())
                    {
                        var instanceID = g.GetGlobalStorage<int>("RightClickGameObject");
                        var go = EngineObject.FindObjectByID<GameObject>(instanceID);
                        DrawContextMenu(go, popupHolder);
                    }
                }

                g.ScrollV();
            }
        }

        private void DrawContextMenu(GameObject? parent, LayoutNode popupHolder)
        {
            bool closePopup = false;
            if (closePopup |= EditorGUI.QuickButton("New GameObject"))
            {
                var go = new GameObject("New GameObject");
                if(parent != null)
                    go.SetParent(parent);
                go.Transform.localPosition = Vector3.zero;
                SelectHandler.SetSelection(new WeakReference(go));
            }

            if(parent != null)
            {
                SelectHandler.SelectIfNot(new WeakReference(parent));
                if (closePopup |= EditorGUI.QuickButton("Rename"))
                    m_RenamingGO = parent;
                if (closePopup |= EditorGUI.QuickButton("Duplicate"))
                    DuplicateSelected();
                if (closePopup |= EditorGUI.QuickButton("Delete"))
                    parent.Destroy();

                if (SelectHandler.Count > 1 && (closePopup |= EditorGUI.QuickButton("Delete All")))
                {
                    SelectHandler.Foreach((go) => {
                        (go.Target as GameObject).Destroy();
                    });
                    SelectHandler.Clear();
                }

                if (SelectHandler.Count > 0 && (closePopup |= EditorGUI.QuickButton("Align With View")))
                {
                    SelectHandler.Foreach((go) => {
                        Camera cam = SceneViewWindow.LastFocusedCamera;
                        (go.Target as GameObject).Transform.position = cam.GameObject.Transform.position;
                        (go.Target as GameObject).Transform.rotation = cam.GameObject.Transform.rotation;
                    });
                }

                if (SelectHandler.Count == 1 && (closePopup |= EditorGUI.QuickButton("Align View With")))
                {
                    Camera cam = SceneViewWindow.LastFocusedCamera;
                    cam.GameObject.Transform.position = parent.Transform.position;
                    cam.GameObject.Transform.rotation = parent.Transform.rotation;
                    SceneViewWindow.SetCamera(parent.Transform.position, parent.Transform.rotation);
                }
            }

            MenuItem.DrawMenuRoot("Template");

            if (closePopup)
                g.ClosePopup(popupHolder);
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
            using (g.Node(entity.GetHashCode().ToString()).Left(left).Top(index * (entryHeight + entryPadding)).ExpandWidth(-(left + g.VScrollBarWidth())).Height(entryHeight).Margin(2, 0).Enter())
            {
                goNodeID = g.CurrentNode.ID;
                float colMult = entity.enabledInHierarchy ? 1 : 0.5f;
                bool isSelected = SelectHandler.IsSelected(new WeakReference(entity));

                double maxwidth = g.CurrentNode.LayoutData.InnerRect.width;
                var rect = g.CurrentNode.LayoutData.InnerRect;
                rect.width = maxwidth;
                rect.height = entryHeight;

                // Interaction
                var interact = g.GetInteractable(rect);
                if (interact.TakeFocus())
                    SelectHandler.HandleSelectable(index++, new WeakReference(entity), true);

                bool justStartedRename = false;
                if (SelectHandler.Count == 1 && g.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left) && interact.IsHovered())
                {
                    justStartedRename = true;
                    m_RenamingGO = entity;
                }
                else if (g.IsPointerClick(Silk.NET.Input.MouseButton.Right) && interact.IsHovered())
                {
                    // POpup holder is our parent, since thats the Tree node
                    g.OpenPopup("RightClickGameObject", null, g.CurrentNode.Parent);
                    g.SetGlobalStorage("RightClickGameObject", entity.InstanceID);
                }

                if (IsFocused)
                    if (isSelected && Input.GetKeyDown(Key.Delete))
                        entity.Destroy();

                // Drag n Drop
                // Dropping uses the current nodes rect by default
                HandleDrop(entity);
                DragnDrop.Drag(entity);

                var col = (interact.IsHovered() ? GuiStyle.Base5 : GuiStyle.Base4 * 0.8f) * colMult;
                g.Draw2D.DrawRectFilled(rect, (isSelected ? GuiStyle.Indigo : col), 8);
                g.Draw2D.DrawRectFilled(rect.Min, new Vector2(entryHeight, entryHeight), GuiStyle.Borders, 8, 9);
                if (isPrefab || isPartOfPrefab || !entity.enabledInHierarchy)
                {
                    var lineColor = (isPrefab ? GuiStyle.Orange : GuiStyle.Yellow);
                    if(!entity.enabledInHierarchy)
                        lineColor = GuiStyle.Red;
                    g.Draw2D.DrawLine(new Vector2(rect.x + entryHeight + 1, rect.y - 1), new Vector2(rect.x + entryHeight + 1, rect.y + entryHeight - 1), lineColor, 3);
                }

                using (g.Node("VisibilityBtn").TopLeft(6).Scale(20).Enter())
                {
                    if (g.IsNodePressed())
                        entity.enabled = !entity.enabled;
                    g.Draw2D.DrawText(entity.enabled ? FontAwesome6.Eye : FontAwesome6.EyeSlash, 20, g.CurrentNode.LayoutData.Rect, entity.enabledInHierarchy ? GuiStyle.Base11 : GuiStyle.Base4);
                }

                // if were pinging we need to open the tree to the pinged object
                if (pingTimer > 0 && pingedGO != null && pingedGO.Target is GameObject go)
                {
                    if (entity.IsParentOf(go)) // Set the tree open
                        g.SetStorage(entity.InstanceID.ToString(), true);
                    else if (entity.InstanceID == go.InstanceID)
                    {
                        // Draw a ping effect
                        // TODO: Scroll to Rect
                        //if (pingTimer > PingDuration - 1f)
                        //    ImGui.ScrollToItem(ImGuiScrollFlags.None);
                        var pingRect = rect;
                        pingRect.Expand(MathF.Sin(pingTimer) * 6f);
                        g.Draw2D.DrawRect(pingRect, GuiStyle.Yellow, 2f, 4f);
                    }
                }

                if (entity.children.Count > 0)
                {
                    bool expanded = g.GetStorage<bool>(entity.InstanceID.ToString());
                    using (g.Node("VisibilityBtn").TopLeft(maxwidth - entryHeight, 5).Scale(20).Enter())
                    {
                        if (g.IsNodePressed())
                        {
                            expanded = !expanded;
                            g.SetStorage(g.CurrentNode.Parent, entity.InstanceID.ToString(), expanded);
                        }
                        g.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, g.CurrentNode.LayoutData.Rect, entity.enabledInHierarchy ? GuiStyle.Base11 : GuiStyle.Base4);
                    }
                    drawChildren = expanded;
                }

                // Name
                var name = entity.Name;
                if (m_RenamingGO == entity)
                {
                    var inputRect = new Rect(rect.x + 33, rect.y + 4, maxwidth - (entryHeight * 2.25), 21);
                    g.Draw2D.DrawRectFilled(inputRect, GuiStyle.WindowBackground, 8);
                    g.InputField("RenameInput", ref name, 64, Gui.InputFieldFlags.None, 30, 3, maxwidth - (entryHeight * 2.25), null, null, true);
                    if (justStartedRename)
                        g.FocusPreviousInteractable();
                    if (!g.PreviousInteractableIsFocus())
                        m_RenamingGO = null;
                    entity.Name = name;
                }
                else
                {
                    var textRect = rect;
                    textRect.width -= entryHeight;
                    g.Draw2D.DrawText(UIDrawList.DefaultFont, name, 20, new Vector2(rect.x + 40, rect.y + 7), GuiStyle.Base11, 0, textRect);
                }

                index++;
            }

            // Open
            if (drawChildren)
            {
                g.PushID(goNodeID);
                for (int i = 0; i < entity.children.Count; i++)
                    DrawGameObject(ref index, entity.children[i], depth + 1, isPartOfPrefab || isPrefab);
                g.PopID();
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

        public void DuplicateSelected()
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