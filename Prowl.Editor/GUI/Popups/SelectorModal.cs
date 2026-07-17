// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Popups;

/// <summary>
/// Controls which tabs are available in the selector.
/// </summary>
[Flags]
public enum SelectorTabs
{
    Scene = 1,
    Assets = 2,
    Both = Scene | Assets,
}

/// <summary>
/// Unified asset/scene selector modal. Provides two tabs:
/// <list type="bullet">
///   <item><b>Scene</b> lists GameObjects and components from the current scene (list view).</item>
///   <item><b>Assets</b> lists project + built-in assets with thumbnails (grid view).</item>
/// </list>
/// Only one selector can be open at a time.
/// </summary>
public static class SelectorModal
{
    // ---- State ----
    private static readonly ModalHandle _handle = new();
    private static string _title = "";
    private static Type _targetType = typeof(object);
    private static SelectorTabs _tabs;
    private static SelectorTabs _activeTab;
    private static Action<object?>? _callback;
    private static string _searchText = "";

    public static bool IsOpen => _handle.IsOpen;

    /// <summary>
    /// Open the selector modal.
    /// </summary>
    /// <param name="title">Modal title (e.g. "Select Mesh").</param>
    /// <param name="targetType">The type being selected (e.g. typeof(Transform), typeof(Mesh), typeof(Rigidbody3D)).</param>
    /// <param name="tabs">Which tabs to show.</param>
    /// <param name="onSelect">Callback with the selected value, or null for "None".</param>
    public static void Open(string title, Type targetType, SelectorTabs tabs, Action<object?> onSelect)
    {
        _title = title;
        _targetType = targetType;
        _tabs = tabs;
        _callback = onSelect;
        _searchText = "";
        _activeTab = tabs.HasFlag(SelectorTabs.Scene) ? SelectorTabs.Scene : SelectorTabs.Assets;
        _handle.Open(DrawInternal, closeOnBackdrop: true);
    }

    public static void Close()
    {
        _callback = null;
        _handle.Close();
    }

    private static void DrawInternal(Paper paper, int layer)
    {
        if (!_handle.IsOpen) return;

        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Modal window (backdrop handled by modal stack)
        using (paper.Column("sel_modal")
            .Size(380, 460)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(layer)
            .StopEventPropagation()
            .Enter())
        {
            DrawHeader(paper, font);
            DrawSearchBar(paper, font);

            if (_tabs.HasFlag(SelectorTabs.Scene) && _tabs.HasFlag(SelectorTabs.Assets))
                DrawTabs(paper, font);

            // Content
            float contentH = 460 - 32 - 30 - 8;
            if (_tabs.HasFlag(SelectorTabs.Scene) && _tabs.HasFlag(SelectorTabs.Assets))
                contentH -= 28; // tabs row

            if (_activeTab == SelectorTabs.Scene)
                DrawSceneTab(paper, font, contentH);
            else
                DrawAssetsTab(paper, font, contentH);
        }
    }

    // ================================================================
    //  Header
    // ================================================================

