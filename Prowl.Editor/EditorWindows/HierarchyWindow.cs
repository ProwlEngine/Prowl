using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.ImGUI.Widgets;
using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public delegate void OnSelect(GameObject go);

public class HierarchyWindow : EditorWindow {

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    public HierarchyWindow() : base()
    {
        Title = "Hierarchy";
    }
    
    private string _searchText = "";
    private GameObject m_RenamingEntity = null;

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
                if (DragnDrop.ReceiveReference<GameObject>(out var go))
                {
                    go.SetParent(null);
                    Selection.Select(go);
                }

                if (DragnDrop.ReceiveAsset<GameObject>(out var original))
                {
                    GameObject clone = (GameObject)EngineObject.Instantiate(original.Res!, true);
                    clone.Position = original.Res!.Position;
                    clone.Orientation = original.Res!.Orientation;
                    clone.SetParent(null);
                    clone.Recalculate();
                    Selection.Select(clone);
                }
            }

            if (ImGui.IsItemClicked())
                Selection.Clear();
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
                entity.Destroy();

            ImGui.Separator();

            DrawContextMenu(entity);

            ImGui.EndPopup();
        }

        // Drag Drop
        // GameObject from Hierarchy
        if (DragnDrop.ReceiveReference<GameObject>(out var go))
        {
            go.SetParent(entity);
            Selection.Select(go);
        }
        // GameObject from Assets - Prefab
        if (DragnDrop.ReceiveAsset<GameObject>(out var original))
        {
            GameObject clone = (GameObject)EngineObject.Instantiate(original.Res!, true);
            clone.Position = original.Res!.Position;
            clone.Orientation = original.Res!.Orientation;
            clone.SetParent(entity);
            clone.Recalculate();
            Selection.Select(clone);
        }
        // Offer GameObject up from Hierarchy for Drag And Drop
        DragnDrop.OfferReference(entity);


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
