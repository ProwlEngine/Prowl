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

        if (!string.IsNullOrEmpty(_searchText) && !entity.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < entity.Children.Count; i++)
                DrawEntityNode(entity.Children[i], depth, isPartOfPrefab);
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        bool isPrefab = entity.AssetID != Guid.Empty;
        bool isSelected = Selection.Current == entity;

        ImGuiTreeNodeFlags flags = CalculateFlags(entity, isSelected);

        int colPushCount = 0;
        if (isSelected)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ImGui.GetColorU32(ImGuiCol.HeaderActive));
            colPushCount += 2;
        }
        else if (isPrefab)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.0f, 0.3f, 1.0f)));
        else if (isPartOfPrefab)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.0f, 0.3f, 0.5f)));

        if (entity.EnabledInHierarchy == false)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            colPushCount += 1;
        }

        var opened = ImGui.TreeNodeEx(entity.Name + "##" + entity.InstanceID, flags);
        ImGui.PopStyleColor(colPushCount);

        // Select
        if (!ImGui.IsItemToggledOpen() && ImGui.IsItemClicked(0))
        {
            Selection.Select(entity);
            if (ImGui.IsMouseDoubleClicked(0))
                m_RenamingEntity = entity;
        }

        DrawGameObjectContextMenu(entity);

        // Drag Drop
        HandleDragnDrop(entity);

        if (entity == m_RenamingEntity)
        {
            ImGui.SetKeyboardFocusHere();
            string name = entity.Name;
            ImGui.InputText("##Tag", ref name, 0x100);
            if (ImGui.IsItemDeactivated())
            {
                m_RenamingEntity = null;
                entity.Name = name;
            }
        }

        ImGui.TableNextColumn();

        DrawVisibilityToggle(entity);

        // Open
        if (opened)
        {
            for (int i = 0; i < entity.Children.Count; i++)
                DrawEntityNode(entity.Children[i], depth + 1, isPartOfPrefab || isPrefab);

            if (opened && entity.Children.Count > 0)
                ImGui.TreePop();
        }
    }

    private static ImGuiTreeNodeFlags CalculateFlags(GameObject entity, bool isSelected)
    {
        return (isSelected ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None)
            | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding
            | (entity.Children.Count == 0 ? ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen : 0);
    }

    private static void HandleDragnDrop(GameObject entity)
    {
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
    }

    private static void DrawVisibilityToggle(GameObject entity)
    {
        ImGui.Text(" " + (entity.Enabled ? FontAwesome6.Eye : FontAwesome6.EyeSlash));
        if (ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            entity.Enabled = !entity.Enabled;
    }

    void DrawContextMenu(GameObject context = null)
    {
        if (ImGui.MenuItem("New Gameobject"))
        {
            GameObject go = new GameObject("New Gameobject");
            if (context != null)
                go.SetParent(context);
            go.Position = Vector3.Zero;
            go.Orientation = Quaternion.Identity;
            go.Scale = Vector3.One;
            Selection.Select(go);
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();
    }

    void DrawGameObjectContextMenu(GameObject entity)
    {
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
    }
}
