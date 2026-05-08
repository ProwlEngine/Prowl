using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Prowl.Editor.Inspector;
using Prowl.Editor.Scripting;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor;

/// <summary>
/// Manages NuGet package references for user scripts.
/// Reads/writes ProjectSettings/Packages.json which is consumed by ScriptCompiler.
/// </summary>
[ProjectSettings("Packages", EditorIcons.Cubes, order: 30, exportToBuild: false)]
public class PackageSettings : ProjectSettingsBase
{
    public List<PackageEntry> Packages = new();

    public class PackageEntry
    {
        public string Name = "";
        public string Version = "";
        public bool EditorOnly = false;
    }

    private string _newName = "";
    private string _newVersion = "";
    private bool _newEditorOnly = false;

    public override void Apply() { }

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
            "Packages flow to both Game and Editor scripts. Mark a package Editor Only to keep it out of player builds.");

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

                Origami.Checkbox(paper, $"pkg_eo_{i}", pkg.EditorOnly, v =>
                    {
                        Packages[idx].EditorOnly = v;
                        Apply();
                        ProjectSettingsRegistry.SaveAll();
                    })
                    .LabelRight("Editor Only").Show();

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

        InspectorRow.Draw(paper, "pkg_add_name", "Package Name", () =>
            Origami.TextField(paper, "pkg_add_name_v", _newName, v => _newName = v).Show());

        InspectorRow.Draw(paper, "pkg_add_ver", "Version", () =>
            Origami.TextField(paper, "pkg_add_ver_v", _newVersion, v => _newVersion = v).Show());

        Origami.Checkbox(paper, "pkg_add_editor_only", _newEditorOnly, v => _newEditorOnly = v)
            .LabelRight("Editor Only").Show();

        Origami.Button(paper, "pkg_add_btn", $"{EditorIcons.Plus}  Add Package", () =>
            {
                if (!string.IsNullOrWhiteSpace(_newName) && !string.IsNullOrWhiteSpace(_newVersion))
                {
                    Packages.Add(new PackageEntry { Name = _newName.Trim(), Version = _newVersion.Trim(), EditorOnly = _newEditorOnly });
                    _newName = "";
                    _newVersion = "";
                    _newEditorOnly = false;
                    Apply();
                    ProjectSettingsRegistry.SaveAll();
                }
            }).Width(140).Show();

        paper.Box("pkg_sp3").Height(8);

        // Recompile button
        Origami.Button(paper, "pkg_recompile", $"{EditorIcons.ArrowsRotate}  Recompile Scripts", () => { ScriptAssemblyManager.RequestRecompile(); }).Width(180).Show();
    }

}
