using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Popups;

/// <summary>
/// Modal for creating a new C# script. A file name is required (C# identifiers only) and
/// the user picks from a set of starter templates. The chosen name is used both as the
/// file name and the class name, so we validate it against C# identifier rules before
/// enabling Create.
/// </summary>
public static class NewScriptDialog
{
    // Templates come from ScriptTemplateRegistry; snapshotted when the dialog opens so the
    // selection index stays stable across a single session (a reload between opens is fine).
    private static IReadOnlyList<ScriptTemplate> s_templates = Array.Empty<ScriptTemplate>();

    // Per-open state reset each time Open() is called
    private static string s_folder = "";
    private static string s_name = "NewScript";
    private static int s_selectedIndex;
    private static Action<string>? s_onCreated;

    /// <summary>
    /// Show the modal. <paramref name="relativeFolder"/> is the Assets-relative folder the
    /// new script will be created in; <paramref name="onCreated"/> receives the final
    /// relative path on success (e.g. for a post-create ping / selection).
    /// </summary>
    public static void Open(string relativeFolder, Action<string>? onCreated = null)
    {
        s_folder = relativeFolder ?? "";
        s_name = SuggestUniqueName(s_folder, "NewScript");
        s_selectedIndex = 0;
        s_onCreated = onCreated;

        ScriptTemplateRegistry.Initialize();
        s_templates = ScriptTemplateRegistry.Templates;
        if (s_templates.Count == 0)
        {
            Runtime.Debug.LogWarning("NewScriptDialog: no script templates registered.");
            return;
        }

        Modal.Push(new DialogModal { Title = "Create C# Script", DrawContent = DrawContent, Width = 560 });
    }

    private static void DrawContent(Paper paper)
    {
        var font = EditorTheme.DefaultFont;
        (bool ok, string? error) = Validate(s_name, s_folder);

        const float bodyHeight = 300f;
        const float listWidth = 220f;

        // Template list on the left, details + name field on the right.
        using (paper.Row("scr_body").Height(bodyHeight).RowBetween(10).Enter())
        {
            // Template list wrapped in a ScrollView so 14+ templates don't overflow.
            using (paper.Box("scr_tpls_frame")
                       .Width(listWidth).Height(bodyHeight)
                       .BackgroundColor(EditorTheme.Neutral200)
                       .BorderWidth(1).BorderColor(EditorTheme.Neutral100)
                       .Rounded(4)
                       .Enter())
            {
                Origami.ScrollView(paper, "scr_tpls_scroll", listWidth, bodyHeight)
                    .Padding(4, 4, 4, 4)
                    .ColSpacing(2)
                    .Body(() =>
                    {
                        for (int i = 0; i < s_templates.Count; i++)
                        {
                            int captured = i;
                            var tpl = s_templates[i];
                            bool isSel = i == s_selectedIndex;

                            using (paper.Row($"scr_tpl_{i}")
                                       .Width(UnitValue.Stretch()).Height(28).Rounded(3)
                                       .BackgroundColor(isSel ? EditorTheme.Purple400 : Color.Transparent)
                                       .Hovered.BackgroundColor(isSel ? EditorTheme.Purple400 : EditorTheme.Ink200)
                                       .End()
                                       .OnClick(captured, (idx, _) => s_selectedIndex = idx)
                                       .Enter())
                            {
                                if (font != null)
                                {
                                    paper.Box($"scr_tpl_ico_{i}")
                                        .Width(22).Height(28)
                                        .Text(tpl.Icon, font)
                                        .TextColor(EditorTheme.Ink500)
                                        .FontSize(11f).Alignment(TextAlignment.MiddleCenter);
                                    paper.Box($"scr_tpl_name_{i}")
                                        .Width(UnitValue.Stretch()).Height(28)
                                        .Text(tpl.Name, font)
                                        .TextColor(EditorTheme.Ink500)
                                        .FontSize(EditorTheme.FontSize - 1)
                                        .Alignment(TextAlignment.MiddleLeft);
                                }
                            }
                        }
                    });
            }

            // Details / inputs
            using (paper.Column("scr_details")
                       .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                       .RowBetween(8).Enter())
            {
                var tpl = s_templates[s_selectedIndex];
                if (font != null)
                {
                    paper.Box("scr_tpl_title")
                        .Width(UnitValue.Stretch()).Height(22)
                        .Text(tpl.Name, font)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize + 1)
                        .Alignment(TextAlignment.MiddleLeft);

                    // Auto-height + wrap so long descriptions aren't clipped.
                    paper.Box("scr_tpl_desc")
                        .Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(48)
                        .Text(tpl.Description, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.Left)
                        .Wrap(Scribe.TextWrapMode.Wrap);
                }

                Origami.TextField(paper, "scr_name", s_name, v => s_name = v)
                    .Placeholder("Name").Width(UnitValue.Stretch()).Show();

                // Validation hint green when valid, red when not. Always visible so the
                // user knows why Create is disabled.
                if (font != null)
                {
                    string hint = ok ? $"Will create {s_name}.cs" : (error ?? "");
                    var col = ok ? Color.FromArgb(255, 90, 180, 100) : Color.FromArgb(255, 220, 110, 110);
                    paper.Box("scr_hint")
                        .Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(18)
                        .Text(hint, font)
                        .TextColor(col)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.Left)
                        .Wrap(Scribe.TextWrapMode.Wrap);

                    // Path preview
                    string folderDisplay = string.IsNullOrEmpty(s_folder) ? "Assets" : $"Assets/{s_folder}";
                    paper.Box("scr_path")
                        .Width(UnitValue.Stretch()).Height(16)
                        .Text(folderDisplay, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
        }

        // Footer buttons right-aligned Cancel + Create. The Create button is a real
        // EditorGUI.Button when valid; when invalid we render a visually-disabled Box
        // so the user sees the error hint rather than clicking a dead button.
        using (paper.Row("scr_btns").Height(EditorTheme.RowHeight).ChildLeft(UnitValue.Stretch()).RowBetween(8).Enter())
        {
            Origami.Button(paper, "scr_cancel", "Cancel", () => { Modal.Pop(); }).Width(90).Show();

            if (ok)
            {
                using (paper.Box("scr_create")
                           .Width(110).Height(EditorTheme.RowHeight).Rounded(3)
                           .BackgroundColor(EditorTheme.Purple400)
                           .Hovered.BackgroundColor(EditorTheme.Purple500).End()
                           .BorderWidth(1).BorderColor(EditorTheme.Purple400)
                           .OnClick(0, (_, _) => DoCreate())
                           .Enter())
                {
                    if (font != null)
                        paper.Box("scr_create_lbl")
                            .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                            .Text("Create", font)
                            .TextColor(Color.White)
                            .FontSize(EditorTheme.FontSize)
                            .Alignment(TextAlignment.MiddleCenter)
                            .IsNotInteractable();
                }
            }
            else
            {
                using (paper.Box("scr_create_disabled")
                           .Width(110).Height(EditorTheme.RowHeight).Rounded(3)
                           .BackgroundColor(EditorTheme.Neutral200)
                           .BorderWidth(1).BorderColor(EditorTheme.Neutral100)
                           .Enter())
                {
                    if (font != null)
                        paper.Box("scr_create_dis_lbl")
                            .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                            .Text("Create", font)
                            .TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize)
                            .Alignment(TextAlignment.MiddleCenter)
                            .IsNotInteractable();
                }
            }
        }
    }

