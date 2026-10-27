// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
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
    /// <summary> Scene tab: lists GameObjects and components from the current scene. </summary>
    Scene = 1,
    /// <summary> Assets tab: lists project and built-in assets with thumbnails. </summary>
    Assets = 2,
    /// <summary> Both tabs: shows both Scene and Assets tabs. </summary>
    Both = Scene | Assets,
}

/// <summary> Unified asset/scene selector modal. Provides a Scene tab (lists GameObjects and components from the current scene) and an Assets tab (lists project and built-in assets with thumbnails). Only one selector can be open at a time. </summary>
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

    /// <summary> Close the selector modal and clear the callback. </summary>
    public static void Close()
    {
        _callback = null;
        _handle.Close();
    }

    private static void DrawInternal(Paper paper, int layer)
    {
        if (!_handle.IsOpen) return;

        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Modal window (backdrop handled by modal stack)
        using (paper.Column("sel_modal")
            .Size(380, 460)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(Origami.Current.Metrics.ContainerRounding)
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
            .Height(32).PaddingLeft(12).PaddingRight(8).Gap(8)
            .BackgroundColor(EditorTheme.Neutral200)
            .RoundedTop(Origami.Current.Metrics.ContainerRounding)
            .Enter())
        {
            paper.Box("sel_title").Height(32)
                .Text(_title, font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            paper.Box("sel_spacer");

            Origami.IconButton(paper, "sel_close", EditorIcons.Xmark_I, Close)
                .Width(24).Height(24).Ghost().Show();
        }
    }

    // ================================================================
    //  Search bar
    // ================================================================

    private static void DrawSearchBar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("sel_searchrow").Height(30).Margin(6, 6, 6, 0).Gap(4).Enter())
        {
            Origami.SearchField(paper, "sel_search", _searchText, v => _searchText = v).AutoFocus().Show();
        }
    }

    // ================================================================
    //  Tabs (Scene / Assets)
    // ================================================================

    private static void DrawTabs(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("sel_tabs").Height(28).Margin(6, 6, 0, 0).Enter())
        {
            Origami.ButtonGroup(paper, "sel_tab_group", _activeTab == SelectorTabs.Scene ? 0 : 1,
                    i => _activeTab = i == 0 ? SelectorTabs.Scene : SelectorTabs.Assets)
                .Height(24).FullWidth()
                .Item(Loc.Get("panel.scene"), EditorIcons.Sitemap)
                .Item(Loc.Get("menu.assets"), EditorIcons.FolderOpen)
                .Show();
        }
    }

    // ================================================================
    //  Scene tab list of GameObjects / Components
    // ================================================================

    private static void DrawSceneTab(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        Scene scene = Runtime.Resources.Scene.Current;
        if (scene.IsNotValid())
        {
            EditorGUI.EmptyState(paper, "sel_scene_empty", Loc.Get("selector.no_scene"), font);
            return;
        }

        // Determine what we're listing
        bool isTransform = typeof(Transform).IsAssignableFrom(_targetType);
        bool isGameObject = typeof(GameObject).IsAssignableFrom(_targetType);
        bool isComponent = typeof(Component).IsAssignableFrom(_targetType);

        Origami.ScrollView(paper, "sel_scene_scroll", 380, height).Padding(4, 4, 4, 0).Body(() =>
        {
            // None option always first
            paper.Box("sel_s_none")
                .Height(EditorTheme.RowHeight).PaddingLeft(8)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Rounded(Origami.Current.Metrics.Rounding)
                .Text($"{EditorIcons.Circle}  None ({_targetType.Name})", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .OnClick(0, (_, _) => { _callback?.Invoke(null); Close(); });

            int idx = 0;
            foreach (GameObject go in scene.AllObjects)
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
                    string path = EditorUtils.GetHierarchyPath(go);
                    string detail = string.IsNullOrEmpty(path) ? "" : path;

                    DrawSceneItem(paper, font, $"sel_s_{idx++}", go.Name, detail,
                        GetGOIcon(go), selectValue);
                }
                else if (isComponent)
                {
                    // List matching components
                    foreach (Component comp in go.GetComponents<Component>())
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
                EditorGUI.EmptyState(paper, "sel_scene_none", Loc.Get("selector.no_objects"), font);
            }
        });
    }

    private static void DrawSceneItem(Paper paper, Prowl.Scribe.FontFile font,
        string id, string name, string detail, string icon, object value)
    {
        using (paper.Row(id)
            .Height(EditorTheme.RowHeight).PaddingLeft(8).Gap(4)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Rounded(Origami.Current.Metrics.Rounding)
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
                    .Width(UnitValue.Auto).Height(EditorTheme.RowHeight).PaddingRight(4)
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
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null)
        {
            EditorGUI.EmptyState(paper, "sel_asset_empty", Loc.Get("selector.no_database"), font);
            return;
        }

        // Exact then prefix matches sort ahead so the closest ones survive the cap.
        IEnumerable<(Guid guid, string name, string parentPath, Type assetType)> all = db.FindAllOfType(_targetType);
        IEnumerable<(Guid guid, string name, string parentPath, Type assetType)> matches = string.IsNullOrEmpty(_searchText)
            ? all
            : all.Where(i => i.name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                 .OrderBy(i => i.name.Equals(_searchText, StringComparison.OrdinalIgnoreCase) ? 0
                             : i.name.StartsWith(_searchText, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                 .ThenBy(i => i.name, StringComparer.OrdinalIgnoreCase);

        var items = matches.Take(200).ToList();

        const float cellSize = 72f;
        const float labelH = 18f;
        const float totalCellH = cellSize + labelH;
        float gridWidth = 380 - 12;

        Origami.ScrollView(paper, "sel_asset_scroll", 380, height).Padding(4, 4, 4, 0).Body(() =>
        {
            // None option always first
            paper.Box("sel_a_none")
                .Height(EditorTheme.RowHeight).PaddingLeft(8)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Rounded(Origami.Current.Metrics.Rounding)
                .Text($"{EditorIcons.Circle}  None ({_targetType.Name})", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .OnClick(0, (_, _) => { _callback?.Invoke(null); Close(); });

            if (items.Count == 0)
            {
                EditorGUI.EmptyState(paper, "sel_asset_none", Loc.Get("selector.no_assets"), font);
            }
            else
            {
                int cols = Math.Max(1, (int)(gridWidth / cellSize));
                int row = 0;
                for (int i = 0; i < items.Count; i += cols)
                {
                    using (paper.Row($"sel_ar_{row}")
                        .Height(totalCellH)
                        .Gap(4)
                        .Enter())
                    {
                        for (int j = 0; j < cols && i + j < items.Count; j++)
                        {
                            int idx = i + j;
                            (Guid guid, string? name, string? parentPath, Type? assetType) = items[idx];
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
        Texture2D? thumbTex = EditorAssetBackend.Instance?.GetThumbnailTexture(guid);

        using (paper.Column(id)
            .Width(cellSize).Height(totalCellH)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Rounded(Origami.Current.Metrics.Rounding)
            .OnClick(guid, (g, _) =>
            {
                Asset? asset = Runtime.AssetDatabase.Get(g);
                _callback?.Invoke(asset);
                Close();
            })
            .Tooltip(name)
            .Enter())
        {
            if (thumbTex.IsValid())
            {
                float thumbRound = Origami.Current.Metrics.SmallRounding;
                paper.Box($"{id}_t")
                    .Width(cellSize - 4).Height(cellSize - 4)
                    .Margin(2, 2, 2, 0).Rounded(thumbRound)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        canvas.DrawImageRounded(thumbTex,
                            (float)r.Min.X, (float)r.Min.Y,
                            (float)r.Size.X, (float)r.Size.Y, thumbRound);
                    }));
            }
            else
            {
                paper.Box($"{id}_t")
                    .Width(cellSize - 4).Height(cellSize - 4)
                    .Margin(2, 2, 2, 0).Rounded(Origami.Current.Metrics.SmallRounding)
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
        if (go.GetComponent<Camera>().IsValid()) return EditorIcons.Camera;
        if (go.GetComponent<Light>().IsValid()) return EditorIcons.Sun;
        if (go.GetComponent<MeshRenderer>().IsValid()) return EditorIcons.Cube;
        if (go.GetComponent<SkinnedMeshRenderer>().IsValid()) return EditorIcons.Cubes;
        return EditorIcons.Circle;
    }

}
