using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Prowl.Icons;
using HexaEngine.ImGuiNET;
using Newtonsoft.Json;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public delegate void OnSelect(GameObject go);

public class HierarchyWindow : EditorWindow {

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    public HierarchyWindow() {
        Title = "Hierarchy";
    }
    
    private string _searchText = "";
    private GameObject m_DeletedEntity = null;
    private GameObject m_RenamingEntity = null;

    private GameObject m_DraggedEntity = null;
    private GameObject m_DraggedEntityTarget = null;

    protected override void Draw()
    {
        ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ContextMenuInBody
            | ImGuiTableFlags.BordersInner
            | ImGuiTableFlags.ScrollY;

        float lineHeight = ImGui.GetTextLineHeight();
        float contentWidth = ImGui.GetContentRegionAvail().X;
        Vector2 padding = ImGui.GetStyle().FramePadding;

        float filterCursorPosX = ImGui.GetCursorPosX();
        ImGui.InputText("##searchBox", ref _searchText, 0x100);

        ImGui.SameLine();

        if (ImGui.Button("  " + FontAwesome6.Plus + " Add  "))
            ImGui.OpenPopup("SceneHierarchyContextWindow");

        if (string.IsNullOrEmpty(_searchText))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(filterCursorPosX + ImGui.GetFontSize() * 0.5f);
            ImGui.TextUnformatted(FontAwesome6.MagnifyingGlass + " Search...");
        }