    private static void DoCreate()
    {
        (bool ok, _) = Validate(s_name, s_folder);
        if (!ok) return;

        string absFolder = AssetCreateMenu.GetAbsoluteFolder(s_folder);
        if (!Directory.Exists(absFolder)) return;

        string fileName = s_name + ".cs";
        string absPath = Path.Combine(absFolder, fileName);
        var tpl = s_templates[s_selectedIndex];
        File.WriteAllText(absPath, tpl.Generate(s_name));

        string relPath = string.IsNullOrEmpty(s_folder) ? fileName : s_folder + "/" + fileName;
        Runtime.Debug.Log($"Created script: {relPath}");
        s_onCreated?.Invoke(relPath);
        Modal.Pop();
    }

    // --- Validation -----------------------------------------------------------------

    private static readonly Regex s_identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly HashSet<string> s_reserved = new(StringComparer.Ordinal)
    {
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while"
    };

    private static (bool ok, string? error) Validate(string name, string folder)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Name is required.");
        if (!s_identifier.IsMatch(name))
            return (false, "Name must start with a letter or underscore, then letters/digits only.");
        if (s_reserved.Contains(name))
            return (false, $"'{name}' is a reserved C# keyword.");

        string abs = AssetCreateMenu.GetAbsoluteFolder(folder);
        if (File.Exists(Path.Combine(abs, name + ".cs")))
            return (false, $"{name}.cs already exists in this folder.");

