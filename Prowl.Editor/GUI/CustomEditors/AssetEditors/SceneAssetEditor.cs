using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for a Scene asset: shows what the scene references as a searchable, virtualized
/// dependency table (icon + name + type, click a row to ping it in the project)
/// </summary>
[CustomAssetEditor(typeof(Scene))]
public class SceneAssetEditor : AssetImporterEditor
{
    private static UnitValue ST => UnitValue.StretchOne;

    // Resolved, de-duplicated list of true top-level asset references (cached per scene).
    private Guid _cachedSceneGuid;
    private readonly System.Collections.Generic.List<Guid> _refs = new();

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var mono = EditorTheme.FontMono ?? font;
        var m = Origami.Current.Metrics;
        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        if (entry.Guid != _cachedSceneGuid)
            RebuildRefs(entry, db);

        var deps = _refs;
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(entry.Path);

        EditorGUI.SectionHeader(paper, $"{id}_hdr", "Scene", first: true);

        // Quick-facts chips.
        using (paper.Row($"{id}_stats").Width(ST).Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).RowBetween(m.SpacingMedium).Enter())
        {
            EditorGUI.StatChip(paper, $"{id}_st_name", $"{EditorIcons.Shapes}  {sceneName}", font);
            EditorGUI.StatChip(paper, $"{id}_st_deps", $"{deps.Count} reference{(deps.Count == 1 ? "" : "s")}", font);
            paper.Box($"{id}_st_pad").Width(ST).Height(1).IsNotInteractable();
        }

        EditorGUI.SectionHeader(paper, $"{id}_deps_hdr", "References");

        if (deps.Count == 0)
        {
            paper.Box($"{id}_deps_empty").Width(ST).Height(40).Margin(m.PaddingLarge, m.PaddingLarge, 0, 0)
                .IsNotInteractable().Text("This scene references no assets.", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);
            return;
        }

        float rowH = EditorTheme.RowHeight + 4f;
        float tableH = 26f + Math.Clamp(deps.Count, 1, 14) * rowH;

        using (paper.Box($"{id}_deps_wrap").Width(ST).Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Enter())
        {
            Origami.Table(paper, $"{id}_deps_tbl", -1, _ => { })
                .Bordered(true)
                .Scroll(400f, tableH).Width(ST)
                .RowHeight(rowH)
                .Virtualize()
                .Column("Asset", 2.2f)
                .Column("Type", 1f)
                .OnRowActivate(i => Selection.Ping(deps[i]))
                .RowCount(deps.Count)
                .CellContent((row, col) => DrawDepCell(paper, font, mono, rowH, deps[row], col, db))
                .Show();
        }
    }

    // Resolve raw dependency GUIDs to their true top-level asset (sub-assets -> parent), de-duplicated,
    // so the list shows real asset references instead of sub-asset GUIDs that read as "Missing".
    private void RebuildRefs(AssetEntry entry, EditorAssetDatabase db)
    {
        _cachedSceneGuid = entry.Guid;
        _refs.Clear();
        var seen = new System.Collections.Generic.HashSet<Guid>();
        foreach (var dep in entry.Dependencies)
        {
            if (dep == entry.Guid) continue; // don't list the scene itself

            Guid top;
            if (BuiltInAssets.IsBuiltIn(dep))
                top = dep;
            else
            {
                string? path = db.GuidToPathIncludingSubAssets(dep);
                top = path != null ? (db.GetEntry(path)?.Guid ?? dep) : dep;
            }

            if (seen.Add(top))
                _refs.Add(top);
        }
    }

    private static void DrawDepCell(Paper paper, Prowl.Scribe.FontFile font, Prowl.Scribe.FontFile mono,
        float rowH, Guid guid, int col, EditorAssetDatabase db)
    {
        string? path = db.GuidToPath(guid);
        bool builtIn = BuiltInAssets.IsBuiltIn(guid);

        string name;
        string typeLabel;
        if (builtIn)
        {
            name = BuiltInAssets.Entries.TryGetValue(guid, out var bi) ? bi.Name : guid.ToString()[..8];
            typeLabel = "Built-in";
        }
        else if (path != null)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path);
            typeLabel = db.GetEntry(path)?.MainAssetType?.Name ?? System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        }
        else
        {
            name = guid.ToString()[..8] + "...";
            typeLabel = "Missing";
        }

        if (col == 0)
        {
            var style = builtIn || path == null
                ? AssetTypeStyles.Folder
                : AssetTypeStyles.For(System.IO.Path.GetExtension(path), typeLabel);

            var ic = paper.Box($"dep_ic_{guid}").Width(20).Height(rowH).Margin(6, 0, 0, 0).IsNotInteractable();
            if (builtIn)
                ic.Text(EditorIcons.Star, font).TextColor(EditorTheme.Amber400).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter);
            else if (style.Badge != null)
                ic.Text(style.Badge, mono).TextColor(style.Color).FontSize(10f).Alignment(TextAlignment.MiddleCenter);
            else
                ic.Icon(paper, style.Icon, style.Color, size: 15f);

            paper.Box($"dep_nm_{guid}").Width(ST).Height(rowH).Margin(4, 6, 0, 0).IsNotInteractable()
                .Text(name, font).TextColor(path == null && !builtIn ? EditorTheme.Red400 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
        }
        else
        {
            paper.Box($"dep_ty_{guid}").Width(ST).Height(rowH).Margin(2, 6, 0, 0).IsNotInteractable()
                .Text(typeLabel, font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
        }
    }

}
