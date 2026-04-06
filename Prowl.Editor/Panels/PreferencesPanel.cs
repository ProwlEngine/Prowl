using System;
using System.Drawing;
using System.IO;
using System.Reflection;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Preferences")]
public class PreferencesPanel : DockPanel
{
    public override string Title => "Preferences";
    public override string Icon => EditorIcons.Sliders;

    private enum Tab { General, Theme }
    private Tab _tab = Tab.General;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var settings = EditorSettings.Instance;

        using (paper.Row("pref_root").Size(width, height).Enter())
        {
            // Sidebar
            float sideW = 160f;
            using (paper.Column("pref_sidebar")
                .Width(sideW).Height(height)
                .BackgroundColor(EditorTheme.Darkest)
                .Enter())
            {
                paper.Box("pref_sidebar_hdr")
                    .Height(28).ChildLeft(8)
                    .Text("Preferences", font).TextColor(EditorTheme.Text)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                EditorGUI.Separator(paper, "pref_sidebar_sep");

                DrawTabButton(paper, font, "General", EditorIcons.Gear, Tab.General);
                DrawTabButton(paper, font, "Theme", EditorIcons.Palette, Tab.Theme);
            }

            paper.Box("pref_div").Width(1).Height(height).BackgroundColor(EditorTheme.Border);

            // Content
            float contentW = width - sideW - 1;
            using (ScrollView.Begin(paper, "pref_content", contentW, height))
            {
                paper.Box("pref_pad").Height(8);

                switch (_tab)
                {
                    case Tab.General: DrawGeneral(paper, settings, contentW); break;
                    case Tab.Theme: DrawTheme(paper, font, settings, contentW); break;
                }

                paper.Box("pref_pad2").Height(16);
            }
        }
    }

    private void DrawTabButton(Paper paper, Prowl.Scribe.FontFile font, string label, string icon, Tab tab)
    {
        bool sel = _tab == tab;
        paper.Box($"pref_tab_{tab}")
            .Height(26).ChildLeft(8).Rounded(3)
            .BackgroundColor(sel ? EditorTheme.Accent : Color.Transparent)
            .Hovered.BackgroundColor(sel ? EditorTheme.Accent : EditorTheme.ButtonHovered).End()
            .Text($"{icon}  {label}", font)
            .TextColor(sel ? EditorTheme.Text : EditorTheme.TextDim)
            .FontSize(EditorTheme.FontSize - 1)
            .Alignment(TextAlignment.MiddleLeft)
            .OnClick(tab, (t, _) => _tab = t);
    }

    private void DrawGeneral(Paper paper, EditorSettings settings, float w)
    {
        EditorGUI.Header(paper, "pref_gen_hdr", $"{EditorIcons.Gear}  General");
        EditorGUI.Separator(paper, "pref_gen_sep");

        EditorGUI.TextField(paper, "pref_projects_path", "Default Projects Path", settings.DefaultProjectsPath)
            .OnValueChanged(v => { settings.DefaultProjectsPath = v; settings.Save(); });

        EditorGUI.Toggle(paper, "pref_show_fps", "Show FPS", settings.ShowFPS)
            .OnValueChanged(v => { settings.ShowFPS = v; settings.Save(); });

        EditorGUI.Toggle(paper, "pref_auto_save", "Auto-Save Layout", settings.AutoSaveLayout)
            .OnValueChanged(v => { settings.AutoSaveLayout = v; settings.Save(); });
    }

    private void DrawTheme(Paper paper, Prowl.Scribe.FontFile font, EditorSettings settings, float w)
    {
        var theme = settings.Theme;

        EditorGUI.Header(paper, "pref_theme_hdr", $"{EditorIcons.Palette}  Theme");
        EditorGUI.Separator(paper, "pref_theme_sep");

        EditorGUI.TextField(paper, "pref_theme_name", "Theme Name", theme.Name)
            .OnValueChanged(v => theme.Name = v);

        paper.Box("pref_theme_sp1").Height(4);

        // Action buttons
        using (paper.Row("pref_theme_actions").Height(28).RowBetween(6).ChildLeft(4).Enter())
        {
            EditorGUI.Button(paper, "pref_theme_apply", $"{EditorIcons.Check}  Apply", width: 80)
                .OnValueChanged(_ => { settings.ApplyTheme(); settings.Save(); });

            EditorGUI.Button(paper, "pref_theme_reset", $"{EditorIcons.RotateLeft}  Reset", width: 80)
                .OnValueChanged(_ => settings.ResetTheme());

            EditorGUI.Button(paper, "pref_theme_export", $"{EditorIcons.Download}  Export", width: 90)
                .OnValueChanged(_ =>
                {
                    FileDialog.Open(FileDialogMode.Save, path =>
                    {
                        if (path == null) return;
                        if (!path.EndsWith(".prowltheme")) path += ".prowltheme";
                        theme.ExportToFile(path);
                        Widgets.Toasts.Info("Theme", $"Exported to {System.IO.Path.GetFileName(path)}");
                    }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { "Prowl Theme" });
                });

            EditorGUI.Button(paper, "pref_theme_import", $"{EditorIcons.Upload}  Import", width: 90)
                .OnValueChanged(_ =>
                {
                    FileDialog.Open(FileDialogMode.Open, path =>
                    {
                        if (path == null) return;
                        var imported = EditorThemeData.ImportFromFile(path);
                        if (imported != null)
                        {
                            settings.Theme = imported;
                            settings.ApplyTheme();
                            settings.Save();
                            Widgets.Toasts.Info("Theme", $"Imported: {imported.Name}");
                        }
                    }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { "Prowl Theme" });
                });
        }

        paper.Box("pref_theme_sp2").Height(12);

        // Color ramps
        DrawColorSection(paper, font, settings, "Neutral", new[]
        {
            ("Neutral 100 (Void)", nameof(EditorThemeData.Neutral100)),
            ("Neutral 200 (Abyss)", nameof(EditorThemeData.Neutral200)),
            ("Neutral 300 (Obsidian)", nameof(EditorThemeData.Neutral300)),
            ("Neutral 400 (Slate)", nameof(EditorThemeData.Neutral400)),
            ("Neutral 500 (Graphite)", nameof(EditorThemeData.Neutral500)),
        });

        DrawColorSection(paper, font, settings, "Purple", new[]
        {
            ("Purple 100", nameof(EditorThemeData.Purple100)),
            ("Purple 200 (Dusk)", nameof(EditorThemeData.Purple200)),
            ("Purple 300 (Twilight)", nameof(EditorThemeData.Purple300)),
            ("Purple 400 (Amethyst)", nameof(EditorThemeData.Purple400)),
            ("Purple 500 (Lavender)", nameof(EditorThemeData.Purple500)),
            ("Purple 600 (Wisteria)", nameof(EditorThemeData.Purple600)),
            ("Purple 700 (Lilac)", nameof(EditorThemeData.Purple700)),
        });

        DrawColorSection(paper, font, settings, "Blue", new[]
        {
            ("Blue 100", nameof(EditorThemeData.Blue100)),
            ("Blue 200 (Midnight)", nameof(EditorThemeData.Blue200)),
            ("Blue 300 (Harbor)", nameof(EditorThemeData.Blue300)),
            ("Blue 400 (Glacier)", nameof(EditorThemeData.Blue400)),
            ("Blue 500 (Mist)", nameof(EditorThemeData.Blue500)),
            ("Blue 600 (Powder)", nameof(EditorThemeData.Blue600)),
            ("Blue 700 (Frost)", nameof(EditorThemeData.Blue700)),
        });

        DrawColorSection(paper, font, settings, "Red", new[]
        {
            ("Red 100", nameof(EditorThemeData.Red100)),
            ("Red 200 (Ember)", nameof(EditorThemeData.Red200)),
            ("Red 300 (Garnet)", nameof(EditorThemeData.Red300)),
            ("Red 400 (Cinnabar)", nameof(EditorThemeData.Red400)),
            ("Red 500 (Blush)", nameof(EditorThemeData.Red500)),
            ("Red 600 (Rose)", nameof(EditorThemeData.Red600)),
            ("Red 700 (Petal)", nameof(EditorThemeData.Red700)),
        });

        DrawColorSection(paper, font, settings, "Ink", new[]
        {
            ("Ink 100 (Graphite)", nameof(EditorThemeData.Ink100)),
            ("Ink 200 (Iron)", nameof(EditorThemeData.Ink200)),
            ("Ink 300 (Pewter)", nameof(EditorThemeData.Ink300)),
            ("Ink 400 (Ash)", nameof(EditorThemeData.Ink400)),
            ("Ink 500 (Starlight)", nameof(EditorThemeData.Ink500)),
        });

        DrawColorSection(paper, font, settings, "Functional", new[]
        {
            ("Background", nameof(EditorThemeData.Background)),
            ("Darkest", nameof(EditorThemeData.Darkest)),
            ("Dark", nameof(EditorThemeData.Dark)),
            ("Normal", nameof(EditorThemeData.Normal)),
            ("Bright", nameof(EditorThemeData.Bright)),
            ("Text", nameof(EditorThemeData.Text)),
            ("Text Dim", nameof(EditorThemeData.TextDim)),
            ("Text Disabled", nameof(EditorThemeData.TextDisabled)),
            ("Button Normal", nameof(EditorThemeData.ButtonNormal)),
            ("Button Hovered", nameof(EditorThemeData.ButtonHovered)),
            ("Button Active", nameof(EditorThemeData.ButtonActive)),
            ("Accent", nameof(EditorThemeData.Accent)),
            ("Accent Dim", nameof(EditorThemeData.AccentDim)),
            ("Splitter", nameof(EditorThemeData.Splitter)),
            ("Splitter Hovered", nameof(EditorThemeData.SplitterHovered)),
            ("Tab Hovered", nameof(EditorThemeData.TabHovered)),
        });
    }

    private void DrawColorSection(Paper paper, Prowl.Scribe.FontFile font, EditorSettings settings,
        string sectionName, (string label, string propName)[] colors)
    {
        EditorGUI.Foldout(paper, $"pref_theme_{sectionName}", sectionName, () =>
        {
            var theme = settings.Theme;
            var type = typeof(EditorThemeData);

            foreach (var (label, propName) in colors)
            {
                var prop = type.GetProperty(propName);
                if (prop == null) continue;

                string currentHex = (string)(prop.GetValue(theme) ?? "#FF00FF");

                EditorGUI.TextField(paper, $"pref_c_{propName}", label, currentHex)
                    .OnValueChanged(v =>
                    {
                        // Validate hex
                        if (v.Length > 0 && v[0] != '#') v = "#" + v;
                        try
                        {
                            ColorTranslator.FromHtml(v);
                            prop.SetValue(theme, v);
                        }
                        catch { }
                    });
            }
        });
    }
}
