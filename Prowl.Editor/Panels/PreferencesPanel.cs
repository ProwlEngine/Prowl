using System;

using Prowl.Editor.Docking;
using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Preferences")]
public class PreferencesPanel : DockPanel
{
    public override string Title => Loc.Get("panel.preferences");
    public override string Icon => EditorIcons.Sliders;

    private enum Tab { General, Theme, Shortcuts }
    private Tab _tab = Tab.General;
    private string _shortcutSearch = "";
    private string? _rebindingId;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var settings = EditorSettings.Instance;

        using (paper.Row("pref_root").Size(width, height).Enter())
        {
            float sideW = 160f;
            using (paper.Column("pref_sidebar")
                .Padding(new UnitValue(EditorTheme.SidePixelPadding))
                .Width(sideW).Height(height)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("pref_sidebar_hdr")
                    .Height(EditorTheme.RowHeight).ChildLeft(EditorTheme.Padding)
                    .Text("Preferences", font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                Origami.Separator(paper, "pref_sidebar_sep").Show();

                DrawTabBtn(paper, font, "General", EditorIcons.Gear, Tab.General);
                DrawTabBtn(paper, font, "Theme", EditorIcons.Palette, Tab.Theme);
                DrawTabBtn(paper, font, "Shortcuts", EditorIcons.Keyboard, Tab.Shortcuts);
            }

            paper.Box("pref_div").Width(1).Height(height).BackgroundColor(EditorTheme.Ink200);

            float contentW = width - sideW - 1;
            Origami.ScrollView(paper, "pref_content", contentW, height)
                .Padding(EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding)
                .Body(() =>
            {
                paper.Box("pref_pad").Height(EditorTheme.Padding * 2);
                switch (_tab)
                {
                    case Tab.General: DrawGeneral(paper, settings); break;
                    case Tab.Theme: DrawTheme(paper, font, settings, contentW); break;
                    case Tab.Shortcuts: DrawShortcuts(paper, font, contentW); break;
                }
                paper.Box("pref_pad2").Height(EditorTheme.Padding * 4);
            });
        }
    }

