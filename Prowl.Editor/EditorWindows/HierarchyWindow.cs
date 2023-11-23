using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using Prowl.Icons;
using ImGuiNET;
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

        if (ImGui.BeginTable("HierarchyTable", 3, tableFlags))
        {
            if (ImGui.BeginPopupContextWindow("SceneHierarchyContextWindow", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
            {
                DrawContextMenu();
                ImGui.EndPopup();
            }

            ImGui.TableSetupColumn("  Label", ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoClip);
            ImGui.TableSetupColumn("  Type", ImGuiTableColumnFlags.WidthFixed, lineHeight * 3.0f);
            ImGui.TableSetupColumn("    " + FontAwesome6.Eye, ImGuiTableColumnFlags.WidthFixed, lineHeight * 2.0f);

            ImGui.TableSetupScrollFreeze(0, 1);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, ImGui.GetFrameHeight());
            for (int column = 0; column < 3; ++column)
            {
                ImGui.TableSetColumnIndex(column);
                string columnName = ImGui.TableGetColumnName(column);
                ImGui.PushID(column);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + padding.Y);
                ImGui.TableHeader(columnName);
                ImGui.PopID();
            }

            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f); 
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, 0f));
            //foreach (var go in Hierarchy.RegisteredGOs)
            for (int i = 0; i < SceneManager.AllGameObjects.Count; i++)
            {
                var go = SceneManager.AllGameObjects[i];
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
                    if (entityPayload.NativePtr != null)
                    {
                        var dataPtr = (int*)entityPayload.Data;
                        int entityId = dataPtr[0];
                        m_DraggedEntity = EngineObject.FindObjectByID<GameObject>(entityId);
                        m_DraggedEntityTarget = null;
                    }

                    ImGui.EndDragDropTarget();
                }

                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        ImGuiPayloadPtr entityPayload = ImGui.AcceptDragDropPayload($"ASSETPAYLOAD_GameObject");
                        if (entityPayload.NativePtr != null)
                            if (Selection.Dragging is Guid guidToAsset)
                            {
                                GameObject go = GameObject.Instantiate(AssetDatabase.LoadAsset<GameObject>(guidToAsset));
                                go.SetParent(null);
                                Selection.Select(go);
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

    (Vector2, Vector2) DrawEntityNode(GameObject entity, uint depth, bool isPartOfPrefab)
    {
        if (entity == null)
            return (Vector2.Zero, Vector2.Zero);

        if (entity.hideFlags.HasFlag(HideFlags.Hide) || entity.hideFlags.HasFlag(HideFlags.HideAndDontSave)) return (Vector2.Zero, Vector2.Zero);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        var tag = entity.Name;

        if (string.IsNullOrEmpty(_searchText) == false && tag.ToLower().Contains(_searchText.ToLower()) == false)
        {
            //foreach (var child in entity.Children)
            for (int i = 0; i < entity.Children.Count; i++)
                DrawEntityNode(entity.Children[i], depth, isPartOfPrefab);
            return (Vector2.Zero, Vector2.Zero);
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
        //var highlight = false;
        if (highlight)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ImGui.GetColorU32(ImGuiCol.HeaderActive));
        }
        else if (entity.EnabledInHierarchy == false)
        {
            //ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        }

        //if (!isPartOfPrefab)
        //    isPartOfPrefab = entity.HasComponent<PrefabComponent>();
        //var prefabColorApplied = isPartOfPrefab && entity != m_SelectedEntity;
        //if (prefabColorApplied)
        //    ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.HeaderSelectedColor);
        var opened = ImGui.TreeNodeEx(entity.Name + "##" + entity.GetHashCode(), flags);

        if (highlight)
            ImGui.PopStyleColor(2);
        else if(entity.EnabledInHierarchy == false)
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
                if (entityPayload.NativePtr != null)
                {
                    var dataPtr = (int*)entityPayload.Data;
                    int entityId = dataPtr[0];
                    m_DraggedEntity = EngineObject.FindObjectByID<GameObject>(entityId);
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
                ImGui.SetDragDropPayload("GameObjectRef", (IntPtr)(&entityId), sizeof(int));
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

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0, 0, 0, 0));

        var buttonSizeX = ImGui.GetContentRegionAvail().X;
        var frameHeight = ImGui.GetFrameHeight();
        ImGui.Button(isPartOfPrefab ? "Prefab" : "Unique", new Vector2(buttonSizeX, frameHeight));
        // Select
        if (ImGui.IsItemDeactivated() && ImGui.IsItemHovered() && !ImGui.IsItemToggledOpen())
            Selection.Select(entity);

        ImGui.TableNextColumn();
        // Visibility Toggle
        {
            ImGui.Text("    " + (entity.Enabled ? FontAwesome6.Eye : FontAwesome6.EyeSlash));

            if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                entity.Enabled = !entity.Enabled;
        }

        ImGui.PopStyleColor(3);

        //if (prefabColorApplied)
        //    ImGui.PopStyleColor();

        // Open
        (Vector2, Vector2) nodeRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        {
            if (opened && !entityDeleted)
            {
                var drawList = ImGui.GetWindowDrawList();

                var verticalLineEnd = verticalLineStart;
                const float lineThickness = 1.5f;

                for(int i=0; i< entity.Children.Count; i++)
                {
                    float HorizontalTreeLineSize = entity.Children[i].Children.Count == 0 ? 18.0f : 9.0f; //chosen arbitrarily
                    (Vector2, Vector2) childRect = DrawEntityNode(entity.Children[i], depth + 1, isPartOfPrefab);

                    var midpoint = (childRect.Item1.Y + childRect.Item2.Y) / 2.0f;
                    drawList.AddLine(new Vector2(verticalLineStart.X, midpoint), new Vector2(verticalLineStart.X + HorizontalTreeLineSize, midpoint), ImGui.GetColorU32(ImGuiCol.PlotLines), lineThickness);
                    verticalLineEnd.Y = midpoint;
                }

                drawList.AddLine(verticalLineStart, verticalLineEnd, ImGui.GetColorU32(ImGuiCol.PlotLines), lineThickness);
            }

            if (opened && entity.Children.Count > 0)
                ImGui.TreePop();
        }

        // PostProcess Actions
        if (entityDeleted)
            m_DeletedEntity = entity;

        return nodeRect;
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
