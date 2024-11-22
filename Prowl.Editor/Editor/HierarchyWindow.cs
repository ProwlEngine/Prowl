// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Editor.Utilities;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Cloning;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor;

public class HierarchyWindow : EditorWindow
{
    private static double entryHeight => (float)EditorStylePrefs.Instance.ItemSize;
    const double EntryPadding = 1;

    private string _searchText = "";
    private GameObject? _renamingGO;
    private string _renamingText;
    public static SelectHandler<WeakReference> SelectHandler { get; private set; } = new((item) => !item.IsAlive || (item.Target is EngineObject eObj && eObj.IsDestroyed), (a, b) => ReferenceEquals(a.Target, b.Target));

    private const float PingDuration = 3f;
    private static float s_pingTimer;
    private static WeakReference? s_pingedGO;
    private bool _justStartedRename;

    public HierarchyWindow() : base()
    {
        Title = FontAwesome6.FolderTree + " Hierarchy";
        SelectHandler.OnSelectObject += (obj) =>
        {
            // Reset ping timer on selection changed
            s_pingTimer = 0;
            s_pingedGO = null;
        };
    }

    public static void Ping(GameObject go)
    {
        s_pingTimer = PingDuration;
        s_pingedGO = new WeakReference(go);
    }

    protected override void Draw()
    {
        s_pingTimer -= Time.deltaTimeF;
        if (s_pingTimer < 0) s_pingTimer = 0;

        SelectHandler.StartFrame();

        gui.CurrentNode.Layout(LayoutType.Column);
        gui.CurrentNode.ScaleChildren();
        gui.CurrentNode.Padding(0, 10, 10, 10);


        using (gui.Node("Search").Width(Size.Percentage(1f)).MaxHeight(entryHeight).Enter())
        {
            gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f, -entryHeight), entryHeight, EditorGUI.InputFieldStyle);