    private static void DrawHeader(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("sel_header")
            .Height(32).ChildLeft(12).ChildRight(8).RowBetween(8)
            .BackgroundColor(EditorTheme.Neutral200)
            .Enter())
        {
            paper.Box("sel_title").Height(32)
                .Text(_title, font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            paper.Box("sel_spacer");

            paper.Box("sel_close")
                .Width(24).Height(24).Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.Red300).End()
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => Close());
        }
    }

    // ================================================================
    //  Search bar
    // ================================================================

    private static void DrawSearchBar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("sel_searchrow").Height(30).Margin(6, 6, 6, 0).RowBetween(4).Enter())
        {
            paper.Box("sel_search_ico")
                .Width(18).Height(24)
                .Text(EditorIcons.MagnifyingGlass, font).TextColor(EditorTheme.Ink400)
                .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

            Origami.SearchField(paper, "sel_search", _searchText, v => _searchText = v).Show();
        }
    }

    // ================================================================
    //  Tabs (Scene / Assets)
    // ================================================================

    private static void DrawTabs(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("sel_tabs").Height(28).Margin(6, 6, 0, 0).RowBetween(2).Enter())
        {
            DrawTabButton(paper, font, "sel_tab_scene", $"{EditorIcons.Sitemap}  {Loc.Get("panel.scene")}", SelectorTabs.Scene);
            DrawTabButton(paper, font, "sel_tab_assets", $"{EditorIcons.FolderOpen}  {Loc.Get("menu.assets")}", SelectorTabs.Assets);
        }
    }

    private static void DrawTabButton(Paper paper, Prowl.Scribe.FontFile font, string id, string label, SelectorTabs tab)
    {
        bool active = _activeTab == tab;
        paper.Box(id)
            .Height(24).Rounded(4)
            .BackgroundColor(active ? EditorTheme.Purple400 : EditorTheme.Neutral200)
            .Hovered.BackgroundColor(active ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
            .Text(label, font)
            .TextColor(active ? EditorTheme.Ink500 : EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
            .OnClick(tab, (t, _) => _activeTab = t);
    }

    // ================================================================
    //  Scene tab list of GameObjects / Components
    // ================================================================

    private static void DrawSceneTab(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        var scene = Runtime.Resources.Scene.Current;
        if (scene == null)
        {
            paper.Box("sel_scene_empty").Height(40)
                .Text(Loc.Get("selector.no_scene"), font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        // Determine what we're listing
        bool isTransform = typeof(Transform).IsAssignableFrom(_targetType);
        bool isGameObject = typeof(GameObject).IsAssignableFrom(_targetType);
        bool isComponent = typeof(MonoBehaviour).IsAssignableFrom(_targetType);

        Origami.ScrollView(paper, "sel_scene_scroll", 380, height).Padding(4, 4, 4, 0).Body(() =>
        {
            // None option always first
            paper.Box("sel_s_none")
                .Height(EditorTheme.RowHeight).ChildLeft(8)
                .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                .Rounded(3)
                .Text($"{EditorIcons.Circle}  None ({_targetType.Name})", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .OnClick(0, (_, _) => { _callback?.Invoke(null); Close(); });

            int idx = 0;
            foreach (var go in scene.AllObjects)
            {
                if (go.HideFlags.HasFlag(HideFlags.Hide) || go.HideFlags.HasFlag(HideFlags.HideAndDontSave))
                    continue;

                if (isTransform || isGameObject)
                {
                    // Filter by search
                    if (!string.IsNullOrEmpty(_searchText) &&
                        !go.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    object selectValue = isTransform ? go.Transform : go;
                    string path = GetHierarchyPath(go);
                    string detail = string.IsNullOrEmpty(path) ? "" : path;

                    DrawSceneItem(paper, font, $"sel_s_{idx++}", go.Name, detail,
                        GetGOIcon(go), selectValue);
                }
                else if (isComponent)
                {
                    // List matching components
                    foreach (var comp in go.GetComponents<MonoBehaviour>())
                    {
                        if (!_targetType.IsAssignableFrom(comp.GetType())) continue;

                        string compName = $"{comp.GetType().Name}";
                        if (!string.IsNullOrEmpty(_searchText) &&
                            !compName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                            !go.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                            continue;

                        DrawSceneItem(paper, font, $"sel_s_{idx++}", go.Name, compName,
                            EditorIcons.PuzzlePiece, comp);
                    }
                }

                if (idx > 200) break;
            }

            if (idx == 0)
            {
                paper.Box("sel_scene_none").Height(40)
                    .Text(Loc.Get("selector.no_objects"), font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            }
        });
    }

    private static void DrawSceneItem(Paper paper, Prowl.Scribe.FontFile font,
        string id, string name, string detail, string icon, object value)
    {
        using (paper.Row(id)
            .Height(EditorTheme.RowHeight).ChildLeft(8).RowBetween(4)
            .Hovered.BackgroundColor(EditorTheme.Purple400).End()
            .Rounded(3)
            .OnClick(value, (val, _) =>
            {
                _callback?.Invoke(val);
                Close();
            })
            .Enter())
        {
            paper.Box($"{id}_ico")
                .Width(14).Height(EditorTheme.RowHeight)
                .Text(icon, font).TextColor(EditorTheme.Ink400)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter);

            paper.Box($"{id}_name")
                .Height(EditorTheme.RowHeight).Clip()
                .Text(name, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            if (!string.IsNullOrEmpty(detail))
            {
                paper.Box($"{id}_detail")
                    .Width(UnitValue.Auto).Height(EditorTheme.RowHeight).ChildRight(4)
                    .Text(detail, font).TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
            }
        }
    }

    // ================================================================
    //  Assets tab grid with thumbnails
    // ================================================================

    private static void DrawAssetsTab(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        var db = EditorAssetBackend.Instance;
        if (db == null)
        {
            paper.Box("sel_asset_empty").Height(40)
                .Text(Loc.Get("selector.no_database"), font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        var items = db.FindAllOfType(_targetType).Take(200).ToList();

        // Filter by search
        if (!string.IsNullOrEmpty(_searchText))
        {
            items = items.Where(i =>
                i.name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        const float cellSize = 72f;
        const float labelH = 18f;
        const float totalCellH = cellSize + labelH;
        float gridWidth = 380 - 12;

        Origami.ScrollView(paper, "sel_asset_scroll", 380, height).Padding(4, 4, 4, 0).Body(() =>
        {
            // None option always first
            paper.Box("sel_a_none")
                .Height(EditorTheme.RowHeight).ChildLeft(8)
                .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                .Rounded(3)
                .Text($"{EditorIcons.Circle}  None ({_targetType.Name})", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .OnClick(0, (_, _) => { _callback?.Invoke(null); Close(); });

            if (items.Count == 0)
            {
                paper.Box("sel_asset_none").Height(40)
                    .Text(Loc.Get("selector.no_assets"), font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            }
            else
            {
                int cols = Math.Max(1, (int)(gridWidth / cellSize));
                int row = 0;
                for (int i = 0; i < items.Count; i += cols)
                {
                    using (paper.Row($"sel_ar_{row}")
                        .Height(totalCellH)
                        .RowBetween(4)
                        .Enter())
                    {
                        for (int j = 0; j < cols && i + j < items.Count; j++)
                        {
                            int idx = i + j;
                            var (guid, name, parentPath, assetType) = items[idx];
                            DrawAssetGridItem(paper, font, $"sel_a_{idx}", guid, name, cellSize, labelH, totalCellH);
                        }
                    }
                    row++;
                }
            }
        });
    }

    private static void DrawAssetGridItem(Paper paper, Prowl.Scribe.FontFile font,
        string id, Guid guid, string name, float cellSize, float labelH, float totalCellH)
    {
        var thumbTex = EditorAssetBackend.Instance?.GetThumbnailTexture(guid);

        using (paper.Column(id)
            .Width(cellSize).Height(totalCellH)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Rounded(4)
            .OnClick(guid, (g, _) =>
            {
                var asset = Runtime.AssetDatabase.Get(g);
                _callback?.Invoke(asset);
                Close();
            })
            .Tooltip(name)
            .Enter())
        {
            if (thumbTex != null)
            {
                paper.Box($"{id}_t")
                    .Width(cellSize - 4).Height(cellSize - 4)
                    .Margin(2, 2, 2, 0).Rounded(4)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        canvas.DrawImage(thumbTex,
                            (float)r.Min.X, (float)r.Min.Y,
                            (float)r.Size.X, (float)r.Size.Y);
                    }));
            }
            else
            {
                paper.Box($"{id}_t")
                    .Width(cellSize - 4).Height(cellSize - 4)
                    .Margin(2, 2, 2, 0).Rounded(4)
                    .Text(EditorIcons.Cube, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(cellSize * 0.45f)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            paper.Box($"{id}_l")
                .Width(cellSize).Height(labelH).Clip()
                .Text(name, font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static string GetGOIcon(GameObject go)
    {
        if (go.GetComponent<Camera>() != null) return EditorIcons.Camera;
        if (go.GetComponent<Light>() != null) return EditorIcons.Sun;
        if (go.GetComponent<MeshRenderer>() != null) return EditorIcons.Cube;
        if (go.GetComponent<SkinnedMeshRenderer>() != null) return EditorIcons.Cubes;
        return EditorIcons.Circle;
    }

    private static string GetHierarchyPath(GameObject go)
    {
        var parent = go.Parent;
        if (!parent.IsValid()) return "";
        var parts = new List<string>();
        while (parent.IsValid())
        {
            parts.Add(parent.Name);
            parent = parent.Parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
