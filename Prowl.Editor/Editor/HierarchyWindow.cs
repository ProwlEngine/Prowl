// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

public class HierarchyWindow : EditorWindow
{
    double entryHeight => (float)EditorStylePrefs.Instance.ItemSize;
    const double entryPadding = 4;

    private string _searchText = "";
    private GameObject? m_RenamingGO;
    public static SelectHandler<WeakReference> SelectHandler { get; private set; } = new((item) => !item.IsAlive || (item.Target is EngineObject eObj && eObj.IsDestroyed), (a, b) => ReferenceEquals(a.Target, b.Target));

    private const float PingDuration = 3f;
    private static float pingTimer;
    private static WeakReference? pingedGO;
    private bool justStartedRename;

    public HierarchyWindow() : base()
    {
        Title = FontAwesome6.FolderTree + " Hierarchy";
        SelectHandler.OnSelectObject += (obj) =>
        {
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
                gui.Draw2D.DrawText(FontAwesome6.CirclePlus, 30, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? Color.white : EditorStylePrefs.Instance.LesserText);

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


        using (gui.Node("Tree").Width(Size.Percentage(1f)).MarginTop(5).Clip().Scroll().Enter())
        {
            //gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 4);

            var dropInteract = gui.GetInteractable();
            HandleDrop(null);

            if (!SelectHandler.SelectedThisFrame && dropInteract.TakeFocus())
                SelectHandler.Clear();

            if (IsFocused)
                if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                    DuplicateSelected();

            if (gui.IsPointerClick(MouseButton.Right) && dropInteract.IsHovered())
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
                    if (instanceID != -1)
                        go = EngineObject.FindObjectByID<GameObject>(instanceID);
                    DrawContextMenu(go, popupHolder);
                }
            }
        }
    }

    private void DrawContextMenu(GameObject? parent, LayoutNode popupHolder)
    {
        EditorGUI.Text("Create");

        bool closePopup = false;
        if (EditorGUI.StyledButton("New GameObject"))
        {
            var go = new GameObject("New GameObject");
            if (parent != null)
                go.SetParent(parent);
            go.Transform.localPosition = Vector3.zero;
            SelectHandler.SetSelection(new WeakReference(go));
            closePopup = true;
        }

        closePopup |= MenuItem.DrawMenuRoot("Create");

        if (parent != null)
        {
            EditorGUI.Separator();
            EditorGUI.Text("GameObject");

            SelectHandler.SelectIfNot(new WeakReference(parent));
            if (EditorGUI.StyledButton("Rename"))
            {
                m_RenamingGO = parent;
                justStartedRename = true;
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
                SelectHandler.Foreach((go) =>
                {
                    (go.Target as GameObject).Destroy();
                });
                SelectHandler.Clear();
                closePopup = true;
            }

            if (SelectHandler.Count > 0 && EditorGUI.StyledButton("Align With View"))
            {
                SelectHandler.Foreach((go) =>
                {
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
        double width = gui.CurrentNode.LayoutData.InnerRect.width - left;
        width = Math.Max(width, 200);
        using (gui.Node(entity.GetHashCode().ToString()).Left(left).Top(index * (entryHeight + entryPadding)).Width(width).Height(entryHeight).Margin(2, 0).Enter())
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
            if (interact.TakeFocus(true))
                SelectHandler.Select(index, new WeakReference(entity));

            if (SelectHandler.Count == 1 && gui.IsPointerDoubleClick(MouseButton.Left) && interact.IsHovered())
            {
                justStartedRename = true;
                m_RenamingGO = entity;
            }
            else if (gui.IsPointerClick(MouseButton.Right) && interact.IsHovered())
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

            var col = (interact.IsHovered() ? EditorStylePrefs.Instance.Hovering : Color.white * 0.5f) * colMult;
            gui.Draw2D.DrawRectFilled(rect, (isSelected ? EditorStylePrefs.Instance.Highlighted : col), (float)EditorStylePrefs.Instance.ButtonRoundness);
            gui.Draw2D.DrawRectFilled(rect.Min, new Vector2(entryHeight, entryHeight), EditorStylePrefs.Instance.Borders, (float)EditorStylePrefs.Instance.ButtonRoundness, 9);
            if (isPrefab || isPartOfPrefab || !entity.enabledInHierarchy)
            {
                var lineColor = (isPrefab ? EditorStylePrefs.Orange : EditorStylePrefs.Yellow);
                if (!entity.enabledInHierarchy)
                    lineColor = EditorStylePrefs.Instance.Warning;
                gui.Draw2D.DrawLine(new Vector2(rect.x + entryHeight + 1, rect.y - 1), new Vector2(rect.x + entryHeight + 1, rect.y + entryHeight - 1), lineColor, 3);
            }

            using (gui.Node("VisibilityBtn").TopLeft(1).Scale(entryHeight).Enter())
            {
                if (gui.IsNodePressed())
                    entity.enabled = !entity.enabled;
                gui.Draw2D.DrawText(entity.enabled ? FontAwesome6.Eye : FontAwesome6.EyeSlash, 20, gui.CurrentNode.LayoutData.Rect, entity.enabledInHierarchy ? Color.white : Color.white * (float)EditorStylePrefs.Instance.Disabled);
            }

            // if were pinging we need to open the tree to the pinged object
            if (pingTimer > 0 && pingedGO != null && pingedGO.Target is GameObject go)
            {
                if (entity.IsParentOf(go)) // Set the tree open
                    gui.SetNodeStorage(entity.InstanceID.ToString(), true);
                else if (entity.InstanceID == go.InstanceID)
                {
                    // Draw a ping effect
                    // TODO: Scroll to Rect
                    var pingRect = rect;
                    pingRect.Expand(MathF.Sin(pingTimer) * 6f);
                    gui.Draw2D.DrawRect(pingRect, EditorStylePrefs.Yellow, 2f, 4f);
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
                    gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.Rect, entity.enabledInHierarchy ? Color.white : Color.white * (float)EditorStylePrefs.Instance.Disabled);
                }
                drawChildren = expanded;
            }

            // Name
            var name = entity.Name;
            if (m_RenamingGO == entity)
            {
                var inputRect = new Rect(rect.x + 33, rect.y, maxwidth - (entryHeight * 2.25), entryHeight);
                gui.Draw2D.DrawRectFilled(inputRect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.ButtonRoundness);
                gui.InputField("RenameInput", ref name, 64, Gui.InputFieldFlags.None, 30, 0, maxwidth - (entryHeight * 2.25), entryHeight, EditorGUI.GetInputStyle(), true);
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
                var textSizeY = Font.DefaultFont.CalcTextSize(name, 20).y;
                var centerY = rect.y + (rect.height / 2) - (textSizeY / 2);
                gui.Draw2D.DrawText(Font.DefaultFont, name, 20, new Vector2(rect.x + 40, centerY + 3), Color.white, 0, textRect);
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
        SelectHandler.Foreach((go) =>
        {
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