            using (gui.Node("CreateGOBtn").Left(Offset.Percentage(1f, -entryHeight + 3)).Scale(entryHeight).Enter())
            {
                gui.Draw2D.DrawText(FontAwesome6.CirclePlus, 30, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? Color.white : EditorStylePrefs.Instance.LesserText);

                if (gui.IsNodePressed())
                    gui.OpenPopup("CreateGameObject");

                if (gui.BeginPopup("CreateGameObject", out LayoutNode? node, false, EditorGUI.InputStyle))
                {
                    using (node!.Width(150).Layout(LayoutType.Column).Spacing(5).Padding(5).FitContentHeight().Enter())
                    {
                        DrawContextMenu(null);
                    }
                }
            }

        }


        using (gui.Node("Tree").Width(Size.Percentage(1f)).MarginTop(5).Clip().Scroll(inputstyle: EditorGUI.InputStyle).Enter())
        {
            //gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 4);

            Interactable dropInteract = gui.GetInteractable();
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
            for (int i = 0; i < SceneManager.Scene.RootObjects.Count(); i++)
            {
                GameObject go = SceneManager.Scene.RootObjects.ElementAt(i);
                DrawGameObject(ref id, go, 0);
                height += entryHeight;
            }

            if (gui.BeginPopup("RightClickGameObject", out LayoutNode? node, false, EditorGUI.InputStyle))
            {
                using (node!.Width(150).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
                {
                    int instanceID = gui.GetGlobalStorage<int>("RightClickGameObject");
                    GameObject? go = null;
                    if (instanceID != -1)
                        go = EngineObject.FindObjectByID<GameObject>(instanceID);
                    DrawContextMenu(go);
                }
            }
        }
    }

    private void DrawContextMenu(GameObject? parent)
    {
        EditorGUI.Text("Create");

        bool closePopup = false;
        if (EditorGUI.StyledButton("New GameObject"))
        {
            var go = new GameObject("New GameObject");
#warning TODO: Need a way to Select the new GameObject
            UndoRedoManager.RecordAction(new AddGameObjectToSceneAction(go, parent));
            //if (parent != null)
            //    go.SetParent(parent);
            //else // SetParent adds to scene automatically so we only need to add if it doesnt have a parent
            //    SceneManager.Scene.Add(go);
            //go.Transform.localPosition = Vector3.zero;
            //SelectHandler.SetSelection(new WeakReference(go));
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
                _renamingGO = parent;
                _renamingText = parent.Name;
                _justStartedRename = true;
                closePopup = true;
            }
            if (EditorGUI.StyledButton("Duplicate"))
            {
                DuplicateSelected();
                closePopup = true;
            }
            if (EditorGUI.StyledButton("Delete"))
            {
                UndoRedoManager.RecordAction(new DeleteGameObjectAction(parent.Identifier));
                //parent.DestroyLater();
                closePopup = true;
            }

            if (SelectHandler.Count > 1 && EditorGUI.StyledButton("Delete All"))
            {
                using (UndoRedoManager.CreateTransaction())
                {
                    SelectHandler.Foreach((go) =>
                    {
                        if (go.Target is GameObject g)
                            UndoRedoManager.RecordAction(new DeleteGameObjectAction(parent.Identifier));
                    });
                }
                SelectHandler.Clear();
                closePopup = true;
            }

            if (SelectHandler.Count > 0 && EditorGUI.StyledButton("Align With View"))
            {
                SelectHandler.Foreach((go) =>
                {
                    if (go.Target is GameObject g)
                    {
                        Camera cam = SceneViewWindow.LastFocusedCamera;
                        g.Transform.position = cam.GameObject.Transform.position;
                        g.Transform.rotation = cam.GameObject.Transform.rotation;
                    }
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

            if (SelectHandler.Count > 0 && SelectHandler.Selected.Any(x => (x.Target as GameObject)?.PrefabLink != null) && EditorGUI.StyledButton("Break Prefab Connection"))
            {
                using (UndoRedoManager.CreateTransaction())
                {
                    SelectHandler.Foreach((go) =>
                    {
                        if (go.Target is GameObject g)
                            UndoRedoManager.RecordAction(new BreakPrefabLinkAction(g));
                    });
                }
                closePopup = true;
            }
        }

        if (closePopup)
            gui.CloseAllPopups();
    }

    public void DrawGameObject(ref int index, GameObject entity, uint depth)
    {
        if (entity == null) return;
        if (entity.hideFlags.HasFlag(HideFlags.Hide) || entity.hideFlags.HasFlag(HideFlags.HideAndDontSave)) return;

        bool isPartOfPrefab = entity.AffectedByPrefabLink != null && entity.AffectedByPrefabLink.IsSource(entity);

        if (!string.IsNullOrEmpty(_searchText) && !entity.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < entity.children.Count; i++)
                DrawGameObject(ref index, entity.children[i], depth);
            return;
        }

        bool drawChildren = false;
        bool isPrefab = entity.PrefabLink != null;
        double left = depth * entryHeight;
        ulong goNodeID = 0;
        double width = gui.CurrentNode.LayoutData.InnerRect.width - left;
        width = Math.Max(width, 200);
        using (gui.Node(entity.GetHashCode().ToString()).Left(left).Top(index * (entryHeight + EntryPadding)).Width(width).Height(entryHeight).Margin(2, 0).Enter())
        {
            goNodeID = gui.CurrentNode.ID;
            float colMult = entity.enabledInHierarchy ? 1 : 0.5f;
            bool isSelected = SelectHandler.IsSelected(new WeakReference(entity));

            double maxwidth = gui.CurrentNode.LayoutData.InnerRect.width;
            Rect rect = gui.CurrentNode.LayoutData.InnerRect;
            rect.width = maxwidth;
            rect.height = entryHeight;

            // Interaction
            SelectHandler.AddSelectableAtIndex(index, new WeakReference(entity));
            Interactable interact = gui.GetInteractable(rect);
            if (interact.TakeFocus(true))
                SelectHandler.Select(index, new WeakReference(entity));

            if (SelectHandler.Count == 1 && gui.IsPointerDoubleClick(MouseButton.Left) && interact.IsHovered())
            {
                _justStartedRename = true;
                _renamingGO = entity;
                _renamingText = entity.Name;
            }
            else if (gui.IsPointerClick(MouseButton.Right) && interact.IsHovered())
            {
                // POpup holder is our parent, since thats the Tree node
                gui.OpenPopup("RightClickGameObject", null, gui.CurrentNode.Parent);
                gui.SetGlobalStorage("RightClickGameObject", entity.InstanceID);
            }

            if (IsFocused)
                if (isSelected && Input.GetKeyDown(Key.Delete))
                    UndoRedoManager.RecordAction(new DeleteGameObjectAction(entity.Identifier));
                    //entity.DestroyLater();

            // Drag n Drop
            // Dropping uses the current nodes rect by default
            HandleDrop(entity);
            DragnDrop.Drag(entity);

            Color col = (interact.IsHovered() ? EditorStylePrefs.Instance.Hovering : Color.white * 0.5f) * colMult;
            gui.Draw2D.DrawRectFilled(rect, (isSelected ? EditorStylePrefs.Instance.Highlighted : col), (float)EditorStylePrefs.Instance.ButtonRoundness);

            // if (entity.children.Count > 0)
            //     gui.Draw2D.DrawRectFilled(rect.Min, new Vector2(22, entryHeight), EditorStylePrefs.Instance.Borders, (float)EditorStylePrefs.Instance.ButtonRoundness, 9);

            if (isPrefab || isPartOfPrefab || !entity.enabledInHierarchy)
            {
                Color lineColor = (isPrefab ? EditorStylePrefs.Orange : EditorStylePrefs.Yellow);
                if (!entity.enabledInHierarchy)
                    lineColor = EditorStylePrefs.Instance.Warning;
                gui.Draw2D.DrawLine(new Vector2(rect.x + entryHeight + 1, rect.y - 1), new Vector2(rect.x + entryHeight + 1, rect.y + entryHeight - 1), lineColor, 3);
            }

            // if were pinging we need to open the tree to the pinged object
            if (s_pingTimer > 0 && s_pingedGO != null && s_pingedGO.Target is GameObject go)
            {
                if (entity.IsParentOf(go)) // Set the tree open
                    gui.SetNodeStorage(entity.InstanceID.ToString(), true);
                else if (entity.InstanceID == go.InstanceID)
                {
                    // Draw a ping effect
                    // TODO: Scroll to Rect
                    Rect pingRect = rect;
                    pingRect.Expand(MathF.Sin(s_pingTimer) * 6f);
                    gui.Draw2D.DrawRect(pingRect, EditorStylePrefs.Yellow, 2f, 4f);
                }
            }

            if (entity.children.Count > 0)
            {
                bool expanded = gui.GetNodeStorage<bool>(entity.InstanceID.ToString());
                using (gui.Node("VisibilityBtn").Top(1).Width(22).Height(entryHeight).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        expanded = !expanded;
                        gui.SetNodeStorage(gui.CurrentNode.Parent, entity.InstanceID.ToString(), expanded);
                    }

                    Rect btnRect = gui.CurrentNode.LayoutData.Rect;
                    // btnRect.x -= 1;
                    gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, btnRect, entity.enabledInHierarchy ? Color.white : Color.white * (float)EditorStylePrefs.Instance.Disabled);
                }
                drawChildren = expanded;
            }

            float leftOffset = entity.children.Count > 0 ? 25 : 7;

            // Name
            string name = entity.Name;

            if (_renamingGO == entity)
            {
                Rect inputRect = new(rect.x + leftOffset, rect.y, maxwidth - (entryHeight * 2.25), entryHeight);
                gui.Draw2D.DrawRectFilled(inputRect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.ButtonRoundness);
                gui.InputField("RenameInput", ref _renamingText, 64, Gui.InputFieldFlags.None, 30, 0, maxwidth - (entryHeight * 2.25), entryHeight, EditorGUI.InputStyle, true);
                if (_justStartedRename)
                    gui.FocusPreviousInteractable();
                if (!gui.PreviousInteractableIsFocus())
                {
                    _renamingGO = null;
                    if (_renamingText != entity.Name)
                        UndoRedoManager.RecordAction(new ChangeFieldOnGameObjectAction(entity, nameof(GameObject.Name), name.Trim()));
                }
                //entity.Name = name;
                _justStartedRename = false;
            }
            else
            {
                Rect textRect = rect;
                textRect.width -= entryHeight;
                double textSizeY = Font.DefaultFont.CalcTextSize(name, 20).y;
                double centerY = rect.y + (rect.height / 2) - (textSizeY / 2);
                gui.Draw2D.DrawText(Font.DefaultFont, name, 20, new Vector2(rect.x + leftOffset, centerY + 3), Color.white, 0, textRect);
            }

            index++;
        }

        // Open
        if (drawChildren)
        {
            gui.PushID(goNodeID);
            for (int i = 0; i < entity.children.Count; i++)
                DrawGameObject(ref index, entity.children[i], depth + 1);
            gui.PopID();
        }
    }

    private static void HandleDrop(GameObject? entity)
    {
        if (DragnDrop.Drop<GameObject>(out GameObject? original))
        {
            GameObject go = original!;
            if (!SceneManager.Has(original!)) // If its not already in the scene, Instantiate it
            {
                go = original!.DeepClone();
                go.AssetID = original!.AssetID; // Retain Asset ID
                UndoRedoManager.RecordAction(new AddGameObjectToSceneAction(go, entity));
            }
            else
            {
                UndoRedoManager.RecordAction(new SetParentAction(go.Identifier, entity.Identifier));
            }

            //if (entity != null)
            //    go.SetParent(entity); // Also adds to scene
            //else
            //{
            //    go.SetParent(null); // null is root - doesnt add to scene as SetParent has no idea what scene to add to
            //    SceneManager.Scene.Add(go);
            //}
            //SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Prefab>(out Prefab? prefab))
        {
            var go = prefab!.Instantiate();
            UndoRedoManager.RecordAction(new AddGameObjectToSceneAction(go, entity));
            //if (entity != null)
            //    go.SetParent(entity); // Also adds to scene
            //else
            //{
            //    go.SetParent(null); // null is root - doesnt add to scene as SetParent has no idea what scene to add to
            //    SceneManager.Scene.Add(go);
            //}
            //SelectHandler.SetSelection(new WeakReference(go));
        }
        else if (DragnDrop.Drop<Scene>(out Scene? scene))
        {
            SceneManager.LoadScene(scene!);
        }
    }

    public static void DuplicateSelected()
    {
        using (UndoRedoManager.CreateTransaction())
        {
            var newGO = new List<WeakReference>();
            SelectHandler.Foreach((obj) =>
            {
                var go = (obj.Target as GameObject);
                if (go == null) return;
                UndoRedoManager.RecordAction(new CloneGameObjectAction(go.Identifier));
                //GameObject? cloned = go.DeepClone() ?? throw new Exception("Failed to clone GameObject");
                //if (go.parent != null)
                //    cloned.SetParent(go.parent);
                //else // SetParent adds to scene automatically so we only need to add ourselves if it doesnt have a parent
                //    SceneManager.Scene.Add(cloned);
                //newGO.Add(new WeakReference(cloned));
            });
            SelectHandler.Clear();
#warning TODO: Need a way to Select the new GameObjects
            //SelectHandler.SetSelection([.. newGO]);
        }
    }

}
