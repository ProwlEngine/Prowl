using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Modal for creating a new C# script. A file name is required (C# identifiers only) and
/// the user picks from a set of starter templates. The chosen name is used both as the
/// file name and the class name, so we validate it against C# identifier rules before
/// enabling Create.
/// </summary>
public static class NewScriptDialog
{
    private sealed class Template
    {
        public string Name;
        public string Description;
        public string Icon;
        public Func<string, string> Generate; // className → source

        public Template(string name, string description, string icon, Func<string, string> generate)
        {
            Name = name; Description = description; Icon = icon; Generate = generate;
        }
    }

    private static readonly Template[] s_templates = BuildTemplates();

    // Per-open state — reset each time Open() is called
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

        ModalDialog.Show(new ModalDialogEntry("Create C# Script", DrawContent, width: 560));
    }

    private static void DrawContent(Paper paper)
    {
        var font = EditorTheme.DefaultFont;
        (bool ok, string? error) = Validate(s_name, s_folder);

        // Template list on the left, details + name field on the right.
        using (paper.Row("scr_body").Height(280).RowBetween(10).Enter())
        {
            // Template list
            using (paper.Column("scr_tpls")
                .Width(220).Height(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.Neutral200)
                .BorderWidth(1).BorderColor(EditorTheme.Neutral100)
                .Rounded(4).ChildTop(4).ChildBottom(4).RowBetween(2)
                .Enter())
            {
                for (int i = 0; i < s_templates.Length; i++)
                {
                    int captured = i;
                    var tpl = s_templates[i];
                    bool isSel = i == s_selectedIndex;

                    using (paper.Row($"scr_tpl_{i}")
                        .Height(28).Margin(4, 0, 0, 0).Rounded(3)
                        .BackgroundColor(isSel ? EditorTheme.Purple400 : Color.Transparent)
                        .Hovered.BackgroundColor(isSel ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
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
                        .Height(22)
                        .Text(tpl.Name, font)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize + 1)
                        .Alignment(TextAlignment.MiddleLeft);

                    paper.Box("scr_tpl_desc")
                        .Height(64)
                        .Text(tpl.Description, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.TopLeft);
                }

                EditorGUI.TextField(paper, "scr_name", "Name", s_name)
                    .OnValueChanged(v => s_name = v);

                // Validation hint — green when valid, red when not. Always visible so the
                // user knows why Create is disabled.
                if (font != null)
                {
                    string hint = ok ? $"Will create {s_name}.cs" : (error ?? "");
                    var col = ok ? Color.FromArgb(255, 90, 180, 100) : Color.FromArgb(255, 220, 110, 110);
                    paper.Box("scr_hint")
                        .Height(18)
                        .Text(hint, font)
                        .TextColor(col)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleLeft);
                }

                // Path preview
                if (font != null)
                {
                    string folderDisplay = string.IsNullOrEmpty(s_folder) ? "Assets" : $"Assets/{s_folder}";
                    paper.Box("scr_path")
                        .Height(16)
                        .Text(folderDisplay, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
        }

        // Footer buttons — we render them inside the content so the Create button can be
        // disabled based on validation state (the modal's built-in Button() has no disabled
        // styling). Cancel always works.
        using (paper.Row("scr_btns").Height(32).ChildLeft(UnitValue.Stretch()).RowBetween(8).Enter())
        {
            EditorGUI.Button(paper, "scr_cancel", "Cancel", width: 90)
                .OnValueChanged(_ => ModalDialog.Close());

            var createBuilder = paper.Box("scr_create")
                .Width(110).Height(EditorTheme.RowHeight).Rounded(3)
                .BackgroundColor(ok ? EditorTheme.Purple400 : EditorTheme.Neutral200)
                .BorderWidth(1).BorderColor(ok ? EditorTheme.Purple400 : EditorTheme.Neutral100);
            if (ok)
                createBuilder.Hovered.BackgroundColor(EditorTheme.Purple500).End()
                    .OnClick(0, (_, _) => DoCreate());

            if (font != null)
            {
                paper.Box("scr_create_lbl")
                    .HookToParent()
                    .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                    .Text("Create", font)
                    .TextColor(ok ? Color.White : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter)
                    .IsNotInteractable();
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
        ModalDialog.Close();
    }

    // ─── Validation ─────────────────────────────────────────────────────────────────

    private static readonly Regex s_identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> s_reserved = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class",
        "const","continue","decimal","default","delegate","do","double","else","enum","event",
        "explicit","extern","false","finally","fixed","float","for","foreach","goto","if",
        "implicit","in","int","interface","internal","is","lock","long","namespace","new",
        "null","object","operator","out","override","params","private","protected","public",
        "readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static",
        "string","struct","switch","this","throw","true","try","typeof","uint","ulong",
        "unchecked","unsafe","ushort","using","virtual","void","volatile","while"
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

    // ─── Templates ──────────────────────────────────────────────────────────────────

    private static Template[] BuildTemplates() => new[]
    {
        new Template("MonoBehaviour", "A basic component with Start and Update lifecycle hooks.",
            EditorIcons.FileCode, BasicMonoBehaviour),
        new Template("Character Controller", "First-person / third-person character movement. RequireComponent pulls in a CharacterController automatically.",
            EditorIcons.PersonRunning, CharacterControllerTemplate),
        new Template("First Person Camera", "Mouse-look yaw/pitch for a camera rig. Drop on a camera GameObject.",
            EditorIcons.Video, FirstPersonCameraTemplate),
        new Template("Rotator", "Continuously rotates the GameObject on a configurable axis. Handy smoke-test component.",
            EditorIcons.ArrowsRotate, RotatorTemplate),
        new Template("Singleton Manager", "Game manager pattern with a static Instance. Self-registers on enable and clears on disable.",
            EditorIcons.Crown, SingletonTemplate),
        new Template("Plain C# Class", "A regular C# class (not a MonoBehaviour). Useful for data types and services.",
            EditorIcons.File, PlainClassTemplate),
    };

    private static string BasicMonoBehaviour(string className) => $@"using Prowl.Runtime;

public class {className} : MonoBehaviour
{{
    public override void Start()
    {{
    }}

    public override void Update()
    {{
    }}
}}
";

    private static string CharacterControllerTemplate(string className) => $@"using Prowl.Runtime;
using Prowl.Vector;

[RequireComponent(typeof(CharacterController))]
public class {className} : MonoBehaviour
{{
    public float MoveSpeed = 6f;
    public float JumpSpeed = 8f;
    public float Gravity = -20f;

    private CharacterController _controller;
    private Float3 _velocity;

    public override void Start()
    {{
        _controller = GetComponent<CharacterController>();
    }}

    public override void Update()
    {{
        Float2 wasd = Input.GetWASD();
        Float3 planar = Transform.Right * wasd.X + Transform.Forward * wasd.Y;
        _velocity.X = planar.X * MoveSpeed;
        _velocity.Z = planar.Z * MoveSpeed;

        if (_controller.IsGrounded)
        {{
            _velocity.Y = 0f;
            if (Input.GetKeyDown(KeyCode.Space))
                _velocity.Y = JumpSpeed;
        }}
        else
        {{
            _velocity.Y += Gravity * (float)Time.DeltaTime;
        }}

        _controller.Move(_velocity * (float)Time.DeltaTime);
    }}
}}
";

    private static string FirstPersonCameraTemplate(string className) => $@"using Prowl.Runtime;
using Prowl.Vector;

public class {className} : MonoBehaviour
{{
    public float Sensitivity = 0.15f;
    public float MinPitch = -89f;
    public float MaxPitch = 89f;

    private float _yaw;
    private float _pitch;

    public override void Start()
    {{
        Float3 euler = Transform.LocalEulerAngles;
        _pitch = euler.X;
        _yaw = euler.Y;
    }}

    public override void Update()
    {{
        if (!Input.CursorLocked) return;

        Float2 delta = Input.MouseDelta;
        _yaw += delta.X * Sensitivity;
        _pitch = System.Math.Clamp(_pitch - delta.Y * Sensitivity, MinPitch, MaxPitch);

        Transform.LocalEulerAngles = new Float3(_pitch, _yaw, 0f);
    }}
}}
";

    private static string RotatorTemplate(string className) => $@"using Prowl.Runtime;
using Prowl.Vector;

public class {className} : MonoBehaviour
{{
    public Float3 Axis = new Float3(0, 1, 0);
    public float DegreesPerSecond = 90f;

    public override void Update()
    {{
        Transform.Rotate(Axis, DegreesPerSecond * (float)Time.DeltaTime);
    }}
}}
";

    private static string SingletonTemplate(string className) => $@"using Prowl.Runtime;

public class {className} : MonoBehaviour
{{
    public static {className}? Instance {{ get; private set; }}

    public override void OnEnable()
    {{
        if (Instance != null && Instance != this)
        {{
            Debug.LogWarning($""Multiple {{nameof({className})}} instances — keeping the first."");
            return;
        }}
        Instance = this;
    }}

    public override void OnDisable()
    {{
        if (Instance == this) Instance = null;
    }}
}}
";

    private static string PlainClassTemplate(string className) => $@"namespace Game;

public class {className}
{{
}}
";
}
