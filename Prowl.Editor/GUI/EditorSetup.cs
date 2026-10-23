// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.ScriptEditor;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

namespace Prowl.Editor.GUI;

/// <summary>
/// The first launch setup: language, scale, script editor, theme and layout density, applied live as they are
/// picked. Shown once, and again from Window > General > Editor Setup.
/// </summary>
public static class EditorSetup
{
    private const float Width = 720f;
    private static readonly float[] s_scales = [0.85f, 1f, 1.15f, 1.3f, 1.5f];

    private static DialogModal? s_dialog;

    [MenuItem("Window/General/Editor Setup", priority: 8)]
    public static void Show()
    {
        if (s_dialog != null && Modal.Contains(s_dialog)) return;

        s_dialog = Origami.Dialog(Loc.Get("setup.title"), Draw, Width);
        s_dialog.CloseOnEscape = true;
        s_dialog.CloseOnBackdrop = false;
        s_dialog.OnDismissed = _ => Finish();
        s_dialog.Button(Loc.Get("setup.preferences"), () =>
            {
                Close();
                EditorApplication.Instance?.OpenPanel(typeof(PreferencesPanel));
            })
            .Button(Loc.Get("setup.done"), Close, OrigamiVariant.Primary);
    }

    /// <summary>Opens the setup the first time the editor shows a project.</summary>
    public static void ShowOnFirstLaunch()
    {
        if (!EditorSettings.Instance.SetupDone) Show();
    }

    private static void Close()
    {
        if (s_dialog != null) Modal.Remove(s_dialog);
        Finish();
    }

    private static void Finish()
    {
        s_dialog = null;
        if (EditorSettings.Instance.SetupDone) return;
        EditorSettings.Instance.SetupDone = true;
        EditorSettings.Instance.Save();
    }

    private static void Draw(Paper paper)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        EditorSettings s = EditorSettings.Instance;
        float height = MathF.Min(520f, (float)paper.ScreenRect.Size.Y * 0.62f);

        Origami.ScrollView(paper, "setup_scroll", Width - 2f, height).Body(() =>
        {
            using (paper.Column("setup_col").Height(UnitValue.Auto).Padding(16, 16, 12, 16).Gap(EditorTheme.Spacing * 2).Enter())
            {
                paper.Box("setup_intro").Height(UnitValue.Auto).IsNotInteractable()
                    .Text(Loc.Get("setup.intro"), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).Wrap(Scribe.TextWrapMode.Wrap);

                EditorGUI.SettingsRow(paper, "setup_lang", Loc.Get("pref.language"), () =>
                    Origami.Dropdown(paper, "setup_lang_v", LocaleHelper.GetIndex(Loc.CurrentLocale), LocaleHelper.SetLocale, LocaleHelper.Names).Show());

                EditorGUI.SettingsRow(paper, "setup_scale", Loc.Get("pref.user_scale"), () =>
                {
                    using (paper.Row("setup_scale_chips").Height(UnitValue.Auto).Gap(EditorTheme.Spacing * 2).Enter())
                        foreach (float scale in s_scales)
                            EditorGUI.Chip(paper, $"setup_scale_{scale}", $"{scale * 100:0}%", MathF.Abs(s.Theme.UserScale - scale) < 0.01f, () =>
                            {
                                s.Theme.UserScale = scale;
                                s.ApplyTheme();
                                s.Save();
                            }, height: 24f);
                });

                ScriptEditorPreferences script = Preferences.Get<ScriptEditorPreferences>();
                EditorGUI.SettingsRow(paper, "setup_script", Loc.Get("setup.script_editor"), () =>
                    Origami.EnumDropdown(paper, "setup_script_v", script.OpenScriptsIn, v =>
                    {
                        script.OpenScriptsIn = v;
                        Preferences.Changed(Preferences.SectionOf(script));
                    }).Show());

                PreferencesPanel.DrawThemePresets(paper, font, s, s.Theme, saveButtons: false);
            }
        });
    }
}
