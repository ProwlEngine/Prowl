using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.Editor.Inspector;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Editor.Projects;
namespace Prowl.Editor.GUI.Panels;

public class InspectorPanel : DockPanel
{
    [MenuItem("Window/General/Inspector", priority: 3)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(InspectorPanel));

    public override string Title => Loc.Get("panel.inspector");
    public override string Icon => EditorIcons.Sliders;

    // Remember the last non-folder selection so navigating folders doesn't clear the inspector.
    private object? _lastInspectable;
    private bool _subscribed;

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        // Selection is global, so the Inspector is the natural owner of its persistence.
        // Only GameObjects round-trip here arbitrary objects (assets, etc.) would need
        // their own addressing scheme and currently aren't stable enough to restore.
        var arr = new System.Text.Json.Nodes.JsonArray();
        foreach (var go in Selection.GetSelected<GameObject>())
            arr.Add(go.Identifier.ToString());
        if (arr.Count == 0) return false;
        state["selection"] = arr;
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        if (state["selection"] is not System.Text.Json.Nodes.JsonArray arr) return;

        var scene = Runtime.Resources.Scene.Current;
        if (scene == null) return;

        bool first = true;
        foreach (var node in arr)
        {
            string? guidStr = node?.GetValue<string>();
            if (!Guid.TryParse(guidStr, out var guid)) continue;
            var go = scene.FindObjectByIdentifier<GameObject>(guid);
            if (go == null) continue;

            if (first) { Selection.Select(go); first = false; }
            else Selection.AddToSelection(go);
        }
    }

    private static bool IsFolderSelection(object? obj)
        => obj is ContentItem ci && ci.IsFolder;

    public override void OnClosed()
    {
        if (_subscribed)
        {
            Selection.OnSelectionChanged -= OnSelectionChanged;
            EditorApplication.PlayModeRequested -= OnPlayModeRequested;
            _subscribed = false;
        }
    }

    // The asset editor drawn last frame, so navigating away can offer to apply what it holds. Kept as a
    // triple because Apply and Revert need the same entry and asset the editor was drawn against.
    private AssetImporterEditor? _openEditor;
    private AssetEntry? _openEntry;
    private EngineObject? _openAsset;

    // What the inspector was showing, and whether the unapplied-changes prompt is up. While it is, the
    // selection change is held: the inspector keeps drawing the asset the prompt is about, so the choice
    // is made against what is on screen rather than behind a dialog for something already gone.
    private object? _openInspectable;
    private bool _promptOpen;

    /// <summary>The asset GUID <paramref name="active"/> inspects, or empty when it is not an asset.</summary>
    private static Guid InspectedAssetGuid(object? active)
    {
        if (active is not ContentItem item || item.IsFolder) return Guid.Empty;
        if (item.Guid != Guid.Empty) return item.Guid;
        return EditorAssetBackend.Instance?.GetEntry(item.RelativePath)?.Guid ?? Guid.Empty;
    }

    /// <summary>
    /// Raises the Apply/Revert prompt when the inspector moves off an asset whose editor still holds
    /// unwritten changes. Whether anything is pending is measured by diffing the editor's state against
    /// what was last written, so this cannot fire on an asset that was merely looked at.
    /// </summary>
    /// <summary>True when the prompt was raised, meaning the caller must keep showing the old asset.</summary>
    private bool PromptIfLeavingUnappliedEdits(Guid nowInspecting)
    {
        if (_openEditor == null || _openEntry == null) return false;
        if (_openEntry.Guid == nowInspecting) return false; // still on it

        AssetImporterEditor editor = _openEditor;
        AssetEntry entry = _openEntry;
        EngineObject? asset = _openAsset;
        _openEditor = null; _openEntry = null; _openAsset = null;

        if (!editor.HasPendingChanges(entry, asset)) return false;

        // Navigating away has already happened by the time this is asked, so dismissing it has to
        // mean something: it reverts, the same as the button does.
        RaiseUnappliedPrompt(editor, entry, asset, onResolved: null, cancellable: false);
        return true;
    }

    /// <summary>
    /// Raises the Apply/Revert choice for an editor holding unwritten changes.
    /// </summary>
    /// <param name="onResolved">Run once the user has chosen, for a caller that is waiting on the answer.</param>
    /// <param name="cancellable">
    /// Offers a way out that leaves the changes exactly as they are. For a prompt that is holding
    /// something up rather than reacting to something that has already happened, since backing out of
    /// pressing play should not be the same as throwing away what is in the inspector.
    /// </param>
    private void RaiseUnappliedPrompt(AssetImporterEditor editor, AssetEntry entry, EngineObject? asset,
        Action? onResolved, bool cancellable)
    {
        _promptOpen = true;

        string name = Path.GetFileName(entry.Path);
        DialogModal dialog = Origami.Dialog(Loc.Get("dialog.unapplied_settings"),
            p => Origami.Label(p, "insp_unapplied_msg", Loc.Get("dialog.unapplied_settings_body", new { name })).Show());

        dialog.CloseOnEscape = true;
        dialog.CloseOnBackdrop = false;

        dialog.OnDismissed = _ =>
        {
            _promptOpen = false;

            // Escaping a prompt that is holding something up means "not now", so it leaves both the
            // changes and whatever was waiting alone.
            if (!cancellable)
                editor.RevertPendingChanges(entry, asset);
        };

        dialog.Button(Loc.Get("dialog.apply"),
                () =>
                {
                    _promptOpen = false;
                    editor.ApplyPendingChanges(entry, asset);
                    Modal.Pop();
                    onResolved?.Invoke();
                }, OrigamiVariant.Primary)
            .Button(Loc.Get("dialog.revert"),
                () =>
                {
                    _promptOpen = false;
                    editor.RevertPendingChanges(entry, asset);
                    Modal.Pop();
                    onResolved?.Invoke();
                });

        if (cancellable)
            dialog.Button(Loc.Get("common.cancel"), () => { _promptOpen = false; Modal.Pop(); });
    }

    /// <summary>
    /// Holds a play mode transition while the inspector has edits nobody has decided about.
    /// </summary>
    /// <remarks>
    /// Both sides of a play session drop every loaded asset instance, so that a session starts from
    /// what is on disk and cannot leave anything behind. Unwritten inspector edits live in those
    /// instances, so without this they would go with them, and the only sign would be an inspector
    /// that quietly reads differently afterwards.
    /// </remarks>
    private void OnPlayModeRequested(PlayModeRequest request)
    {
        if (_promptOpen)
        {
            request.Defer("a choice about unapplied changes");
            return;
        }

        if (_openEditor == null || _openEntry == null) return;
        if (!_openEditor.HasPendingChanges(_openEntry, _openAsset)) return;

        AssetImporterEditor editor = _openEditor;
        AssetEntry entry = _openEntry;
        EngineObject? asset = _openAsset;
        bool entering = request.Entering;

        request.Defer($"unapplied changes to '{Path.GetFileName(entry.Path)}'");

        // Asking again rather than resuming a held transition: the answer runs every handler afresh,
        // so a second panel with something outstanding gets its turn instead of being skipped.
        RaiseUnappliedPrompt(editor, entry, asset,
            onResolved: () =>
            {
                if (entering) EditorApplication.RequestPlayMode();
                else EditorApplication.RequestExitPlayMode();
            },
            cancellable: true);
    }

    private void OnSelectionChanged()
    {
        var active = Selection.ActiveObject;

        // If the new selection is a folder (or all selected are folders), keep the
        // previous inspectable so browsing folders doesn't wipe the inspector.
        if (active == null || IsFolderSelection(active))
            return;

        _lastInspectable = active;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (!_subscribed)
        {
            Selection.OnSelectionChanged += OnSelectionChanged;
            EditorApplication.PlayModeRequested += OnPlayModeRequested;
            _subscribed = true;
        }

        Origami.ScrollView(paper, "insp_scroll", width, height).Padding(0, 0, 0, 0).Body(() =>
        {
            // Determine what to inspect: current selection, unless it's a folder
            var active = Selection.ActiveObject;
            if (active == null || IsFolderSelection(active))
                active = _lastInspectable;

            if (Selection.Count == 0 && _lastInspectable == null)
            {
                DrawEmpty(paper, font, width);
                return;
            }

            if (active == null)
            {
                DrawEmpty(paper, font, width);
                return;
            }

            // Leaving an asset whose import settings were edited but not applied: offer to write them or
            // put them back. The selection change is queued while the prompt is up - the inspector keeps
            // drawing the asset in question so the choice is made against what is actually on screen.
            if (_promptOpen)
            {
                if (_openInspectable != null) active = _openInspectable;
            }
            else if (PromptIfLeavingUnappliedEdits(InspectedAssetGuid(active)))
            {
                active = _openInspectable ?? active;
            }
            else
            {
                _openInspectable = active;
            }

            // Draw based on type GameObject has its own header
            if (active is GameObject gameObject)
            {
                var gos = Selection.GetSelected<GameObject>().ToList();
                if (gos.Count > 1)
                    GameObjectInspector.DrawMulti(paper, font, gos);
                else
                    GameObjectInspector.Draw(paper, font, gameObject);
            }
            else
            {
                DrawSelectionHeader(paper, font, active);
                Origami.Separator(paper, "insp_sep_header").Show();

                if (active is ContentItem contentItem)
                    DrawAssetInspector(paper, font, contentItem);
                else if (active is ConsoleLogSelection logEntry)
                    DrawConsoleLogInspector(paper, font, logEntry);
                else if (active is EngineObject engineObj)
                    DrawEngineObjectInspector(paper, font, engineObj);
                else
                    DrawGenericInspector(paper, font, active);
            }

            // Multi-selection summary (GameObjects already get a full multi-object inspector above)
            if (Selection.Count > 1 && active is not GameObject)
            {
                                Origami.Header(paper, "insp_h_multi", Loc.Get("inspector.selection")).Underline().Show();
                Origami.Label(paper, "insp_multi_count", $"{Selection.Count} {Loc.Get("inspector.objects_selected")}").Show();

                for (int i = 0; i < Selection.Count && i < 20; i++)
                {
                    var obj = Selection.Selected[i];
                    string name = obj switch
                    {
                        ContentItem ci => $"{(ci.IsFolder ? EditorIcons.Folder : GetExtensionIcon(Path.GetExtension(ci.Name).ToLowerInvariant()))} {ci.Name}",
                        EngineObject eo => $"{EditorIcons.Cube} {eo.Name}",
                        _ => obj.ToString() ?? Loc.Get("inspector.unknown")
                    };
                    Origami.Label(paper, $"insp_sel_{i}", name).Show();
                }

                if (Selection.Count > 20)
                    Origami.Label(paper, "insp_more", Loc.Get("inspector.and_more", new { count = Selection.Count - 20 })).Show();
            }

            paper.Box("insp_bottom_pad").Height(20);
        });

        // Drag a script (.cs) from the Project panel onto the inspector to add it as a component.
        DrawScriptComponentDropZone(paper, font, width, height);
    }

    /// <summary>
    /// While a single GameObject is selected and a script asset that resolves to a component type is
    /// being dragged, overlays the inspector with a drop target that adds that component on drop.
    /// </summary>
    private static void DrawScriptComponentDropZone(Paper paper, Scribe.FontFile font, float width, float height)
    {
        if (!DragDrop.IsDragging && !DragDrop.IsDropFrame) return;
        if (DragDrop.Payload is not AssetDragPayload payload) return;

        var go = Selection.ActiveObject as GameObject;
        if (go == null || Selection.GetSelected<GameObject>().Count() != 1) return;

        Type? componentType = Prowl.Editor.Projects.Scripting.ScriptComponentResolver.ResolveComponentType(payload);
        if (componentType == null) return;

        using (paper.Box("insp_script_drop")
            .PositionType(PositionType.SelfDirected).Position(0, 0).Size(width, height)
            .Layer(Layer.Overlay)
            .BackgroundColor(Color.FromArgb(38, EditorTheme.Purple400))
            .BorderColor(EditorTheme.Purple400).BorderWidth(2).Rounded(6)
            .Enter())
        {
            paper.Box("insp_script_drop_lbl")
                .Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).IsNotInteractable()
                .Text($"{EditorIcons.Plus}  {Loc.Get("inspector.add_component")}: {componentType.Name}", font)
                .TextColor(EditorTheme.Purple400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);

            // Complete the drop: the drag has ended (released) while over this overlay.
            if (paper.IsParentHovered && !DragDrop.IsDragging)
            {
                GameObjectInspector.AddComponentWithUndo(go, componentType);
                DragDrop.EndDrag();
            }
        }
    }

    private void DrawEmpty(Paper paper, Scribe.FontFile font, float width)
    {
        paper.Box("insp_empty").Height(80)
            .Text(Loc.Get("inspector.nothing_selected"), font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);

        paper.Box("insp_hint").Height(30)
            .Text(Loc.Get("inspector.nothing_selected_hint"), font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter);
    }

    private void DrawSelectionHeader(Paper paper, Scribe.FontFile font, object active)
    {
        string icon;
        string name;
        string typeName;

        if (active is ContentItem ci)
        {
            icon = ci.IsFolder ? EditorIcons.Folder : GetExtensionIcon(Path.GetExtension(ci.Name).ToLowerInvariant());
            name = ci.Name;
            typeName = ci.IsFolder ? Loc.Get("inspector.folder") : ci.TypeLabel;
        }
        else if (active is EngineObject eo)
        {
            icon = EditorIcons.Cube;
            name = eo.Name;
            typeName = eo.GetType().Name;
        }
        else
        {
            icon = EditorIcons.CircleInfo;
            name = active.ToString() ?? Loc.Get("inspector.unknown");
            typeName = active.GetType().Name;
        }

        using (paper.Row("insp_header")
            .Height(40).Padding(4, 0, 4, 4).RowBetween(8)
            .Enter())
        {
            // Large icon
            paper.Box("insp_h_icon")
                .Width(32).Height(32)
                .BackgroundColor(EditorTheme.Hover)
                .Rounded(6)
                .Text(icon, font)
                .TextColor(EditorTheme.Purple400)
                .FontSize(18f)
                .Alignment(TextAlignment.MiddleCenter);

            using (paper.Column("insp_h_info").Height(32).ColBetween(1).Enter())
            {
                paper.Box("insp_h_name")
                    .Height(18)
                    .Text(name, font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box("insp_h_type")
                    .Height(14)
                    .Text(typeName, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    private void DrawAssetInspector(Paper paper, Scribe.FontFile font, ContentItem item)
    {
        if (item.IsFolder)
        {
            DrawFolderInfo(paper, font, item);
            return;
        }

        var db = EditorAssetBackend.Instance;
        if (db == null) return;

        // Sub-asset: show read-only view with Extract button
        if (item.IsSubAsset)
        {
            DrawSubAssetInspector(paper, font, item, db);
            return;
        }

        var entry = db.GetEntry(item.RelativePath);

        // Check for custom asset editor
        if (entry?.MainAssetType != null)
        {
            var assetEditor = EditorRegistries.GetAssetEditor(entry);
            if (assetEditor != null)
            {
                var asset = Runtime.AssetDatabase.Get(item.Guid != Guid.Empty ? item.Guid : entry.Guid);

                // Remembered so moving off this asset can still reach its editor to apply or revert.
                _openEditor = assetEditor;
                _openEntry = entry;
                _openAsset = asset;

                try { assetEditor.OnGUI(paper, "insp_asset_editor", entry, asset); }
                catch (Exception ex)
                {
                    Runtime.Debug.LogError($"[Inspector] Asset editor for {entry.MainAssetType.Name} threw and was skipped: {ex.Message}");
                }
                return;
            }
        }

        // No editor is registered for this type, so show what the asset actually holds before its
        // file metadata. Without this a custom EngineObject asset shows only its path and guid, and
        // every field on it needs a hand-written editor before it can be seen or edited at all.
        DrawAssetFieldsFallback(paper, item, entry);

        // Generic asset info
        Origami.Header(paper, "insp_h_asset", Loc.Get("inspector.asset_info")).Show();

        Origami.Label(paper, "insp_path", $"{Loc.Get("inspector.path")}: {item.RelativePath}").Show();

        if (entry != null)
        {
            Origami.Label(paper, "insp_guid", $"{Loc.Get("inspector.guid")}: {entry.Guid}").Show();
            Origami.Label(paper, "insp_importer", $"{Loc.Get("inspector.importer")}: {entry.ImporterType}").Show();

            if (entry.MainAssetType != null)
                Origami.Label(paper, "insp_maintype", $"{Loc.Get("inspector.type")}: {entry.MainAssetType.Name}").Show();

            // Last modified
            var lastMod = new DateTime(entry.LastModifiedTicks, DateTimeKind.Utc).ToLocalTime();
            Origami.Label(paper, "insp_lastmod", $"{Loc.Get("inspector.modified")}: {lastMod:yyyy-MM-dd HH:mm:ss}").Show();

            // Dependencies
            if (entry.Dependencies.Length > 0)
            {
                Origami.Separator(paper, "insp_sep_deps").Show();
                Origami.Header(paper, "insp_h_deps", $"{Loc.Get("inspector.dependencies")} ({entry.Dependencies.Length})").Show();
                for (int i = 0; i < entry.Dependencies.Length && i < 20; i++)
                {
                    var depGuid = entry.Dependencies[i];
                    DrawAssetLink(paper, font, $"insp_dep_{i}", depGuid, db);
                }
            }

            // Dependents (who references this asset)
            var dependents = db.Dependencies.GetDependents(entry.Guid);
            if (dependents.Count > 0)
            {
                Origami.Separator(paper, "insp_sep_refs").Show();
                Origami.Header(paper, "insp_h_refs", $"{Loc.Get("inspector.used_by")} ({dependents.Count})").Show();
                int count = 0;
                foreach (var depGuid in dependents)
                {
                    if (count >= 20) break;
                    DrawAssetLink(paper, font, $"insp_ref_{count}", depGuid, db);
                    count++;
                }
            }
        }

        // Import Settings
        if (entry != null && Project.Current != null)
        {
            string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
            string metaPath = MetaFile.GetMetaPath(absPath);

            if (File.Exists(metaPath))
            {
                var meta = MetaFile.Read(metaPath);
                var importer = EditorRegistries.CreateImporterByName(entry.ImporterType);

                if (importer != null)
                {
                    // Ensure settings exist (use defaults if missing)
                    var settings = meta.Settings ?? importer.DefaultSettings();
                    if (settings != null && settings.TagType == Echo.EchoType.Compound)
                    {
                                                Origami.Header(paper, "insp_h_settings", $"{EditorIcons.Gear}  {Loc.Get("inspector.import_settings")}").Underline().Show();

                        foreach (var kvp in settings.Tags.ToList())
                        {
                            string key = kvp.Key;
                            var val = kvp.Value;

                            switch (val.TagType)
                            {
                                case Echo.EchoType.Bool:
                                    Origami.Checkbox(paper, $"insp_set_{key}", val.BoolValue,
                                            v => { settings[key] = new Echo.EchoObject(v); })
                                        .LabelRight(PropertyGridUtils.NicifyName(key)).Show();
                                    break;

                                case Echo.EchoType.Int:
                                    EditorGUI.Row(paper, $"insp_set_{key}", PropertyGridUtils.NicifyName(key), () =>
                                        Origami.NumericField<int>(paper, $"insp_set_{key}_v", val.IntValue,
                                            v => { settings[key] = new Echo.EchoObject(v); }).Show());
                                    break;

                                case Echo.EchoType.Float:
                                    EditorGUI.Row(paper, $"insp_set_{key}", PropertyGridUtils.NicifyName(key), () =>
                                        Origami.NumericField<float>(paper, $"insp_set_{key}_v", val.FloatValue,
                                            v => { settings[key] = new Echo.EchoObject(v); }).Show());
                                    break;

                                case Echo.EchoType.String:
                                    EditorGUI.Row(paper, $"insp_set_{key}", PropertyGridUtils.NicifyName(key), () =>
                                        Origami.TextField(paper, $"insp_set_{key}_v", val.StringValue,
                                            v => { settings[key] = new Echo.EchoObject(v); }).Show());
                                    break;
                            }
                        }

                        // Save & Reimport button
                        paper.Box("insp_set_sp").Height(4);
                        Origami.Button(paper, "insp_set_save", $"{EditorIcons.FloppyDisk}  {Loc.Get("inspector.save_and_reimport")}", () =>
                        {
                            meta.Settings = settings;
                            MetaFile.Write(metaPath, meta);
                            db.Reimport(entry.Guid);
                        }).Width(150).Show();
                    }
                }
            }
        }

        // Reimport button
        Origami.Separator(paper, "insp_sep_actions").Show();
        if (entry != null)
        {
            Origami.Button(paper, "insp_reimport", $"{EditorIcons.ArrowsRotate}  {Loc.Get("inspector.reimport")}", () => db.Reimport(entry.Guid)).Show();
        }
    }



    private void DrawFolderInfo(Paper paper, Scribe.FontFile font, ContentItem item)
    {
        Origami.Header(paper, "insp_h_folder", Loc.Get("inspector.folder")).Show();
        Origami.Label(paper, "insp_folder_path", $"{Loc.Get("inspector.path")}: {item.RelativePath}").Show();

        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, item.RelativePath);
        if (!Directory.Exists(absPath)) return;

        var counts = GetFolderCounts(item.RelativePath, absPath);
        if (counts == null) return;

        Origami.Label(paper, "insp_folder_files", $"{Loc.Get("inspector.files")}: {counts.Value.Files}").Show();
        Origami.Label(paper, "insp_folder_folders", $"{Loc.Get("inspector.subfolders")}: {counts.Value.Folders}").Show();
    }

    // Recursive folder counts are cached rather than walked every frame, since the inspector redraws
    // continuously while a folder stays selected. Dropped whenever the asset database content moves.
    private static readonly Dictionary<string, (int Files, int Folders)> _folderCounts = [];
    private static int _folderCountsVersion = -1;

    private static (int Files, int Folders)? GetFolderCounts(string relativePath, string absPath)
    {
        int version = EditorAssetBackend.Instance?.ContentVersion ?? -1;
        if (_folderCountsVersion != version)
        {
            _folderCounts.Clear();
            _folderCountsVersion = version;
        }

        if (_folderCounts.TryGetValue(relativePath, out var cached)) return cached;

        try
        {
            int files = Directory.GetFiles(absPath, "*", SearchOption.AllDirectories)
                .Count(f => !f.EndsWith(".meta"));
            int folders = Directory.GetDirectories(absPath, "*", SearchOption.AllDirectories).Length;
            var counts = (files, folders);
            _folderCounts[relativePath] = counts;
            return counts;
        }
        catch { return null; }
    }

    private void DrawSubAssetInspector(Paper paper, Scribe.FontFile font, ContentItem item, EditorAssetBackend db)
    {
        // Find the parent entry
        AssetEntry? parentEntry = null;
        SubAssetEntry? subEntry = null;
        foreach (var e in db.GetAllEntries())
        {
            if (e.SubAssets == null) continue;
            var match = e.SubAssets.FirstOrDefault(s => s.Guid == item.Guid);
            if (match != null)
            {
                parentEntry = e;
                subEntry = match;
                break;
            }
        }

        // Header with sub-asset badge
        using (paper.Row("insp_sub_header").Height(28).ChildLeft(8).RowBetween(6).Enter())
        {
            paper.Box("insp_sub_badge")
                .Width(UnitValue.Auto).Height(20)
                .ChildLeft(6).ChildRight(6)
                .BackgroundColor(EditorTheme.Selected)
                .Rounded(4)
                .Text(Loc.Get("inspector.sub_asset"), font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("insp_sub_name")
                .Height(28)
                .Text(item.Name, font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);
        }

        Origami.Separator(paper, "insp_sub_sep1").Show();

        // Info
        Origami.Label(paper, "insp_sub_type", $"{Loc.Get("inspector.type")}: {item.TypeLabel}").Show();
        Origami.Label(paper, "insp_sub_guid", $"{Loc.Get("inspector.guid")}: {item.Guid}").Show();
        if (parentEntry != null)
            Origami.Label(paper, "insp_sub_parent", $"{Loc.Get("inspector.parent")}: {parentEntry.Path}").Show();

        Origami.Separator(paper, "insp_sub_sep2").Show();

        // A sub-asset uses the SAME custom editor as a main asset of its type, just wrapped in a read-only
        // scope so Origami widgets are disabled. Editors that draw non-Origami interactive elements gate them
        // on Origami.IsReadOnly themselves (e.g. TextureAssetEditor's Save button).
        var asset = Runtime.AssetDatabase.Get(item.Guid);
        if (asset != null)
        {
            var subEditor = parentEntry != null ? EditorRegistries.GetAssetEditor(asset.GetType()) : null;
            if (subEditor != null)
            {
                Origami.BeginReadOnly();
                try { subEditor.OnGUI(paper, "insp_sub_editor", parentEntry!, asset); }
                finally { Origami.EndReadOnly(); }
            }
            else
            {
                // Generic read-only property grid for types without a custom editor.
                Origami.Header(paper, "insp_sub_h_props", Loc.Get("inspector.properties_readonly")).Show();
                var fields = GUI.PropertyGridUtils.GetSerializableFields(asset.GetType());
                foreach (var field in fields)
                {
                    object? val = field.GetValue(asset);
                    string label = GUI.PropertyGridUtils.NicifyName(field.Name);
                    Origami.Label(paper, $"insp_sub_prop_{field.Name}", $"{label}: {val ?? "(null)"}").Show();
                }
            }
        }

        // Dependencies (what this sub-asset itself references, e.g. a Sprite's own Texture)
        if (subEntry?.Dependencies.Length > 0)
        {
            Origami.Separator(paper, "insp_sub_sep_deps").Show();
            Origami.Header(paper, "insp_sub_h_deps", $"{Loc.Get("inspector.dependencies")} ({subEntry.Dependencies.Length})").Show();
            for (int i = 0; i < subEntry.Dependencies.Length && i < 20; i++)
                DrawAssetLink(paper, font, $"insp_sub_dep_{i}", subEntry.Dependencies[i], db);
        }

        // Dependents (who references this sub-asset directly, e.g. a UIImage pointing at this Sprite)
        var subDependents = db.Dependencies.GetDependents(item.Guid);
        if (subDependents.Count > 0)
        {
            Origami.Separator(paper, "insp_sub_sep_refs").Show();
            Origami.Header(paper, "insp_sub_h_refs", $"{Loc.Get("inspector.used_by")} ({subDependents.Count})").Show();
            int refCount = 0;
            foreach (var depGuid in subDependents)
            {
                if (refCount >= 20) break;
                DrawAssetLink(paper, font, $"insp_sub_ref_{refCount}", depGuid, db);
                refCount++;
            }
        }

        Origami.Separator(paper, "insp_sub_sep3").Show();

        // Extract button clone sub-asset to a standalone file
        Origami.Button(paper, "insp_sub_extract", $"{EditorIcons.FileExport}  {Loc.Get("inspector.extract_as_asset")}", () => ExtractSubAsset(item, parentEntry, subEntry, asset)).Show();
    }

    private void ExtractSubAsset(ContentItem item, AssetEntry? parentEntry, SubAssetEntry? subEntry, EngineObject? asset)
    {
        if (asset == null || parentEntry == null || Project.Current == null) return;

        var db = EditorAssetBackend.Instance;
        if (db == null) return;

        // Determine target path same folder as parent, with sub-asset name
        string parentDir = Path.GetDirectoryName(parentEntry.Path)?.Replace('\\', '/') ?? "";
        string ext = asset switch
        {
            Prowl.Runtime.Resources.Material => ".mat",
            Prowl.Runtime.Resources.Mesh => ".mesh",
            Prowl.Runtime.AnimationClip => ".anim",
            Prowl.Runtime.Resources.Texture2D => ".png",
            _ => ".asset"
        };

        string fileName = $"{item.Name}{ext}";
        string relativePath = string.IsNullOrEmpty(parentDir) ? fileName : $"{parentDir}/{fileName}";

        // Serialize the asset to the file
        // Clear the sub-asset's AssetID so it serializes as a full object, not a reference
        var originalId = asset.AssetID;
        asset.AssetID = Guid.Empty;
        try
        {
            var echo = Echo.Serializer.Serialize(typeof(object), asset);

            if (echo != null)
            {
                string absPath = Path.Combine(Project.Current.AssetsPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
                File.WriteAllText(absPath, echo.WriteToString());
                Runtime.Debug.Log($"Extracted sub-asset '{item.Name}' to '{relativePath}'");
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to extract sub-asset: {ex.Message}");
        }
        finally
        {
            asset.AssetID = originalId; // Restore
        }
    }

    /// <summary>Assets with edits not yet written, so moving away and back keeps the Save button up.</summary>
    private readonly HashSet<Guid> _unsavedAssets = [];

    /// <summary>
    /// Draws an asset's own serialized fields for types that have no editor of their own, which is
    /// what makes a custom <see cref="EngineObject"/> asset editable without writing one.
    /// </summary>
    private void DrawAssetFieldsFallback(Paper paper, ContentItem item, AssetEntry? entry)
    {
        if (entry?.MainAssetType == null) return;
        if (!typeof(EngineObject).IsAssignableFrom(entry.MainAssetType)) return;

        Guid guid = item.Guid != Guid.Empty ? item.Guid : entry.Guid;
        EngineObject? asset = Runtime.AssetDatabase.Get(guid);
        if (asset.IsNotValid()) return;

        Origami.Header(paper, "insp_h_fields", Loc.Get("inspector.properties")).Underline().Show();
        PropertyGridUtils.Draw(paper, "insp_asset_fields", asset, _ => _unsavedAssets.Add(guid));

        if (Origami.IsReadOnly || !_unsavedAssets.Contains(guid)) return;

        var db = EditorAssetBackend.Instance;
        if (db == null) return;

        // Edits live on the cached instance until they are written, so leaving the asset selected
        // does not lose them, but nothing else will write them either.
        using (paper.Row("insp_asset_fields_bar").Height(UnitValue.Auto).RowBetween(8).Enter())
        {
            Origami.Button(paper, "insp_asset_fields_save",
                $"{EditorIcons.FloppyDisk}  {Loc.Get("inspector.save_and_reimport")}", () =>
                {
                    db.SaveAsset(asset);
                    _unsavedAssets.Remove(guid);
                }).Show();

            Origami.Button(paper, "insp_asset_fields_revert",
                $"{EditorIcons.ArrowsRotate}  {Loc.Get("dialog.revert")}", () =>
                {
                    db.Reimport(guid);
                    _unsavedAssets.Remove(guid);
                }).Show();
        }
    }

    private void DrawEngineObjectInspector(Paper paper, Scribe.FontFile font, EngineObject obj)
    {
        Origami.Header(paper, "insp_h_eo", obj.GetType().Name).Show();

        Origami.Label(paper, "insp_eo_name", $"{Loc.Get("inspector.name")}: {obj.Name}").Show();
        Origami.Label(paper, "insp_eo_id", $"{Loc.Get("inspector.instance_id")}: {obj.InstanceID}").Show();

        if (obj.AssetID != Guid.Empty)
            Origami.Label(paper, "insp_eo_assetid", $"{Loc.Get("inspector.asset_id")}: {obj.AssetID}").Show();
        if (!string.IsNullOrEmpty(obj.AssetPath))
            Origami.Label(paper, "insp_eo_assetpath", $"{Loc.Get("inspector.asset_path")}: {obj.AssetPath}").Show();

        // Use PropertyGrid for reflection-based editing
                Origami.Header(paper, "insp_h_props", Loc.Get("inspector.properties")).Underline().Show();
        PropertyGridUtils.Draw(paper, "insp_pg", obj);
    }

    private void DrawConsoleLogInspector(Paper paper, Scribe.FontFile font, ConsoleLogSelection log)
    {
        float fs = EditorTheme.FontSize;

        // Severity icon + label
        string icon = log.Severity switch
        {
            LogSeverity.Warning => EditorIcons.TriangleExclamation,
            LogSeverity.Error or LogSeverity.Exception => EditorIcons.CircleExclamation,
            LogSeverity.Success => EditorIcons.CircleCheck,
            _ => EditorIcons.CircleInfo
        };
        var textColor = log.Severity switch
        {
            LogSeverity.Warning => EditorTheme.Amber400,
            LogSeverity.Error or LogSeverity.Exception => EditorTheme.Red400,
            LogSeverity.Success => EditorTheme.Green400,
            _ => EditorTheme.Ink500
        };

        Origami.Header(paper, "log_hdr", $"{icon}  {log.Severity}").Show();

        // Time + count
        using (paper.Row("log_meta").Height(EditorTheme.RowHeight).RowBetween(8).Enter())
        {
            Origami.Label(paper, "log_time", $"{Loc.Get("inspector.time")}: {log.Time}").Show();
            if (log.Count > 1)
                Origami.Label(paper, "log_count", $"{Loc.Get("inspector.count")}: {log.Count}").Show();
        }

        Origami.Separator(paper, "log_sep1").Show();

        // Full message (word-wrapped)
        Origami.Header(paper, "log_msg_hdr", Loc.Get("inspector.message")).Show();
        paper.Box("log_msg")
            .Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(40)
            .BackgroundColor(EditorTheme.Neutral400).Rounded(3)
            .Padding(8, 8, 6, 6)
            .Text(log.Message, font).TextColor(textColor)
            .Wrap(Scribe.TextWrapMode.Wrap)
            .FontSize(fs - 1);

        // Stack trace
        if (log.StackTrace != null && log.StackTrace.StackFrames.Length > 0)
        {
            paper.Box("log_sp").Height(8);
                        Origami.Header(paper, "log_st_hdr", Loc.Get("inspector.stack_trace")).Underline().Show();

            for (int i = 0; i < log.StackTrace.StackFrames.Length; i++)
            {
                var frame = log.StackTrace.StackFrames[i];
                string frameText = frame.ToString();

                var frameBg = i % 2 == 0
                    ? System.Drawing.Color.FromArgb(8, 255, 255, 255)
                    : System.Drawing.Color.Transparent;

                paper.Box($"log_frame_{i}")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(18)
                    .BackgroundColor(frameBg).Rounded(2)
                    .Padding(8, 0, 2, 2)
                    .Text(frameText, font).TextColor(EditorTheme.Ink400)
                    .FontSize(fs - 2);
            }
        }
    }

    private void DrawGenericInspector(Paper paper, Scribe.FontFile font, object obj)
    {
        Origami.Header(paper, "insp_h_generic", obj.GetType().Name).Show();
        Origami.Label(paper, "insp_generic_str", obj.ToString() ?? "null").Show();

                Origami.Header(paper, "insp_h_gprops", Loc.Get("inspector.properties")).Underline().Show();
        PropertyGridUtils.Draw(paper, "insp_gpg", obj);
    }

    private static string GetExtensionIcon(string ext) => EditorRegistries.GetFileIconForExtension(ext);

    private static void DrawAssetLink(Paper paper, Scribe.FontFile font, string id, Guid guid, EditorAssetBackend db)
    {
        string? path = db.GuidToPath(guid);
        bool isBuiltIn = Runtime.BuiltInAssets.IsBuiltIn(guid);
        string displayName;
        string icon;

        if (isBuiltIn)
        {
            var entries = Runtime.BuiltInAssets.Entries;
            displayName = entries.TryGetValue(guid, out var bi) ? bi.Name : guid.ToString()[..8];
            icon = EditorIcons.Star;
        }
        else if (path != null)
        {
            displayName = System.IO.Path.GetFileName(path);
            icon = GetExtensionIcon(System.IO.Path.GetExtension(path).ToLowerInvariant());
        }
        else
        {
            displayName = guid.ToString()[..8] + "...";
            icon = EditorIcons.CircleQuestion;
        }

        paper.Box(id)
            .Height(EditorTheme.RowHeight).ChildLeft(8).Rounded(3)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .Text($"{icon}  {displayName}", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSizeSmall)
            .Alignment(PaperUI.TextAlignment.MiddleLeft)
            .OnClick(guid, (g, _) => Selection.Ping(g));
    }
}
