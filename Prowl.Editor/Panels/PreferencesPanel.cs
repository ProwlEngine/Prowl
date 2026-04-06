using System;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;

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
            float sideW = 160f;
            using (paper.Column("pref_sidebar")
                .Width(sideW).Height(height)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("pref_sidebar_hdr")
                    .Height(28).ChildLeft(8)
                    .Text("Preferences", font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                EditorGUI.Separator(paper, "pref_sidebar_sep");

                DrawTabBtn(paper, font, "General", EditorIcons.Gear, Tab.General);
                DrawTabBtn(paper, font, "Theme", EditorIcons.Palette, Tab.Theme);
            }

            paper.Box("pref_div").Width(1).Height(height).BackgroundColor(EditorTheme.Ink200);

            float contentW = width - sideW - 1;
            using (ScrollView.Begin(paper, "pref_content", contentW, height))
            {
                paper.Box("pref_pad").Height(8);
                switch (_tab)
                {
                    case Tab.General: DrawGeneral(paper, settings); break;
                    case Tab.Theme: DrawTheme(paper, font, settings, contentW); break;
                }
                paper.Box("pref_pad2").Height(16);
            }
        }
    }

    private void DrawTabBtn(Paper paper, Prowl.Scribe.FontFile font, string label, string icon, Tab tab)
    {
        bool sel = _tab == tab;
        paper.Box($"pref_tab_{tab}")
            .Height(26).ChildLeft(8).Rounded(3)
            .BackgroundColor(sel ? EditorTheme.Accent : Color.Transparent)
            .Hovered.BackgroundColor(sel ? EditorTheme.Accent : EditorTheme.ButtonHovered).End()
            .Text($"{icon}  {label}", font)
            .TextColor(sel ? EditorTheme.Ink500 : EditorTheme.Ink500Dim)
            .FontSize(EditorTheme.FontSize - 1)
            .Alignment(TextAlignment.MiddleLeft)
            .OnClick(tab, (t, _) => _tab = t);
    }

    // ================================================================
    //  General
    // ================================================================

    private void DrawGeneral(Paper paper, EditorSettings s)
    {
        EditorGUI.Header(paper, "pref_gen_hdr", $"{EditorIcons.Gear}  General");
        EditorGUI.Separator(paper, "pref_gen_sep");

        EditorGUI.TextField(paper, "pref_proj_path", "Default Projects Path", s.DefaultProjectsPath)
            .OnValueChanged(v => { s.DefaultProjectsPath = v; s.Save(); });

        EditorGUI.Toggle(paper, "pref_auto_save", "Auto-Save Layout", s.AutoSaveLayout)
            .OnValueChanged(v => { s.AutoSaveLayout = v; s.Save(); });

        EditorGUI.Toggle(paper, "pref_reimport_focus", "Reimport Only on Focus", s.ReimportOnFocusOnly)
            .OnValueChanged(v => { s.ReimportOnFocusOnly = v; s.Save(); });
    }

    // ================================================================
    //  Theme (Colors + Sizing)
    // ================================================================

    private void DrawTheme(Paper paper, Prowl.Scribe.FontFile font, EditorSettings s, float w)
    {
        var theme = s.Theme;

        EditorGUI.Header(paper, "pref_theme_hdr", $"{EditorIcons.Palette}  Theme");
        EditorGUI.Separator(paper, "pref_theme_sep");

        EditorGUI.TextField(paper, "pref_theme_name", "Theme Name", theme.Name)
            .OnValueChanged(v => theme.Name = v);

        paper.Box("pref_theme_sp1").Height(4);

        // Actions
        using (paper.Row("pref_theme_actions").Height(28).RowBetween(6).ChildLeft(4).Enter())
        {
            EditorGUI.Button(paper, "pref_apply", $"{EditorIcons.Check}  Apply", width: 80)
                .OnValueChanged(_ => { s.ApplyTheme(); s.Save(); });

            EditorGUI.Button(paper, "pref_reset", $"{EditorIcons.RotateLeft}  Reset", width: 80)
                .OnValueChanged(_ => s.ResetTheme());

            EditorGUI.Button(paper, "pref_export", $"{EditorIcons.Download}  Export", width: 90)
                .OnValueChanged(_ =>
                {
                    FileDialog.Open(FileDialogMode.Save, path =>
                    {
                        if (path == null) return;
                        if (!path.EndsWith(".prowltheme")) path += ".prowltheme";
                        theme.ExportToFile(path);
                        Toasts.Info("Theme", $"Exported to {System.IO.Path.GetFileName(path)}");
                    }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { "Prowl Theme" });
                });

            EditorGUI.Button(paper, "pref_import", $"{EditorIcons.Upload}  Import", width: 90)
                .OnValueChanged(_ =>
                {
                    FileDialog.Open(FileDialogMode.Open, path =>
                    {
                        if (path == null) return;
                        var imported = EditorThemeData.ImportFromFile(path);
                        if (imported != null)
                        {
                            s.Theme = imported;
                            s.ApplyTheme();
                            s.Save();
                            Toasts.Info("Theme", $"Imported: {imported.Name}");
                        }
                    }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { "Prowl Theme" });
                });
        }

        paper.Box("pref_theme_sp2").Height(12);

        // ── Color Ramps ──
        string[] neutralNames = ["100 Void", "200 Abyss", "300 Obsidian", "400 Slate ★", "500 Graphite"];
        string[] purpleNames = ["100", "200 Dusk", "300 Twilight", "400 Amethyst ★", "500 Lavender", "600 Wisteria", "700 Lilac"];
        string[] blueNames = ["100", "200 Midnight", "300 Harbor", "400 Glacier ★", "500 Mist", "600 Powder", "700 Frost"];
        string[] redNames = ["100", "200 Ember", "300 Garnet", "400 Cinnabar ★", "500 Blush", "600 Rose", "700 Petal"];
        string[] inkNames = ["100 Graphite", "200 Iron", "300 Pewter ★", "400 Ash", "500 Starlight"];

        DrawRamp(paper, s, "Neutral", theme.Neutral, neutralNames);
        DrawRamp(paper, s, "Purple", theme.Purple, purpleNames);
        DrawRamp(paper, s, "Blue", theme.Blue, blueNames);
        DrawRamp(paper, s, "Red", theme.Red, redNames);
        DrawRamp(paper, s, "Ink", theme.Ink, inkNames);

        paper.Box("pref_theme_sp3").Height(12);

        // ── Sizing ──
        EditorGUI.Foldout(paper, "pref_sz_general", "General Sizing", () =>
        {
            SzSlider(paper, s, "Font Size", theme.FontSize, 8, 32, v => theme.FontSize = v);
            SzSlider(paper, s, "Row Height", theme.RowHeight, 16, 40, v => theme.RowHeight = v);
            SzSlider(paper, s, "Menu Bar Height", theme.MenuBarHeight, 18, 48, v => theme.MenuBarHeight = v);
            SzSlider(paper, s, "Label Width", theme.LabelWidth, 60, 240, v => theme.LabelWidth = v);
            SzSlider(paper, s, "Spacing", theme.Spacing, 0, 12, v => theme.Spacing = v);
            SzSlider(paper, s, "Padding", theme.Padding, 0, 16, v => theme.Padding = v);
            SzSlider(paper, s, "Roundness", theme.Roundness, 0, 20, v => theme.Roundness = v);
        });

        EditorGUI.Foldout(paper, "pref_sz_docking", "Docking", () =>
        {
            SzSlider(paper, s, "Splitter Size", theme.SplitterSize, 4, 24, v => theme.SplitterSize = v);
            SzSlider(paper, s, "Dock Padding", theme.DockPadding, 0, 24, v => theme.DockPadding = v);
        });

        EditorGUI.Foldout(paper, "pref_sz_tabs", "Tabs", () =>
        {
            SzSlider(paper, s, "Tab Bar Height", theme.TabBarHeight, 18, 40, v => theme.TabBarHeight = v);
            SzSlider(paper, s, "Tab Padding", theme.TabPadding, 4, 24, v => theme.TabPadding = v);
        });
    }

    private void DrawRamp(Paper paper, EditorSettings s, string name, ColorRamp ramp, string[] stopNames)
    {
        EditorGUI.Foldout(paper, $"pref_ramp_{name}", name, () =>
        {
            var primaryColor = HexToVColor(ramp.Primary);
            EditorGUI.ColorField(paper, $"pref_ramp_{name}_primary", $"{name} Primary", primaryColor)
                .OnValueChanged(v =>
                {
                    ramp.Primary = VColorToHex(v);
                    s.ApplyTheme();
                });

            EditorGUI.Toggle(paper, $"pref_ramp_{name}_override", "Override All Stops", ramp.OverrideAll)
                .OnValueChanged(v =>
                {
                    ramp.OverrideAll = v;
                    if (v && ramp.Overrides != null)
                    {
                        for (int i = 0; i < ramp.StopCount; i++)
                            ramp.Overrides[i] = ColorRamp.ColorToHex(ramp.GetStop(i));
                    }
                    s.ApplyTheme();
                });

            if (ramp.OverrideAll && ramp.Overrides != null)
            {
                for (int i = 0; i < ramp.StopCount; i++)
                {
                    int idx = i;
                    string label = i < stopNames.Length ? stopNames[i] : $"Stop {i}";
                    var stopColor = HexToVColor(ramp.Overrides[i]);
                    EditorGUI.ColorField(paper, $"pref_ramp_{name}_{i}", label, stopColor)
                        .OnValueChanged(v =>
                        {
                            ramp.Overrides![idx] = VColorToHex(v);
                            s.ApplyTheme();
                        });
                }
            }
        });
    }

    private void SzSlider(Paper paper, EditorSettings s, string label, float value, float min, float max, Action<float> set)
    {
        EditorGUI.Slider(paper, $"pref_sz_{label.Replace(" ", "_")}", label, value, min, max)
            .OnValueChanged(v =>
            {
                set(MathF.Round(v, 1));
                s.ApplyTheme();
                s.Save();
            });
    }

    // ================================================================
    //  Color conversion helpers
    // ================================================================

    private static VColor HexToVColor(string hex)
    {
        var c = ColorRamp.ParseHex(hex);
        return new VColor(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }

    private static string VColorToHex(VColor c)
    {
        int r = Math.Clamp((int)(c.R * 255), 0, 255);
        int g = Math.Clamp((int)(c.G * 255), 0, 255);
        int b = Math.Clamp((int)(c.B * 255), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
