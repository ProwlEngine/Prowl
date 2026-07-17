using System.Collections.Generic;

using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// Manages NuGet package references for user scripts.
/// Persisted as ProjectSettings/Packages.yaml and consumed by ScriptCompiler via the settings registry.
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

        Origami.Header(paper, "pkg_hdr", $"{EditorIcons.Cubes}  NuGet Packages").Underline().Show();

        Origami.Label(paper, "pkg_info",
            "Packages flow to both Game and Editor scripts. Mark a package Editor Only to keep it out of player builds.").Show();

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
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);

                Origami.Checkbox(paper, $"pkg_eo_{i}", pkg.EditorOnly, v =>
                    {
                        Packages[idx].EditorOnly = v;
                        Apply();
                        EditorRegistries.SaveSettings();
                        ScriptAssemblyManager.RequestRecompile();
                    })
                    .LabelRight("Editor Only").Show();

                paper.Box($"pkg_ver_{i}")
                    .Width(80).Height(EditorTheme.RowHeight)
                    .Text(pkg.Version, font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall)
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
                        EditorRegistries.SaveSettings();
                        ScriptAssemblyManager.RequestRecompile();
                    });
            }
        }

        if (Packages.Count == 0)
        {
            paper.Box("pkg_empty").Height(30)
                .Text("No packages installed", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
        }

        paper.Box("pkg_sp2").Height(8);
        Origami.Separator(paper, "pkg_add_sep").Show();

        // Add new package
        Origami.Header(paper, "pkg_add_hdr", "Add Package").Show();

        EditorGUI.Row(paper, "pkg_add_name", "Package Name", () =>
            Origami.TextField(paper, "pkg_add_name_v", _newName, v => _newName = v).Show());

        EditorGUI.Row(paper, "pkg_add_ver", "Version", () =>
            Origami.TextField(paper, "pkg_add_ver_v", _newVersion, v => _newVersion = v).Show());

        Origami.Checkbox(paper, "pkg_add_editor_only", _newEditorOnly, v => _newEditorOnly = v)
            .LabelRight("Editor Only").Show();

        Origami.Button(paper, "pkg_add_btn", $"{EditorIcons.Plus}  Add Package", () =>
            {
                string name = _newName.Trim();
                string version = _newVersion.Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                    return;

                if (Packages.Exists(p => p.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)))
                {
                    Runtime.Debug.LogWarning($"Package '{name}' is already in the list.");
                    return;
                }

                Packages.Add(new PackageEntry { Name = name, Version = version, EditorOnly = _newEditorOnly });
                _newName = "";
                _newVersion = "";
                _newEditorOnly = false;
                Apply();
                EditorRegistries.SaveSettings();
                ScriptAssemblyManager.RequestRecompile();
            }).Width(140).Show();
    }

}