        if (ImGui.BeginTable("HierarchyTable", 2, tableFlags))
        {
            if (ImGui.BeginPopupContextWindow("SceneHierarchyContextWindow", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
            {
                DrawContextMenu();
                ImGui.EndPopup();
            }

            ImGui.TableSetupColumn("  Label", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoClip, contentWidth);
            ImGui.TableSetupColumn(" " + FontAwesome6.Eye, ImGuiTableColumnFlags.WidthFixed, lineHeight * 1.25f);

            ImGui.TableSetupScrollFreeze(0, 1);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, ImGui.GetFrameHeight());
            for (int column = 0; column < 2; ++column)
            {
                ImGui.TableSetColumnIndex(column);
                string columnName = ImGui.TableGetColumnNameS(column);
                ImGui.PushID(column);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + padding.Y);
                ImGui.TableHeader(columnName);
                ImGui.PopID();
            }

            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f); 
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, 0f));
            //foreach (var go in Hierarchy.RegisteredGOs)
            for (int i = 0; i < GameObjectManager.AllGameObjects.Count; i++)
            {
                var go = GameObjectManager.AllGameObjects[i];
                if (go.Parent == null)
                    DrawEntityNode(go, 0, false);
            }
            ImGui.PopStyleVar(2);

            //if (ImGui.BeginPopupContextWindow("SceneHierarchyContextWindow", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
            //    DrawContextMenu();

            ImGui.EndTable();

            unsafe
            {
                if (ImGui.BeginDragDropTarget())
                {
                    ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload("GameObjectRef");
                    if (!entityPayload.IsNull)
                    {
                        //var dataPtr = (int*)entityPayload.Data;
                        //int entityId = dataPtr[0];
                        m_DraggedEntity = EngineObject.FindObjectByID<GameObject>((int)Selection.Dragging);
                        m_DraggedEntityTarget = null;
                    }

                    ImGui.EndDragDropTarget();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload($"ASSETPAYLOAD_GameObject");
                        if (!entityPayload.IsNull)
                            if (Selection.Dragging is Guid guidToAsset)
                            {
                                var original = AssetDatabase.LoadAsset<GameObject>(guidToAsset);
                                GameObject clone = (GameObject)EngineObject.Instantiate(original, true);
                                clone.Position = original.Position;
                                clone.Orientation = original.Orientation;
                                clone.SetParent(null);
                                clone.Recalculate();
                                //GameObject go = GameObject.Instantiate();
                                //go.SetParent(null);
                                Selection.Select(clone);
                            }
                    }
                    ImGui.EndDragDropTarget();
                }
            }

            if (ImGui.IsItemClicked())
                Selection.Clear();
        }

        //if (m_DraggedEntity != null && m_DraggedEntityTarget != null)
        if (m_DraggedEntity != null)
        {
            m_DraggedEntity.SetParent(m_DraggedEntityTarget);
            m_DraggedEntity = null;
            m_DraggedEntityTarget = null;
        }

        if (m_DeletedEntity != null)
        {
            m_DeletedEntity.DestroyImmediate();
            m_DeletedEntity = null;
        }
    }

    void DrawEntityNode(GameObject entity, uint depth, bool isPartOfPrefab)
    {
        if (entity == null) return;

        if (entity.hideFlags.HasFlag(HideFlags.Hide) || entity.hideFlags.HasFlag(HideFlags.HideAndDontSave)) return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var tag = entity.Name;
        bool isPrefab = entity.AssetID != Guid.Empty;

        if (string.IsNullOrEmpty(_searchText) == false && tag.ToLower().Contains(_searchText.ToLower()) == false)
        {
            for (int i = 0; i < entity.Children.Count; i++)
                DrawEntityNode(entity.Children[i], depth, isPartOfPrefab);
            return;
        }

        ImGuiTreeNodeFlags flags = (Selection.Current == entity) ? ImGuiTreeNodeFlags.Selected : 0;
        flags |= ImGuiTreeNodeFlags.OpenOnArrow;
        flags |= ImGuiTreeNodeFlags.SpanFullWidth;
        flags |= ImGuiTreeNodeFlags.FramePadding;

        if (entity.Children.Count == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        }

        var highlight = Selection.Current == entity;
        if (highlight)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ImGui.GetColorU32(ImGuiCol.HeaderActive));
        }
        else if (entity.EnabledInHierarchy == false)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        }

        if(!highlight && isPrefab)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.0f, 0.3f, 1.0f)));

        var opened = ImGui.TreeNodeEx(entity.Name + "##" + entity.GetHashCode(), flags);

        if (highlight)
            ImGui.PopStyleColor(2);
        else if (entity.EnabledInHierarchy == false)
            ImGui.PopStyleColor(2);

        // Select
        if (!ImGui.IsItemToggledOpen() &&
            (ImGui.IsItemClicked(ImGuiMouseButton.Left) ||
             ImGui.IsItemClicked(ImGuiMouseButton.Middle) ||
             ImGui.IsItemClicked(ImGuiMouseButton.Right)))
        {
            Selection.Select(entity);
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                m_RenamingEntity = entity;
        }

        bool entityDeleted = false;
        if (ImGui.BeginPopupContextItem())
        {
            if (Selection.Current != entity)
                Selection.Select(entity);

            if (ImGui.MenuItem("Rename", "F2"))
                m_RenamingEntity = entity;
            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
            {
                // Duplicating, Easiest way to duplicate is to Serialize then Deserialize and add the new object to the hierarchy
                var prefab = JsonUtility.Serialize(entity);
                GameObject deserialized = JsonUtility.Deserialize<GameObject>(prefab);
                deserialized.SetParent(entity.Parent);
                Selection.Select(deserialized);
            }
            if (ImGui.MenuItem("Delete", "Del"))
                entityDeleted = true;

            ImGui.Separator();

            DrawContextMenu(entity);

            ImGui.EndPopup();
        }

        var verticalLineStart = ImGui.GetCursorScreenPos();
        verticalLineStart.X -= 0.5f;
        verticalLineStart.Y -= ImGui.GetFrameHeight() * 0.5f;

        // Drag Drop
        unsafe
        {
            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload("GameObjectRef");
                if (!entityPayload.IsNull)
                {
                    //var dataPtr = (int*)entityPayload.Data;
                    //int entityId = dataPtr[0];
                    m_DraggedEntity = EngineObject.FindObjectByID<GameObject>((int)Selection.Dragging);
                    m_DraggedEntityTarget = entity;
                }
                else
                {
                    // Recieve Prefab
                }

                ImGui.EndDragDropTarget();
            }

            if (ImGui.BeginDragDropSource())
            {
                ImGui.TextUnformatted(tag);
                int entityId = entity.InstanceID;
                //ImGui.SetDragDropPayload("GameObjectRef", (IntPtr)(&entityId), sizeof(int));
                Selection.SetDragging(entityId);
                ImGui.EndDragDropSource();
            }
        }

        if (entity == m_RenamingEntity)
        {
            ImGui.SetKeyboardFocusHere();
            ImGui.InputText("##Tag", ref tag, 0x100);
            if (ImGui.IsItemDeactivated())
            {
                m_RenamingEntity = null;
                entity.Name = tag;
            }
        }

        ImGui.TableNextColumn();

        // Visibility Toggle
        {
            ImGui.Text(" " + (entity.Enabled ? FontAwesome6.Eye : FontAwesome6.EyeSlash));

            if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                entity.Enabled = !entity.Enabled;
        }

        ImGui.PopStyleColor(3);

        //if (prefabColorApplied)
        //    ImGui.PopStyleColor();

        // Open
        if (opened)
        {
            for (int i = 0; i < entity.Children.Count; i++)
                DrawEntityNode(entity.Children[i], depth + 1, isPartOfPrefab);

            if (opened && entity.Children.Count > 0)
                ImGui.TreePop();
        }

        // PostProcess Actions
        if (entityDeleted)
            m_DeletedEntity = entity;
    }

    void DrawContextMenu(GameObject context = null)
    {
        bool hasContext = context != null;

        GameObject toSelect = null;

        if (ImGui.MenuItem("New Gameobject"))
        {
            toSelect = new GameObject("New Gameobject");
            if (hasContext)
                toSelect.SetParent(context);
            toSelect.Position = Vector3.Zero;
            toSelect.Orientation = Quaternion.Identity;
            toSelect.Scale = Vector3.One;
            ImGui.CloseCurrentPopup();
        }

        if (toSelect != null)
        {
            Selection.Select(toSelect);
        }
    }
}
