using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Prowl.Editor.Scripting;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor;

/// <summary>
/// Manages NuGet package references for user scripts.
/// Reads/writes ProjectSettings/Packages.json which is consumed by ScriptCompiler.
/// </summary>
[ProjectSettings("Packages", EditorIcons.Cubes, order: 30)]
public class PackageSettings : ProjectSettingsBase
{
    public List<PackageEntry> Packages { get; set; } = new();

    public class PackageEntry
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
    }

    private string _newName = "";
    private string _newVersion = "";

    public override void Apply()
    {
        // Write Packages.json for ScriptCompiler to consume
        SavePackagesJson();
    }

    public override void ResetToDefaults()
    {
        Packages.Clear();
    }

    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        EditorGUI.Header(paper, "pkg_hdr", $"{EditorIcons.Cubes}  NuGet Packages");
        EditorGUI.Separator(paper, "pkg_sep");

        EditorGUI.Label(paper, "pkg_info",
            "Packages are added to both Game and Editor script assemblies.");

        paper.Box("pkg_sp1").Height(4);

        // Package list
        for (int i = 0; i < Packages.Count; i++)
        {
            int idx = i;
            var pkg = Packages[i];

            using (paper.Row($"pkg_row_{i}").Height(EditorTheme.RowHeight).RowBetween(4).ChildLeft(4).Enter())
            {
                paper.Box($"pkg_name_{i}")
                    .Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(pkg.Name, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box($"pkg_ver_{i}")
                    .Width(80).Height(EditorTheme.RowHeight)
                    .Text(pkg.Version, font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleRight);

                paper.Box($"pkg_del_{i}")
                    .Width(20).Height(EditorTheme.RowHeight).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(idx, (ci, _) =>
                    {
                        Packages.RemoveAt(ci);
                        Apply();
                        ProjectSettingsRegistry.SaveAll();
                    });
            }
        }

        if (Packages.Count == 0)
        {
            paper.Box("pkg_empty").Height(30)
                .Text("No packages installed", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);
        }

        paper.Box("pkg_sp2").Height(8);
        EditorGUI.Separator(paper, "pkg_add_sep");

        // Add new package
        EditorGUI.Header(paper, "pkg_add_hdr", "Add Package");

        EditorGUI.TextField(paper, "pkg_add_name", "Package Name", _newName)
            .OnValueChanged(v => _newName = v);

        EditorGUI.TextField(paper, "pkg_add_ver", "Version", _newVersion)
            .OnValueChanged(v => _newVersion = v);

        EditorGUI.Button(paper, "pkg_add_btn", $"{EditorIcons.Plus}  Add Package", width: 140)
            .OnValueChanged(_ =>
            {
                if (!string.IsNullOrWhiteSpace(_newName) && !string.IsNullOrWhiteSpace(_newVersion))
                {
                    Packages.Add(new PackageEntry { Name = _newName.Trim(), Version = _newVersion.Trim() });
                    _newName = "";
                    _newVersion = "";
                    Apply();
                    ProjectSettingsRegistry.SaveAll();
                }
            });

        paper.Box("pkg_sp3").Height(8);

        // Recompile button
        EditorGUI.Button(paper, "pkg_recompile", $"{EditorIcons.ArrowsRotate}  Recompile Scripts", width: 180)
            .OnValueChanged(_ => ScriptAssemblyManager.RequestRecompile());
    }

    private void SavePackagesJson()
    {
        var project = Project.Current;
        if (project == null) return;

        string path = Path.Combine(project.ProjectSettingsPath, "Packages.json");

        // Convert to the format ScriptCompiler expects
        var list = new List<object>();
        foreach (var pkg in Packages)
            list.Add(new { Name = pkg.Name, Version = pkg.Version });

        string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