    private void DrawTabBtn(Paper paper, Prowl.Scribe.FontFile font, string label, string icon, Tab tab)
    {
        bool sel = _tab == tab;
        paper.Box($"pref_tab_{tab}")
            .Height(EditorTheme.RowHeight)
            .Margin(0, 0, 0, EditorTheme.VerticalNavbarSpacing)
            .ChildLeft(EditorTheme.Padding).Rounded(EditorTheme.Roundness * 0.5f)
            .BackgroundColor(sel ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(sel ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
            .Text($"{icon}  {label}", font)
            .TextColor(sel ? EditorTheme.Ink500 : EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize - 1)
            .Alignment(TextAlignment.MiddleLeft)
            .OnClick(tab, (t, _) => _tab = t);
    }

    // ================================================================
    //  General
    // ================================================================

    private void DrawGeneral(Paper paper, EditorSettings s)
    {
        Origami.Header(paper, "pref_gen_hdr", $"{EditorIcons.Gear}  General").Underline().Show();

        InspectorRow.Draw(paper, "pref_lang", "Language", () =>
            Origami.Dropdown(paper, "pref_lang_dd",
                LocaleHelper.GetIndex(Loc.CurrentLocale),
                LocaleHelper.SetLocale, LocaleHelper.Names).Show());

        InspectorRow.Draw(paper, "pref_proj_path", "Default Projects Path", () =>
            Origami.TextField(paper, "pref_proj_path_v", s.DefaultProjectsPath,
                v => { s.DefaultProjectsPath = v; s.Save(); }).Show());

        Origami.Checkbox(paper, "pref_auto_save", SaveManager.AutoSaveEnabled,
                v => { SaveManager.AutoSaveEnabled = v; s.AutoSaveLayout = v; s.Save(); })
            .LabelRight("Auto-Save").Show();

        Origami.Checkbox(paper, "pref_reimport_focus", s.ReimportOnFocusOnly,
                v => { s.ReimportOnFocusOnly = v; s.Save(); })
            .LabelRight("Reimport Only on Focus").Show();

        string[] thumbOptions = ["32", "64", "128"];
        int thumbIndex = s.ThumbnailSize switch { 64 => 1, 128 => 2, _ => 0 };
        InspectorRow.Draw(paper, "pref_thumb_size", "Thumbnail Size", () =>
            Origami.Dropdown(paper, "pref_thumb_size_v", thumbIndex,
                v =>
                {
                    s.ThumbnailSize = v switch { 1 => 64, 2 => 128, _ => 32 };
                    s.Save();
                    ThumbnailGenerator.DeleteAll();
                    ProjectPanel.ClearThumbnailCache();
                }, thumbOptions).Show());
    }

    // ================================================================
    //  Theme (Colors + Sizing)
    // ================================================================

    private void DrawTheme(Paper paper, Prowl.Scribe.FontFile font, EditorSettings s, float w)
    {
        var theme = s.Theme;

        Origami.Header(paper, "pref_theme_hdr", $"{EditorIcons.Palette}  Theme").Underline().Show();

        InspectorRow.Draw(paper, "pref_theme_name", "Theme Name", () =>
            Origami.TextField(paper, "pref_theme_name_v", theme.Name,
                v => theme.Name = v).Show());

        paper.Box("pref_theme_sp1").Height(EditorTheme.Spacing * 2);

        // Actions
        using (paper.Row("pref_theme_actions")
            .Height(EditorTheme.RowHeight)
            .RowBetween(EditorTheme.Spacing * 3)
            .ChildLeft(EditorTheme.Padding)
            .Enter())
        {
            Origami.Button(paper, "pref_apply", $"{EditorIcons.Check}  Apply", () => { s.ApplyTheme(); s.Save(); }).Width(80).Show();

            Origami.Button(paper, "pref_reset", $"{EditorIcons.RotateLeft}  Reset", () => s.ResetTheme()).Width(80).Show();

            Origami.Button(paper, "pref_export", $"{EditorIcons.Download}  Export", () =>
            {
                EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
                {
                    if (path == null) return;
                    if (!path.EndsWith(".prowltheme")) path += ".prowltheme";
                    theme.ExportToFile(path);
                    Toasts.Info("Theme", $"Exported to {System.IO.Path.GetFileName(path)}");
                }, filters: new[] { "*.prowltheme" }, filterLabels: new[] { "Prowl Theme" });
            }).Width(90).Show();

            Origami.Button(paper, "pref_import", $"{EditorIcons.Upload}  Import", () =>
            {
                EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
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
            }).Width(90).Show();
        }

        paper.Box("pref_theme_sp2").Height(EditorTheme.Padding * 3);

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

        paper.Box("pref_theme_sp3").Height(EditorTheme.Padding * 3);

        // ── Font ──
        Origami.Foldout(paper, "pref_ft_general", "Font").Body(() =>
        {
            PrefTextField(paper, s, "Font", theme.DefaultFontName, v => theme.DefaultFontName = v);
            PrefTextField(paper, s, "Bold Font", theme.DefaultBoldFontName, v => theme.DefaultBoldFontName = v);
        });

        // ── Sizing ──
        Origami.Foldout(paper, "pref_sz_general", "General Sizing").Body(() =>
        {
            SzSlider(paper, s, "User Scale", theme.UserScale, 0.5f, 2, v => theme.UserScale = v, false);
            SzSlider(paper, s, "Font Size", theme.FontSize, 8, 32, v => theme.FontSize = v);
            SzSlider(paper, s, "Row Height", theme.RowHeight, 16, 40, v => theme.RowHeight = v);
            SzSlider(paper, s, "Menu Bar Height", theme.MenuBarHeight, 18, 48, v => theme.MenuBarHeight = v);
            SzSlider(paper, s, "Label Width", theme.LabelWidth, 60, 240, v => theme.LabelWidth = v);
            SzSlider(paper, s, "Spacing", theme.Spacing, 0, 12, v => theme.Spacing = v);
            SzSlider(paper, s, "Padding", theme.Padding, 0, 16, v => theme.Padding = v);
            SzSlider(paper, s, "Roundness", theme.Roundness, 0, 20, v => theme.Roundness = v);
        });

        Origami.Foldout(paper, "pref_sz_docking", "Docking").Body(() =>
        {
            SzSlider(paper, s, "Splitter Size", theme.SplitterSize, 4, 24, v => theme.SplitterSize = v);
            SzSlider(paper, s, "Dock Padding", theme.DockPadding, 0, 24, v => theme.DockPadding = v);
        });

        Origami.Foldout(paper, "pref_sz_tabs", "Tabs").Body(() =>
        {
            SzSlider(paper, s, "Tab Bar Height", theme.TabBarHeight, 18, 40, v => theme.TabBarHeight = v);
            SzSlider(paper, s, "Tab Padding", theme.TabPadding, 4, 24, v => theme.TabPadding = v);
        });
    }

    private void DrawRamp(Paper paper, EditorSettings s, string name, ColorRamp ramp, string[] stopNames)
    {
        Origami.Foldout(paper, $"pref_ramp_{name}", name).Body(() =>
        {
            var primaryColor = HexToVColor(ramp.Primary);
            InspectorRow.Draw(paper, $"pref_ramp_{name}_primary", $"{name} Primary", () =>
                Origami.ColorField(paper, $"pref_ramp_{name}_primary_cf", primaryColor, v =>
                {
                    ramp.Primary = VColorToHex(v);
                    s.ApplyTheme();
                }).Show());

            Origami.Checkbox(paper, $"pref_ramp_{name}_override", ramp.OverrideAll, v =>
                {
                    ramp.OverrideAll = v;
                    if (v && ramp.Overrides != null)
                    {
                        for (int i = 0; i < ramp.StopCount; i++)
                            ramp.Overrides[i] = ColorRamp.ColorToHex(ramp.GetStop(i));
                    }
                    s.ApplyTheme();
                })
                .LabelRight("Override All Stops").Show();

            if (ramp.OverrideAll && ramp.Overrides != null)
            {
                for (int i = 0; i < ramp.StopCount; i++)
                {
                    int idx = i;
                    string label = i < stopNames.Length ? stopNames[i] : $"Stop {i}";
                    var stopColor = HexToVColor(ramp.Overrides[i]);
                    InspectorRow.Draw(paper, $"pref_ramp_{name}_{i}", label, () =>
                        Origami.ColorField(paper, $"pref_ramp_{name}_{idx}_cf", stopColor, v =>
                        {
                            ramp.Overrides![idx] = VColorToHex(v);
                            s.ApplyTheme();
                        }).Show());
                }
            }
        });
    }


    private void PrefTextField(Paper paper, EditorSettings s, string label, string value, Action<string> set)
    {
        string baseId = $"pref_ft_{label.Replace(" ", "_")}";
        InspectorRow.Draw(paper, baseId, label, () =>
            Origami.TextField(paper, $"{baseId}_v", value, v =>
            {
                set(v);
                //s.ApplyTheme();
                s.Save();
            }).Show());
    }


    private void SzSlider(Paper paper, EditorSettings s, string label, float value, float min, float max, Action<float> set, bool applyOnSlide = true)
    {
        string baseId = $"pref_sz_{label.Replace(" ", "_")}";
        InspectorRow.Draw(paper, baseId, label, () =>
            Origami.Slider(paper, $"{baseId}_v", value, v =>
            {
                set(MathF.Round(v, 1));
                if (applyOnSlide)
                {
                    s.ApplyTheme();
                    s.Save();
                }
            }, min, max).Format("F1").Show());
    }

    // ================================================================
    //  Shortcuts
    // ================================================================

    private void DrawShortcuts(Paper paper, Prowl.Scribe.FontFile font, float w)
    {
        // Handle rebinding input each frame
        if (_rebindingId != null)
        {
            ShortcutManager.IsRebinding = true;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _rebindingId = null;
                ShortcutManager.IsRebinding = false;
            }
            else
            {
                // Check for any non-modifier key press
                foreach (KeyCode key in Enum.GetValues<KeyCode>())
                {
                    if (key == KeyCode.Unknown || key == KeyCode.Escape) continue;
                    // Skip modifier keys themselves they're captured via flags
                    if (key is KeyCode.ShiftLeft or KeyCode.ShiftRight
                        or KeyCode.ControlLeft or KeyCode.ControlRight
                        or KeyCode.AltLeft or KeyCode.AltRight
                        or KeyCode.SuperLeft or KeyCode.SuperRight) continue;

                    if (Input.GetKeyDown(key))
                    {
                        var binding = new ShortcutBinding(key, Input.IsCtrlPressed, Input.IsShiftPressed, Input.IsAltPressed);
                        ShortcutManager.SetOverride(_rebindingId, binding);
                        _rebindingId = null;
                        ShortcutManager.IsRebinding = false;
                        break;
                    }
                }
            }
        }

        Origami.Header(paper, "pref_sc_hdr", $"{EditorIcons.Keyboard}  Shortcuts").Underline().Show();

        // Search bar
        Origami.SearchField(paper, "pref_sc_search", _shortcutSearch,
            v => _shortcutSearch = v, "Search shortcuts...").Show();

        paper.Box("pref_sc_sp1").Height(EditorTheme.Spacing * 2);

        // Reset All button
        using (paper.Row("pref_sc_actions").Height(EditorTheme.RowHeight).ChildLeft(EditorTheme.Padding).Enter())
        {
            Origami.Button(paper, "pref_sc_reset_all", $"{EditorIcons.RotateLeft}  Reset All to Defaults", () => ShortcutManager.ClearAllOverrides()).Width(200).Show();
        }

        paper.Box("pref_sc_sp2").Height(EditorTheme.Padding * 2);

        // Group by category
        string? lastCategory = null;
        foreach (var shortcut in ShortcutManager.GetAllShortcuts())
        {
            // Filter by search
            if (!string.IsNullOrEmpty(_shortcutSearch) &&
                !shortcut.DisplayName.Contains(_shortcutSearch, StringComparison.OrdinalIgnoreCase) &&
                !shortcut.Id.Contains(_shortcutSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            // Category header
            if (shortcut.Category != lastCategory)
            {
                lastCategory = shortcut.Category;
                paper.Box($"pref_sc_sp_{lastCategory}").Height(EditorTheme.Spacing * 2);
                Origami.Header(paper, $"pref_sc_cat_{lastCategory}", lastCategory).Underline().Show();
            }

            bool isRebinding = _rebindingId == shortcut.Id;
            bool isOverridden = shortcut.Override != null;
            string bindDisplay = isRebinding
                ? "Press key...  (Esc to cancel)"
                : ShortcutManager.GetDisplayString(shortcut.Binding);

            using (paper.Row($"pref_sc_{shortcut.Id}")
                .Height(EditorTheme.RowHeight)
                .ChildLeft(EditorTheme.Padding).RowBetween(EditorTheme.Spacing * 2)
                .Enter())
            {
                // Display name
                paper.Box($"pref_sc_name_{shortcut.Id}")
                    .Width(w * 0.4f).Height(EditorTheme.RowHeight)
                    .Text(shortcut.DisplayName, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);

                // Binding button
                paper.Box($"pref_sc_bind_{shortcut.Id}")
                    .Width(160).Height(EditorTheme.RowHeight - 4).Rounded(EditorTheme.Roundness * 0.5f)
                    .BackgroundColor(isRebinding ? EditorTheme.Purple300 : EditorTheme.Neutral200)
                    .Hovered.BackgroundColor(isRebinding ? EditorTheme.Purple300 : EditorTheme.Ink200).End()
                    .Text(bindDisplay, font)
                    .TextColor(isRebinding ? EditorTheme.Ink500 : (isOverridden ? EditorTheme.Purple500 : EditorTheme.Ink400))
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleCenter)
                    .OnClick(shortcut.Id, (id, _) =>
                    {
                        _rebindingId = _rebindingId == id ? null : id;
                        ShortcutManager.IsRebinding = _rebindingId != null;
                    });

                // Reset button (only if overridden)
                if (isOverridden)
                {
                    paper.Box($"pref_sc_rst_{shortcut.Id}")
                        .Width(50).Height(EditorTheme.RowHeight - 4).Rounded(EditorTheme.Roundness * 0.5f)
                        .BackgroundColor(EditorTheme.Neutral200)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text("Reset", font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleCenter)
                        .OnClick(shortcut.Id, (id, _) => ShortcutManager.ClearOverride(id));
                }
            }
        }
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
