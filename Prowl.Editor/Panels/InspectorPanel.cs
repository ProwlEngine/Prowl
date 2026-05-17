using System;
using System.IO;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
namespace Prowl.Editor.Panels;

[EditorWindow("General/Inspector")]
public class InspectorPanel : DockPanel
{
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
            _subscribed = true;
        }

        Origami.ScrollView(paper, "insp_scroll", width, height).Padding(8, 0, 8, 0).Body(() =>
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

            // Draw based on type GameObject has its own header
            if (active is GameObject gameObject)
            {
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

            // Multi-selection summary
            if (Selection.Count > 1)
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
                        _ => obj.ToString() ?? "Unknown"
                    };
                    Origami.Label(paper, $"insp_sel_{i}", name).Show();
                }

                if (Selection.Count > 20)
                    Origami.Label(paper, "insp_more", Loc.Get("inspector.and_more", new { count = Selection.Count - 20 })).Show();
            }

            paper.Box("insp_bottom_pad").Height(20);
        });
    }

    private void DrawEmpty(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        paper.Box("insp_empty").Height(80)
            .Text(Loc.Get("inspector.nothing_selected"), font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);

        paper.Box("insp_hint").Height(30)
            .Text(Loc.Get("inspector.nothing_selected_hint"), font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize - 4)
            .Alignment(TextAlignment.MiddleCenter);
    }

    private void DrawSelectionHeader(Paper paper, Prowl.Scribe.FontFile font, object active)
    {
        string icon;
        string name;
        string typeName;

        if (active is ContentItem ci)
        {
            icon = ci.IsFolder ? EditorIcons.Folder : GetExtensionIcon(Path.GetExtension(ci.Name).ToLowerInvariant());
            name = ci.Name;
            typeName = ci.IsFolder ? "Folder" : ci.TypeLabel;
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
            name = active.ToString() ?? "Unknown";
            typeName = active.GetType().Name;
        }

        using (paper.Row("insp_header")
            .Height(40).Padding(4, 0, 4, 4).RowBetween(8)
            .Enter())
        {
            // Large icon
            paper.Box("insp_h_icon")
                .Width(32).Height(32)
                .BackgroundColor(Color.FromArgb(30, 255, 255, 255))
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
                    .FontSize(EditorTheme.FontSize - 4)
                    .Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    private void DrawAssetInspector(Paper paper, Prowl.Scribe.FontFile font, ContentItem item)
    {
        if (item.IsFolder)
        {
            DrawFolderInfo(paper, font, item);
            return;
        }

        var db = EditorAssetDatabase.Instance;
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
            var assetEditor = AssetImporterEditorRegistry.GetEditor(entry.MainAssetType);
            if (assetEditor != null)
            {
                var asset = Runtime.AssetDatabase.Get(item.Guid != Guid.Empty ? item.Guid : entry.Guid);
                assetEditor.OnGUI(paper, "insp_asset_editor", entry, asset);
                return;
            }
        }

        // Fallback: generic asset info
        Origami.Header(paper, "insp_h_asset", Loc.Get("inspector.asset_info")).Show();

        Origami.Label(paper, "insp_path", $"{Loc.Get("inspector.path")}: {item.RelativePath}").Show();

        if (entry != null)
        {
            Origami.Label(paper, "insp_guid", $"GUID: {entry.Guid}").Show();
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
                var importer = Importers.ImporterRegistry.CreateByTypeName(entry.ImporterType);

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
                                        .LabelRight(NicifySettingName(key)).Show();
                                    break;

                                case Echo.EchoType.Int:
                                    InspectorRow.Draw(paper, $"insp_set_{key}", NicifySettingName(key), () =>
                                        Origami.NumericField<int>(paper, $"insp_set_{key}_v", val.IntValue,
                                            v => { settings[key] = new Echo.EchoObject(v); }).Show());
                                    break;

                                case Echo.EchoType.Float:
                                    InspectorRow.Draw(paper, $"insp_set_{key}", NicifySettingName(key), () =>
                                        Origami.NumericField<float>(paper, $"insp_set_{key}_v", val.FloatValue,
                                            v => { settings[key] = new Echo.EchoObject(v); }).Show());
                                    break;

                                case Echo.EchoType.String:
                                    InspectorRow.Draw(paper, $"insp_set_{key}", NicifySettingName(key), () =>
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

    private static string NicifySettingName(string name)
    {
        // "generateMipmaps" -> "Generate Mipmaps"
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder();
        sb.Append(char.ToUpper(name[0]));
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    private void DrawFolderInfo(Paper paper, Prowl.Scribe.FontFile font, ContentItem item)
    {
        Origami.Header(paper, "insp_h_folder", Loc.Get("inspector.folder")).Show();
        Origami.Label(paper, "insp_folder_path", $"{Loc.Get("inspector.path")}: {item.RelativePath}").Show();

        string absPath = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
        if (Directory.Exists(absPath))
        {
            try
            {
                int fileCount = Directory.GetFiles(absPath, "*", SearchOption.AllDirectories)
                    .Count(f => !f.EndsWith(".meta"));
                int folderCount = Directory.GetDirectories(absPath, "*", SearchOption.AllDirectories).Length;
                Origami.Label(paper, "insp_folder_files", $"{Loc.Get("inspector.files")}: {fileCount}").Show();
                Origami.Label(paper, "insp_folder_folders", $"{Loc.Get("inspector.subfolders")}: {folderCount}").Show();
            }
            catch { }
        }
    }

    private void DrawSubAssetInspector(Paper paper, Prowl.Scribe.FontFile font, ContentItem item, EditorAssetDatabase db)
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
                .BackgroundColor(System.Drawing.Color.FromArgb(255, 80, 80, 100))
                .Rounded(4)
                .Text(Loc.Get("inspector.sub_asset"), font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 3)
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
        Origami.Label(paper, "insp_sub_guid", $"GUID: {item.Guid}").Show();
        if (parentEntry != null)
            Origami.Label(paper, "insp_sub_parent", $"{Loc.Get("inspector.parent")}: {parentEntry.Path}").Show();

        Origami.Separator(paper, "insp_sub_sep2").Show();

        // Load the sub-asset and show its properties read-only
        var asset = Runtime.AssetDatabase.Get(item.Guid);
        if (asset != null)
        {
            // Show type-specific preview
            if (asset is Prowl.Runtime.Resources.Texture2D tex)
            {
                float previewSize = 180f;
                float aspect = tex.Width / (float)Math.Max(1, tex.Height);
                float pw = aspect >= 1 ? previewSize : previewSize * aspect;
                float ph = aspect >= 1 ? previewSize / aspect : previewSize;

                paper.Box("insp_sub_tex_preview")
                    .Size(pw, ph)
                    .Margin(8, 4, 8, 4)
                    .BackgroundColor(System.Drawing.Color.FromArgb(255, 20, 20, 22))
                    .Rounded(4)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        canvas.DrawImage(tex,
                            (float)r.Min.X, (float)r.Min.Y,
                            (float)r.Size.X, (float)r.Size.Y);
                    }));

                Origami.Label(paper, "insp_sub_tex_size", $"{Loc.Get("inspector.size")}: {tex.Width} x {tex.Height}").Show();
                Origami.Label(paper, "insp_sub_tex_fmt", $"{Loc.Get("inspector.format")}: {tex.ImageFormat}").Show();
            }
            else if (asset is Prowl.Runtime.Resources.Mesh mesh && parentEntry != null && subEntry != null)
            {
                Inspector.MeshAssetEditor.DrawForSubAsset(paper, "insp_sub_mesh", parentEntry, subEntry, mesh);
            }
            else
            {
                // Generic read-only property grid
                Origami.Header(paper, "insp_sub_h_props", Loc.Get("inspector.properties_readonly")).Show();
                // Show properties as labels
                var fields = GUI.PropertyGridUtils.GetSerializableFields(asset.GetType());
                foreach (var field in fields)
                {
                    object? val = field.GetValue(asset);
                    string label = GUI.PropertyGridUtils.NicifyName(field.Name);
                    Origami.Label(paper, $"insp_sub_prop_{field.Name}", $"{label}: {val ?? "(null)"}").Show();
                }
            }
        }

        Origami.Separator(paper, "insp_sub_sep3").Show();

        // Extract button clone sub-asset to a standalone file
        Origami.Button(paper, "insp_sub_extract", $"{EditorIcons.FileExport}  {Loc.Get("inspector.extract_as_asset")}", () => ExtractSubAsset(item, parentEntry, subEntry, asset)).Show();
    }

    private void ExtractSubAsset(ContentItem item, AssetEntry? parentEntry, SubAssetEntry? subEntry, EngineObject? asset)
    {
        if (asset == null || parentEntry == null || Project.Current == null) return;

        var db = EditorAssetDatabase.Instance;
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
        try
        {
            // Clear the sub-asset's AssetID so it serializes as a full object, not a reference
            var originalId = asset.AssetID;
            asset.AssetID = Guid.Empty;

            var echo = Echo.Serializer.Serialize(typeof(object), asset);
            asset.AssetID = originalId; // Restore

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
    }

    private void DrawEngineObjectInspector(Paper paper, Prowl.Scribe.FontFile font, EngineObject obj)
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

    private void DrawConsoleLogInspector(Paper paper, Prowl.Scribe.FontFile font, ConsoleLogSelection log)
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
            LogSeverity.Warning => System.Drawing.Color.FromArgb(255, 230, 200, 80),
            LogSeverity.Error or LogSeverity.Exception => System.Drawing.Color.FromArgb(255, 230, 80, 80),
            LogSeverity.Success => System.Drawing.Color.FromArgb(255, 80, 200, 80),
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

    private void DrawGenericInspector(Paper paper, Prowl.Scribe.FontFile font, object obj)
    {
        Origami.Header(paper, "insp_h_generic", obj.GetType().Name).Show();
        Origami.Label(paper, "insp_generic_str", obj.ToString() ?? "null").Show();

                Origami.Header(paper, "insp_h_gprops", Loc.Get("inspector.properties")).Underline().Show();
        PropertyGridUtils.Draw(paper, "insp_gpg", obj);
    }

    private static string GetExtensionIcon(string ext) => FileIconRegistry.GetIconForExtension(ext);

    private static void DrawAssetLink(Paper paper, Prowl.Scribe.FontFile font, string id, Guid guid, EditorAssetDatabase db)
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
            .FontSize(EditorTheme.FontSize - 1)
            .Alignment(PaperUI.TextAlignment.MiddleLeft)
            .OnClick(guid, (g, _) => Selection.Ping(g));
    }
}
