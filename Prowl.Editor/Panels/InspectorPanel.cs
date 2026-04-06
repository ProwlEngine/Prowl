using System;
using System.IO;
using System.Linq;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Inspector")]
public class InspectorPanel : DockPanel
{
    public override string Title => "Inspector";
    public override string Icon => EditorIcons.Sliders;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (ScrollView.Begin(paper, "insp_scroll", width, height, paddingLeft: 8, paddingRight: 0, paddingTop: 8))
        {
            if (Selection.Count == 0)
            {
                DrawEmpty(paper, font, width);
                return;
            }

            var active = Selection.ActiveObject;
            if (active == null)
            {
                DrawEmpty(paper, font, width);
                return;
            }

            // Draw based on type — GameObject has its own header
            if (active is GameObject gameObject)
            {
                GameObjectInspector.Draw(paper, font, gameObject);
            }
            else
            {
                DrawSelectionHeader(paper, font, active);
                EditorGUI.Separator(paper, "insp_sep_header");

                if (active is ContentItem contentItem)
                    DrawAssetInspector(paper, font, contentItem);
                else if (active is EngineObject engineObj)
                    DrawEngineObjectInspector(paper, font, engineObj);
                else
                    DrawGenericInspector(paper, font, active);
            }

            // Multi-selection summary
            if (Selection.Count > 1)
            {
                EditorGUI.Separator(paper, "insp_sep_multi");
                EditorGUI.Header(paper, "insp_h_multi", "Selection");
                EditorGUI.Label(paper, "insp_multi_count", $"{Selection.Count} objects selected");

                for (int i = 0; i < Selection.Count && i < 20; i++)
                {
                    var obj = Selection.Selected[i];
                    string name = obj switch
                    {
                        ContentItem ci => $"{GetTypeIcon(ci)} {ci.Name}",
                        EngineObject eo => $"{EditorIcons.Cube} {eo.Name}",
                        _ => obj.ToString() ?? "Unknown"
                    };
                    EditorGUI.Label(paper, $"insp_sel_{i}", name);
                }

                if (Selection.Count > 20)
                    EditorGUI.Label(paper, "insp_more", $"... and {Selection.Count - 20} more");
            }

            paper.Box("insp_bottom_pad").Height(20);
        }
    }

    private void DrawEmpty(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        paper.Box("insp_empty").Height(80)
            .Text("Nothing Selected", font)
            .TextColor(EditorTheme.Ink500Disabled)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleCenter);

        paper.Box("insp_hint").Height(30)
            .Text("Select an asset or object to inspect it.", font)
            .TextColor(EditorTheme.Ink500Disabled)
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
            icon = GetTypeIcon(ci);
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
            .Height(40).ChildLeft(4).RowBetween(8).ChildTop(4).ChildBottom(4)
            .Enter())
        {
            // Large icon
            paper.Box("insp_h_icon")
                .Width(32).Height(32)
                .BackgroundColor(Color.FromArgb(30, 255, 255, 255))
                .Rounded(6)
                .Text(icon, font)
                .TextColor(EditorTheme.Accent)
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
                    .TextColor(EditorTheme.Ink500Dim)
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
        EditorGUI.Header(paper, "insp_h_asset", "Asset Info");

        EditorGUI.Label(paper, "insp_path", $"Path: {item.RelativePath}");

        if (entry != null)
        {
            EditorGUI.Label(paper, "insp_guid", $"GUID: {entry.Guid}");
            EditorGUI.Label(paper, "insp_importer", $"Importer: {entry.ImporterType}");

            if (entry.MainAssetType != null)
                EditorGUI.Label(paper, "insp_maintype", $"Type: {entry.MainAssetType.Name}");

            // Last modified
            var lastMod = new DateTime(entry.LastModifiedTicks, DateTimeKind.Utc).ToLocalTime();
            EditorGUI.Label(paper, "insp_lastmod", $"Modified: {lastMod:yyyy-MM-dd HH:mm:ss}");

            // Dependencies
            if (entry.Dependencies.Length > 0)
            {
                EditorGUI.Separator(paper, "insp_sep_deps");
                EditorGUI.Header(paper, "insp_h_deps", "Dependencies");
                for (int i = 0; i < entry.Dependencies.Length && i < 20; i++)
                {
                    string depPath = db.GuidToPath(entry.Dependencies[i]) ?? entry.Dependencies[i].ToString();
                    EditorGUI.Label(paper, $"insp_dep_{i}", $"  {depPath}");
                }
            }

            // Dependents (who references this asset)
            var dependents = db.Dependencies.GetDependents(entry.Guid);
            if (dependents.Count > 0)
            {
                EditorGUI.Separator(paper, "insp_sep_refs");
                EditorGUI.Header(paper, "insp_h_refs", "Used By");
                int count = 0;
                foreach (var depGuid in dependents)
                {
                    if (count >= 20) break;
                    string depPath = db.GuidToPath(depGuid) ?? depGuid.ToString();
                    EditorGUI.Label(paper, $"insp_ref_{count}", $"  {depPath}");
                    count++;
                }
            }
        }

        // Reimport button
        EditorGUI.Separator(paper, "insp_sep_actions");
        if (entry != null)
        {
            EditorGUI.Button(paper, "insp_reimport", $"{EditorIcons.ArrowsRotate}  Reimport")
                .OnValueChanged(_ => db.Reimport(entry.Guid));
        }
    }

    private void DrawFolderInfo(Paper paper, Prowl.Scribe.FontFile font, ContentItem item)
    {
        EditorGUI.Header(paper, "insp_h_folder", "Folder");
        EditorGUI.Label(paper, "insp_folder_path", $"Path: {item.RelativePath}");

        string absPath = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
        if (Directory.Exists(absPath))
        {
            try
            {
                int fileCount = Directory.GetFiles(absPath, "*", SearchOption.AllDirectories)
                    .Count(f => !f.EndsWith(".meta"));
                int folderCount = Directory.GetDirectories(absPath, "*", SearchOption.AllDirectories).Length;
                EditorGUI.Label(paper, "insp_folder_files", $"Files: {fileCount}");
                EditorGUI.Label(paper, "insp_folder_folders", $"Subfolders: {folderCount}");
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
                .Text("Sub-Asset", font)
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

        EditorGUI.Separator(paper, "insp_sub_sep1");

        // Info
        EditorGUI.Label(paper, "insp_sub_type", $"Type: {item.TypeLabel}");
        EditorGUI.Label(paper, "insp_sub_guid", $"GUID: {item.Guid}");
        if (parentEntry != null)
            EditorGUI.Label(paper, "insp_sub_parent", $"Parent: {parentEntry.Path}");

        EditorGUI.Separator(paper, "insp_sub_sep2");

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

                EditorGUI.Label(paper, "insp_sub_tex_size", $"Size: {tex.Width} x {tex.Height}");
                EditorGUI.Label(paper, "insp_sub_tex_fmt", $"Format: {tex.ImageFormat}");
            }
            else if (asset is Prowl.Runtime.Resources.Mesh mesh)
            {
                EditorGUI.Label(paper, "insp_sub_mesh_verts", $"Vertices: {mesh.Vertices?.Length ?? 0:N0}");
                EditorGUI.Label(paper, "insp_sub_mesh_tris", $"Triangles: {(mesh.Indices?.Length ?? 0) / 3:N0}");
                EditorGUI.Label(paper, "insp_sub_mesh_bounds", $"Bounds: {mesh.bounds.Size}");
            }
            else
            {
                // Generic read-only property grid
                EditorGUI.Header(paper, "insp_sub_h_props", "Properties (Read-Only)");
                // Show properties as labels
                var fields = Widgets.PropertyGrid.GetSerializableFields(asset.GetType());
                foreach (var field in fields)
                {
                    object? val = field.GetValue(asset);
                    string label = Widgets.PropertyGrid.NicifyName(field.Name);
                    EditorGUI.Label(paper, $"insp_sub_prop_{field.Name}", $"{label}: {val ?? "(null)"}");
                }
            }
        }

        EditorGUI.Separator(paper, "insp_sub_sep3");

        // Extract button — clone sub-asset to a standalone file
        EditorGUI.Button(paper, "insp_sub_extract", $"{EditorIcons.FileExport}  Extract as Asset")
            .OnValueChanged(_ => ExtractSubAsset(item, parentEntry, subEntry, asset));
    }

    private void ExtractSubAsset(ContentItem item, AssetEntry? parentEntry, SubAssetEntry? subEntry, EngineObject? asset)
    {
        if (asset == null || parentEntry == null || Project.Current == null) return;

        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        // Determine target path — same folder as parent, with sub-asset name
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
            var ctx = new Echo.SerializationContext();
            Runtime.AssetDatabase.ConfigureContext(ctx);

            // Clear the sub-asset's AssetID so it serializes as a full object, not a reference
            var originalId = asset.AssetID;
            asset.AssetID = Guid.Empty;

            var echo = Echo.Serializer.Serialize(asset, ctx);
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
        EditorGUI.Header(paper, "insp_h_eo", obj.GetType().Name);

        EditorGUI.Label(paper, "insp_eo_name", $"Name: {obj.Name}");
        EditorGUI.Label(paper, "insp_eo_id", $"Instance ID: {obj.InstanceID}");

        if (obj.AssetID != Guid.Empty)
            EditorGUI.Label(paper, "insp_eo_assetid", $"Asset ID: {obj.AssetID}");
        if (!string.IsNullOrEmpty(obj.AssetPath))
            EditorGUI.Label(paper, "insp_eo_assetpath", $"Asset Path: {obj.AssetPath}");

        // Use PropertyGrid for reflection-based editing
        EditorGUI.Separator(paper, "insp_sep_props");
        EditorGUI.Header(paper, "insp_h_props", "Properties");
        PropertyGrid.Draw(paper, "insp_pg", obj);
    }

    private void DrawGenericInspector(Paper paper, Prowl.Scribe.FontFile font, object obj)
    {
        EditorGUI.Header(paper, "insp_h_generic", obj.GetType().Name);
        EditorGUI.Label(paper, "insp_generic_str", obj.ToString() ?? "null");

        EditorGUI.Separator(paper, "insp_sep_gprops");
        EditorGUI.Header(paper, "insp_h_gprops", "Properties");
        PropertyGrid.Draw(paper, "insp_gpg", obj);
    }

    private static string GetTypeIcon(ContentItem ci)
    {
        if (ci.IsFolder) return EditorIcons.Folder;
        string ext = Path.GetExtension(ci.Name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => EditorIcons.FileCode,
            ".shader" => EditorIcons.WandMagicSparkles,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" => EditorIcons.FileImage,
            ".scene" => EditorIcons.Cubes,
            ".mat" => EditorIcons.Palette,
            ".fbx" or ".obj" or ".gltf" or ".glb" => EditorIcons.VectorSquare,
            _ => EditorIcons.File,
        };
    }
}