        return (true, null);
    }

    private static string SuggestUniqueName(string folder, string baseName)
    {
        string abs = AssetCreateMenu.GetAbsoluteFolder(folder);
        if (!Directory.Exists(abs)) return baseName;

        if (!File.Exists(Path.Combine(abs, baseName + ".cs"))) return baseName;
        for (int i = 1; i < 1000; i++)
        {
            string candidate = baseName + i;
            if (!File.Exists(Path.Combine(abs, candidate + ".cs"))) return candidate;
        }

        return baseName;
    }

    // --- Templates ------------------------------------------------------------------
    // Built-in templates. Users add their own by tagging a static method
    //   [ScriptTemplate("Name", "Description", EditorIcons.Foo)]
    //   public static string Generate(string className) => $"public class {className} {{}}";
    // on any static method returning string and taking a single string (className).

    [ScriptTemplate("MonoBehaviour",
        "A basic component with Start and Update lifecycle hooks. Start where most scripts begin.",
        EditorIcons.FileCode, Order = 0)]
    private static string BasicMonoBehaviour(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewMonoBehaviour.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Character Controller",
        "First/third-person character with WASD + jump. RequireComponent auto-adds a CharacterController when the script is attached.",
        EditorIcons.PersonRunning, Order = 10)]
    private static string CharacterControllerTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewCharacterController.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Rigidbody Mover",
        "Physics-based movement via Rigidbody3D velocity. Runs in FixedUpdate and plays well with other physics bodies.",
        EditorIcons.Cube, Order = 20)]
    private static string RigidbodyMoverTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewRigidbodyMover.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("First Person Camera",
        "Mouse-look yaw/pitch for a camera rig. Only active while the cursor is locked pair with a Character Controller.",
        EditorIcons.Video, Order = 30)]
    private static string FirstPersonCameraTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewFirstPersonCamera.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Fly Camera",
        "Right-click to enter fly mode with WASD + EQ movement, mouse look, and Shift to sprint. Great for debug or spectator cameras.",
        EditorIcons.JetFighter, Order = 35)]
    private static string FlyCameraTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewFlyCamera.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Follow Camera",
        "Smoothly follows a target Transform with a configurable offset. Good for third-person / chase cameras.",
        EditorIcons.Camera, Order = 40)]
    private static string FollowCameraTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewFollowCamera.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Look At Target",
        "Rotates the GameObject each frame to face an assigned target Transform (turrets, NPC heads, billboards).",
        EditorIcons.Eye, Order = 50)]
    private static string LookAtTargetTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewLookAtTarget.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Rotator",
        "Continuously rotates the GameObject on a configurable axis. Handy smoke-test component.",
        EditorIcons.ArrowsRotate, Order = 60)]
    private static string RotatorTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewRotator.cstemplate").Replace("{[className]}", className);
    }

    [ScriptTemplate("Health",
        "Gameplay component with hit points, Damage/Heal helpers, and Died/HealthChanged events. Call Damage() from weapons.",
        EditorIcons.Heart, Order = 70)]
    private static string HealthTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewHealth.cstemplate").Replace("{[className]}", className);
    }

    [ScriptTemplate("Timed Destroy",
        "Destroys the GameObject after a configurable delay. Useful for projectiles, effects, and debris.",
        EditorIcons.Clock, Order = 80)]
    private static string TimedDestroyTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewTimedDestroy.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Prefab Spawner",
        "Spawns instances of an assigned prefab on key press or on a timer. Demonstrates the Prefab.Instantiate API.",
        EditorIcons.WandMagicSparkles, Order = 90)]
    private static string SpawnerTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewSpawner.cstemplate").Replace("{[className]}", className);
    }

    [ScriptTemplate("Audio One-Shot",
        "Plays an AudioClip on Start or when Play() is called. Component picks up an AudioSource automatically.",
        EditorIcons.VolumeHigh, Order = 100)]
    private static string AudioOneShotTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewAudioOneShot.cstemplate")
            .Replace("{[className]}", className);
    }

    [ScriptTemplate("Singleton Manager",
        "Game manager pattern with a static Instance. Self-registers on enable and clears on disable.",
        EditorIcons.Crown, Order = 110)]
    private static string SingletonTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewSingleton.cstemplate").Replace("{[className]}", className);
    }

    [ScriptTemplate("Data Asset",
        "A savable data asset (ScriptableObject-style) that appears in Assets > Create. Holds designer-tunable config data.",
        EditorIcons.Database, Order = 120)]
    private static string DataAssetTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewDataAsset.cstemplate").Replace("{[className]}", className);
    }

    [ScriptTemplate("Plain C# Class",
        "A regular C# class not a MonoBehaviour. Useful for pure-data types, services, and helpers.",
        EditorIcons.File, Order = 130)]
    private static string PlainClassTemplate(string className)
    {
        return EditorApplication.GetEmbeddedResourceText("NewPlainClass.cstemplate")
            .Replace("{[className]}", className);
    }
}
