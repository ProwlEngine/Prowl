using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;
using Prowl.OrigamiUI.Graphite;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.ScriptEditor;

/// <summary>Whether a double clicked script opens in the editor's own code editor or the system's default app.</summary>
public enum ScriptEditorMode
{
    BuiltIn,
    External,
}

/// <summary>The line endings a script is saved with.</summary>
public enum LineEndingMode { Keep, LF, CRLF }

/// <summary>The encoding a script is saved with.</summary>
public enum FileEncodingMode { Keep, Utf8, Utf8Bom }

/// <summary>When scripts save themselves.</summary>
public enum ScriptAutoSave { Off, AfterDelay, OnFocusLoss }

/// <summary>How the built in code editor looks and behaves. Changes apply to open scripts straight away.</summary>
[EditorPreferences("Script Editor", EditorIcons.Code, order: 10, ProjectOverridable = true)]
public sealed class ScriptEditorPreferences : EditorPreferences
{
    [Tooltip("Open double clicked scripts in the built in code editor, or in the system's default app.")]
    public ScriptEditorMode OpenScriptsIn = ScriptEditorMode.BuiltIn;

    /// <summary>Font family or font file for code. Empty follows the editor's mono font. Picked above the other settings.</summary>
    [HideInInspector] public string Font = "";

    public const float DefaultFontSize = 15f;

    [Header("Text")]
    [Range(6, 48), Tooltip("Ctrl+wheel in a script changes this too. The status bar shows it as a zoom of the default size.")]
    public float FontSize = DefaultFontSize;
    [Range(1, 2.5f), Tooltip("Row height as a multiple of the font size.")] public float LineHeight = 1.5f;
    [Range(1, 8)] public int TabSize = 4;
    [Tooltip("Tab and auto indent insert spaces instead of tab characters.")] public bool InsertSpaces = true;
    [Tooltip("Columns with a vertical guide line.")] public List<int> Rulers = [120];
    [Tooltip("Joined glyphs such as arrows and comparison operators, in fonts that have them.")] public bool Ligatures = true;
    [Tooltip("Atlas quality for code text, separate from the rest of the editor's text quality.")] public Scribe.FontQuality TextQuality = Scribe.FontQuality.High;

    [Header("Display")]
    public bool ShowLineNumbers = true;
    public bool ShowFolding = true;
    public bool ShowMinimap = true;
    [Range(40, 200)] public float MinimapWidth = 90f;
    public bool ShowIndentGuides = true;
    public bool ShowWhitespace;
    public bool ShowStatusBar = true;
    public bool HighlightCurrentLine = true;
    public bool HighlightMatchingBrackets = true;
    [Tooltip("Marks other occurrences of the word under the caret.")] public bool HighlightOccurrences = true;
    [Tooltip("Shows reference counts after declarations.")] public bool ShowCodeLens = true;

    [Tooltip("Marks spaces and tabs at the end of lines.")] public bool ShowTrailingWhitespace;
    [Tooltip("Colours brackets by how deeply they are nested.")] public bool BracketPairColors;
    [Tooltip("Colours indent guides by their level.")] public bool RainbowIndentGuides;

    [Header("Scrolling and Caret")]
    public bool SmoothScrolling = true;
    public bool ScrollBeyondLastLine = true;
    [Range(0, 20), Tooltip("Lines kept visible above and below the caret.")] public int ScrollMargin = 3;
    public bool CaretBlink = true;
    public CaretStyle CaretStyle = CaretStyle.Line;
    [Range(1, 4), Tooltip("Width of the line caret in pixels.")] public float CaretWidth = 2f;
    [Tooltip("The key held while clicking to add a caret. The other one goes to the definition under the pointer.")]
    public MultiCaretModifier MultiCaretModifier = MultiCaretModifier.Alt;
    [Tooltip("Drag selected text to move it, or hold Ctrl to copy it.")] public bool DragAndDropText = true;

    [Header("Editing")]
    public bool AutoClosePairs = true;
    public bool AutoIndent = true;
    [Tooltip("Formats the statement or block a typed ';' or '}' closes.")] public bool FormatOnType = true;

    [Header("Completion and Analysis")]
    [Tooltip("Opens completion as soon as a word is typed, not only on '.' and Ctrl+Space.")] public bool CompletionOnTyping = true;
    [Tooltip("Shows the parameter list while typing a call. Ctrl+Shift+Space opens it either way.")] public bool ParameterHints = true;
    [Tooltip("Which keys accept the selected suggestion.")] public CompletionAcceptKeys AcceptCompletionWith = CompletionAcceptKeys.TabAndEnter;
    [Tooltip("Typing one of these accepts the selected suggestion first. Leave empty to only accept with the keys above.")]
    public string CompletionCommitCharacters = ".,;()[]{}<>=";
    [Tooltip("Puts suggestions you accepted recently first.")] public bool SortCompletionsByRecentUse;
    [Range(0, 3), Tooltip("Seconds the mouse rests before hover info shows. Zero turns it off.")] public float HoverDelay = 0.5f;
    [Range(0.05f, 2), Tooltip("Seconds without edits before diagnostics and colours refresh.")] public float AnalysisDelay = 0.35f;

    /// <summary>The colour scheme in use. Picked and edited above the other settings.</summary>
    [HideInInspector] public string ColorScheme = "Theme";

    [Header("Saving")]
    [Tooltip("Formats the whole file when you save it.")] public bool FormatOnSave;
    [Tooltip("Removes unused using directives and sorts the rest when you save.")] public bool OrganizeUsingsOnSave;
    [Tooltip("Removes spaces and tabs at the end of lines when you save.")] public bool TrimTrailingWhitespaceOnSave;
    [Tooltip("Makes sure the file ends with a line break.")] public bool InsertFinalNewline;
    public LineEndingMode LineEndings = LineEndingMode.Keep;
    public FileEncodingMode Encoding = FileEncodingMode.Keep;
    [Tooltip("Auto save only writes the file. Formatting and the other save steps run when you save yourself.")]
    public ScriptAutoSave AutoSave = ScriptAutoSave.Off;
    [Range(0.2f, 10), Tooltip("Seconds after the last edit before an auto save.")] public float AutoSaveDelay = 1f;

    // Find options, remembered from the last find bar. Not shown in Preferences.
    [HideInInspector] public bool FindMatchCase;
    [HideInInspector] public bool FindWholeWord;
    [HideInInspector] public bool FindRegex;

    public override void Apply() => ScriptEditorWindow.ApplyPreferences();

    private static readonly string[] s_schemeFilters = ["*" + CodeColorSchemes.FileExtension];

    public override void OnGUI(Paper paper, float width)
    {
        PreferencesPanel.FontRow(paper, "se_font", "Font",
            [("", "Editor Mono Font"), (EditorApplication.BundledMonoFontName, EditorApplication.BundledMonoFontName)],
            Font, v =>
            {
                Font = v;
                Preferences.Changed(Preferences.SectionOf(this));
            }, compact: false);

        string[] names = [.. CodeColorSchemes.All.Select(s => s.Name)];
        int index = Math.Max(0, Array.IndexOf(names, ColorScheme));
        CodeColorScheme scheme = CodeColorSchemes.Find(ColorScheme);
        bool builtIn = CodeColorSchemes.IsBuiltIn(scheme.Name);

        EditorGUI.SettingsRow(paper, "se_scheme", "Colour Scheme", () =>
        {
            using (paper.Row("se_scheme_row").Height(UnitValue.Auto).Gap(4).Enter())
            {
                Origami.Dropdown(paper, "se_scheme_v", index, i => SetScheme(names[i]), names).Show();
                Origami.IconButton(paper, "se_scheme_dup", EditorIcons.Copy_I, () =>
                        PreferencesPanel.PromptName("Duplicate Colour Scheme", scheme.Name + " Copy", name =>
                        {
                            CodeColorSchemes.Save(scheme.Clone(name));
                            SetScheme(name);
                        }))
                    .Tooltip("Duplicate").Show();
                if (!builtIn)
                    Origami.IconButton(paper, "se_scheme_del", EditorIcons.Trash_I, () =>
                            Origami.Confirm("Delete Colour Scheme", $"Delete \"{scheme.Name}\"?", () =>
                            {
                                CodeColorSchemes.Delete(scheme.Name);
                                SetScheme("Theme");
                            }))
                        .Tooltip("Delete").Show();
                Origami.IconButton(paper, "se_scheme_import", EditorIcons.FileImport_I, () =>
                        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
                        {
                            if (path == null) return;
                            try { SetScheme(CodeColorSchemes.Import(path).Name); }
                            catch (Exception ex) { Toasts.Error("Import failed", ex.Message); }
                        }, filters: s_schemeFilters, filterLabels: ["Colour schemes"]))
                    .Tooltip("Import").Show();
                Origami.IconButton(paper, "se_scheme_export", EditorIcons.FileExport_I, () =>
                        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
                        {
                            if (path == null) return;
                            if (!path.EndsWith(CodeColorSchemes.FileExtension, StringComparison.OrdinalIgnoreCase)) path += CodeColorSchemes.FileExtension;
                            try { CodeColorSchemes.Export(scheme, path); }
                            catch (Exception ex) { Toasts.Error("Export failed", ex.Message); }
                        }, filters: s_schemeFilters, filterLabels: ["Colour schemes"]))
                    .Tooltip("Export").Show();
            }
        });

        if (builtIn)
            EditorGUI.Note(paper, "se_scheme_note", "Built-in schemes are read only. Duplicate one to change its colours.");
        else
            Origami.Foldout(paper, "se_scheme_edit", "Edit Colours").Body(() => DrawSchemeEditor(paper, scheme));
    }

    private void SetScheme(string name)
    {
        ColorScheme = name;
        Preferences.Changed(Preferences.SectionOf(this));
    }

    // Every token kind with its colour and font style, then the editor's own colours.
    private static void DrawSchemeEditor(Paper paper, CodeColorScheme scheme)
    {
        CodeEditorColors current = scheme.Build(EditorTheme.OrigamiTheme);

        foreach (CodeTokenKind kind in Enum.GetValues<CodeTokenKind>())
        {
            string key = kind.ToString();
            CodeTokenStyle style = current.Styles[(int)kind];
            EditorGUI.Row(paper, $"se_tok_{key}", PropertyGridUtils.NicifyName(key), () =>
            {
                using (paper.Row($"se_tok_{key}_row").Height(UnitValue.Auto).Gap(4).Enter())
                {
                    SchemeColorField(paper, $"se_tok_{key}_c", scheme, key, current[kind]);
                    StyleChip(paper, $"se_tok_{key}_b", "B", scheme, key, style, CodeTokenStyle.Bold);
                    StyleChip(paper, $"se_tok_{key}_i", "I", scheme, key, style, CodeTokenStyle.Italic);
                    StyleChip(paper, $"se_tok_{key}_u", "U", scheme, key, style, CodeTokenStyle.Underline);
                }
            });
        }

        EditorGUI.SectionHeader(paper, "se_scheme_ui", "Editor");
        foreach (FieldInfo field in CodeColorSchemes.UiFields)
            EditorGUI.Row(paper, $"se_ui_{field.Name}", PropertyGridUtils.NicifyName(field.Name), () =>
                SchemeColorField(paper, $"se_ui_{field.Name}_c", scheme, field.Name, (Color32)field.GetValue(current)!));
    }

    private static void SchemeColorField(Paper paper, string id, CodeColorScheme scheme, string key, Color32 value)
    {
        var color = new Prowl.Vector.Color(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
        Origami.ColorField(paper, id, color, v =>
        {
            scheme.Colors[key] = CodeColorSchemes.ToHex(new Color32(ToByte(v.R), ToByte(v.G), ToByte(v.B), ToByte(v.A)));
            CodeColorSchemes.Save(scheme);
        }).Alpha(true).Width(130f).Show();
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static void StyleChip(Paper paper, string id, string label, CodeColorScheme scheme, string key, CodeTokenStyle style, CodeTokenStyle flag)
        => EditorGUI.Chip(paper, id, label, (style & flag) != 0, () =>
        {
            scheme.Styles[key] = style ^ flag;
            CodeColorSchemes.Save(scheme);
        }, height: 22f);

    /// <summary>Copies these values onto a code editor, its options and its find state.</summary>
    public void ApplyTo(OrigamiUI.CodeEditor editor)
    {
        ApplyTo(editor.Options);
        editor.FindCaseSensitive = FindMatchCase;
        editor.FindWholeWord = FindWholeWord;
        editor.FindRegex = FindRegex;
    }

    /// <summary>Sets the code font and its bold and italic faces, following the editor's mono font when none is picked.</summary>
    public void ApplyFonts(CodeEditorOptions options)
    {
        if (Font.Length > 0 && EditorApplication.Instance is { } app)
        {
            EditorApplication.MonoFontFaces faces = app.MonoFaces(Font);
            options.Font = faces.Regular;
            options.BoldFont = faces.Bold;
            options.ItalicFont = faces.Italic;
            options.BoldItalicFont = faces.BoldItalic;
        }
        else
        {
            options.Font = null;
            options.BoldFont = EditorTheme.FontMonoBold;
            options.ItalicFont = EditorTheme.FontMonoItalic;
            options.BoldItalicFont = EditorTheme.FontMonoBoldItalic;
        }
    }

    /// <summary>Copies these values onto a code editor's options.</summary>
    public void ApplyTo(CodeEditorOptions options)
    {
        ApplyFonts(options);
        // The editor's zoom is measured from here. ScriptEditorWindow zooms it to FontSize.
        options.FontSize = DefaultFontSize;
        options.LineHeight = LineHeight;
        options.TabSize = TabSize;
        options.InsertSpaces = InsertSpaces;
        options.Ligatures = Ligatures;
        options.TextQuality = TextQuality;
        options.Rulers = [.. Rulers];
        options.ShowLineNumbers = ShowLineNumbers;
        options.ShowFolding = ShowFolding;
        options.ShowMinimap = ShowMinimap;
        options.MinimapWidth = MinimapWidth;
        options.ShowIndentGuides = ShowIndentGuides;
        options.ShowWhitespace = ShowWhitespace;
        options.ShowStatusBar = ShowStatusBar;
        options.HighlightCurrentLine = HighlightCurrentLine;
        options.HighlightMatchingBrackets = HighlightMatchingBrackets;
        options.HighlightOccurrences = HighlightOccurrences;
        options.ShowCodeLens = ShowCodeLens;
        options.SmoothScrolling = SmoothScrolling;
        options.ScrollBeyondLastLine = ScrollBeyondLastLine;
        options.ScrollMargin = ScrollMargin;
        options.CaretBlink = CaretBlink;
        options.AutoClosePairs = AutoClosePairs;
        options.AutoIndent = AutoIndent;
        options.FormatOnType = FormatOnType;
        options.CompletionOnTyping = CompletionOnTyping;
        options.HoverDelay = HoverDelay;
        options.AnalysisDelay = AnalysisDelay;
        options.ShowTrailingWhitespace = ShowTrailingWhitespace;
        options.BracketPairColors = BracketPairColors;
        options.RainbowIndentGuides = RainbowIndentGuides;
        options.CaretStyle = CaretStyle;
        options.CaretWidth = CaretWidth;
        options.MultiCaretModifier = MultiCaretModifier;
        options.DragAndDropText = DragAndDropText;
        options.ParameterHints = ParameterHints;
        options.AcceptCompletionWith = AcceptCompletionWith;
        options.CompletionCommitCharacters = CompletionCommitCharacters;
        options.SortCompletionsByRecentUse = SortCompletionsByRecentUse;
        // No debugger yet, so no breakpoint margin.
        options.ShowGlyphMargin = false;
    }
}

/// <summary>
/// The dock window scripts and shaders are edited in. Every open file is a tab of the one window. Scripts get
/// completion, diagnostics and navigation from their own compilation unit, shaders from the shared shader workspace.
/// </summary>
public class ScriptEditorWindow : DockPanel
{
    private sealed class ScriptTab
    {
        public required string Path;
        public required OrigamiUI.CodeEditor Editor;
        public Guid Guid;
        public int Id;
        public bool OnFallback;
        public bool HadBom;
        public DateTime DiskTime;
        public bool IsShader;
        public bool Asking;
        public bool Gone;
        /// <summary>A library file such as Graphite/Core.glsl, or a source file outside the project's Assets.</summary>
        public bool ReadOnly;
        /// <summary>A library file, named by its include name rather than a path on disk.</summary>
        public bool IsLibrary => !System.IO.Path.IsPathRooted(Path);
        public string Name => System.IO.Path.GetFileName(Path);
    }

    private static ScriptEditorWindow? s_window;
    private static int s_nextTabId;

    private readonly List<ScriptTab> _tabs = [];
    private int _active;
    private double _nextDiskCheck;

    private ScriptTab? Active => _active >= 0 && _active < _tabs.Count ? _tabs[_active] : null;

    public override string Title => "Scripts";
    public override string Icon => EditorIcons.Code;

    static ScriptEditorWindow()
    {
        // Auto save leaves scripts alone, since every write recompiles.
        SaveManager.OnSave += () =>
        {
            if (s_window == null || SaveManager.IsAutoSave) return null;
            var saved = new List<string>();
            foreach (var tab in s_window._tabs)
                if (tab.Editor.IsDirty && Save(tab)) saved.Add(tab.Name);
            return saved.Count == 0 ? null : Loc.Get("save.script", new { name = string.Join(", ", saved) });
        };
        SaveManager.CollectUnsaved += unsaved =>
        {
            if (s_window == null) return;
            foreach (var tab in s_window._tabs)
                if (tab.Editor.IsDirty) unsaved.Add(Loc.Get("save.script", new { name = tab.Name }));
        };
    }

    public ScriptEditorWindow()
    {
        s_window = this;
        ScriptWorkspaces.Refreshed += OnWorkspacesRefreshed;
    }

    [AssetDoubleClickHandler(".cs", ".shader", ".glsl", ".compute")]
    private static bool OnDoubleClick(string relativePath, Guid guid)
    {
        if (Project.Current is not { } project) return false;
        string path = Path.Combine(project.AssetsPath, relativePath);
        if (Preferences.Get<ScriptEditorPreferences>().OpenScriptsIn != ScriptEditorMode.BuiltIn)
            return ExternalTools.OpenScript(path);
        OpenFor(path);
        return true;
    }

    /// <summary>
    /// Opens a script as a tab of the script window, or switches to its tab, optionally at a 1 based line and column.
    /// A shader library name such as Graphite/Core.glsl, or a file outside the project's Assets, opens read only.
    /// </summary>
    public static void OpenFor(string path, int line = 0, int column = 1)
    {
        if (Path.IsPathRooted(path)) path = Path.GetFullPath(path);
        var window = s_window;
        bool created = window == null;
        window ??= new ScriptEditorWindow();

        int index = window._tabs.FindIndex(t => SamePath(t.Path, path));
        if (index < 0)
        {
            var tab = window.Load(path);
            if (tab == null)
            {
                if (created) s_window = null;
                return;
            }
            window._tabs.Add(tab);
            index = window._tabs.Count - 1;
        }
        window._active = index;

        if (created) EditorApplication.Instance?.OpenPanelInstance(window, 960, 720);
        else EditorApplication.Instance?.FocusPanelInstance(window);

        var editor = window._tabs[index].Editor;
        editor.Focus();
        if (line > 0) editor.GoToLine(line, column);
    }

    // Scripts zoom from the default font size, so the status bar's percentage is the Font Size preference against it.
    private static void SetFontSize(OrigamiUI.CodeEditor editor, float size)
    {
        size = Math.Clamp(size, editor.Options.MinFontSize, editor.Options.MaxFontSize);
        if (MathF.Abs(editor.FontSize - size) < 0.05f) return;
        editor.ResetZoom();
        editor.Zoom(size - editor.Options.FontSize);
    }

    // Ctrl+wheel zoom and the find bar's toggles carry over to every script and the next session.
    private static void RememberEditorState(OrigamiUI.CodeEditor editor)
    {
        ScriptEditorPreferences prefs = Preferences.Get<ScriptEditorPreferences>();
        float zoom = MathF.Round(editor.FontSize, 1);
        if (zoom == prefs.FontSize && editor.FindCaseSensitive == prefs.FindMatchCase
            && editor.FindWholeWord == prefs.FindWholeWord && editor.FindRegex == prefs.FindRegex)
            return;

        prefs.FontSize = zoom;
        prefs.FindMatchCase = editor.FindCaseSensitive;
        prefs.FindWholeWord = editor.FindWholeWord;
        prefs.FindRegex = editor.FindRegex;
        Preferences.Changed(Preferences.SectionOf(prefs));
    }

    // Undo, redo and save stay on the code editor's own keys, which already match the global ones.
    private static readonly CodeEditorCommand[] s_notRebindable = [CodeEditorCommand.Undo, CodeEditorCommand.Redo, CodeEditorCommand.Save];
    private const string ShortcutCategory = "CodeEditor/";

    private static CodeEditorKeymap? s_keymap;
    private static int s_keymapVersion = -1;

    [InitializeOnLoad]
    private static void RegisterShortcuts()
    {
        foreach (CodeEditorCommand command in Enum.GetValues<CodeEditorCommand>())
        {
            if (s_notRebindable.Contains(command)) continue;
            CodeKeyBinding? binding = CodeEditorKeymap.Default.PrimaryBinding(command);
            KeyCode key = binding is { } b && Runtime.GUI.PaperInputBridge.TryGetKeyCode(b.Key, out KeyCode k) ? k : KeyCode.Unknown;
            ShortcutManager.Register(ShortcutCategory + command, PropertyGridUtils.NicifyName(command.ToString()), key,
                binding?.Ctrl ?? false, binding?.Shift ?? false, binding?.Alt ?? false);
        }
    }

    /// <summary>The code editor keymap with the Shortcuts tab's rebinds applied.</summary>
    private static CodeEditorKeymap Keymap()
    {
        if (s_keymap != null && s_keymapVersion == ShortcutManager.Version) return s_keymap;
        s_keymapVersion = ShortcutManager.Version;

        var keymap = CodeEditorKeymap.Default.Clone();
        foreach (CodeEditorCommand command in Enum.GetValues<CodeEditorCommand>())
        {
            string id = ShortcutCategory + command;
            if (ShortcutManager.GetBinding(id) is not { } binding || ShortcutManager.GetDefault(id) is not { } fallback || binding.Equals(fallback)) continue;
            keymap.Unbind(command);
            if (binding.Key != KeyCode.Unknown && Runtime.GUI.PaperInputBridge.TryGetPaperKey(binding.Key, out PaperKey key))
                keymap.Bind(command, new CodeKeyBinding(key, binding.Ctrl, binding.Shift, binding.Alt));
        }
        return s_keymap = keymap;
    }

    private static float s_appliedFontSize = -1f;

    internal static void ApplyPreferences()
    {
        if (s_window == null) return;
        ScriptEditorPreferences prefs = Preferences.Get<ScriptEditorPreferences>();
        // Only a new font size is pushed, so a Ctrl+wheel not recorded yet survives other preference changes.
        bool resize = prefs.FontSize != s_appliedFontSize;
        s_appliedFontSize = prefs.FontSize;
        foreach (var tab in s_window._tabs)
        {
            ApplyOptions(tab.Editor, tab.ReadOnly);
            if (resize) SetFontSize(tab.Editor, prefs.FontSize);
        }
    }

    // Code shown outside the project it builds in would light up with errors that are not real, so it gets no diagnostics or reference counts.
    private static void ApplyOptions(OrigamiUI.CodeEditor editor, bool readOnly)
    {
        Preferences.Get<ScriptEditorPreferences>().ApplyTo(editor);
        if (!readOnly) return;
        editor.Options.ReadOnly = true;
        editor.Options.ShowDiagnostics = false;
        editor.Options.ShowCodeLens = false;
    }

    private static bool InAssets(string path)
        => Project.Current is { } project && Path.GetFullPath(path).StartsWith(Path.GetFullPath(project.AssetsPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private ScriptTab? Load(string path)
    {
        string text;
        bool bom = false;
        DateTime time = default;
        bool library = !Path.IsPathRooted(path);
        if (library)
        {
            if (ShaderWorkspaces.Shared.Resolve(path, null) is not { } source)
            {
                Toasts.Info("No source", $"{path} is not a shader library file this editor knows.");
                return null;
            }
            text = source.Text;
        }
        else
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                bom = HasBom(bytes);
                text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                time = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError($"[ScriptEditor] Could not open '{path}': {e.Message}");
                return null;
            }
        }
        bool readOnly = library || !InAssets(path);

        bool onFallback = false;
        var shaderKind = ShaderLanguage.KindFor(Path.GetExtension(path));
        CodeLanguage language = shaderKind is { } kind
            ? new ShaderLanguage(ShaderWorkspaces.Shared, kind, path)
            : new RoslynCSharpLanguage(ScriptWorkspaces.Get(path, out onFallback), path);
        var editor = new OrigamiUI.CodeEditor(text, language) { FilePath = path };
        ApplyOptions(editor, readOnly);
        SetFontSize(editor, Preferences.Get<ScriptEditorPreferences>().FontSize);

        var backend = EditorAssetBackend.Instance;
        var tab = new ScriptTab
        {
            Path = path, Editor = editor, Id = ++s_nextTabId,
            Guid = !readOnly && backend != null ? backend.PathToGuid(backend.ToRelativePath(path)) : Guid.Empty,
            OnFallback = onFallback && !readOnly, IsShader = shaderKind != null, HadBom = bom, DiskTime = time, ReadOnly = readOnly,
        };
        editor.SaveRequested += () => SaveManager.RequestSave();
        editor.NavigateRequested += Navigate;
        editor.ExternalEditsRequested += ApplyExternalEdits;
        return tab;
    }

    private static bool HasBom(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    // Scripts opened before their unit compiled start on the fallback workspace and move once a compile has placed them.
    private void OnWorkspacesRefreshed()
    {
        foreach (var tab in _tabs)
        {
            if (!tab.OnFallback) continue;
            var workspace = ScriptWorkspaces.Get(tab.Path, out tab.OnFallback);
            if (!tab.OnFallback) tab.Editor.Language = new RoslynCSharpLanguage(workspace, tab.Path);
        }
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        CheckDisk(paper);
        TickAutoSave();
        var m = Origami.Current.Metrics;
        const float tabsH = 32, toolbarH = 30;

        using (paper.Column("se_root").Width(width).Height(height).Enter())
        {
            if (_tabs.Count == 0)
            {
                EditorGUI.EmptyState(paper, "se_empty", "Double click a script or shader to open it here", EditorTheme.DefaultFont!);
                return;
            }
            _active = Math.Clamp(_active, 0, _tabs.Count - 1);
            var active = _tabs[_active];

            var tabs = Origami.Tabs(paper, "se_tabs", _active, i => { _active = i; _tabs[i].Editor.Focus(); })
                .Width(UnitValue.Stretch()).Height(tabsH)
                .Closeable(i => CloseTab(i));
            foreach (var tab in _tabs) tabs.Tab(tab.ReadOnly ? tab.Name + " (read only)" : tab.Editor.IsDirty ? tab.Name + " *" : tab.Name);
            tabs.Show();

            using (paper.Row("se_bar").Width(UnitValue.Stretch()).Height(toolbarH)
                .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.SpacingMedium)
                .BackgroundColor(EditorTheme.Neutral200)
                .AlignItems(LayoutAlignment.Center).Enter())
            {
                string shown = !active.ReadOnly && Project.Current is { } project ? Path.GetRelativePath(project.AssetsPath, active.Path) : active.Path;
                paper.Box("se_path").Width(UnitValue.Stretch()).Height(toolbarH).IsNotInteractable()
                    .Text(shown, EditorTheme.FontMono ?? Origami.Current.Font!).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                if (!active.IsLibrary) EditorGUI.PillButton(paper, "se_ext", "Open Externally", false, () => OpenExternally(active));
                if (active.ReadOnly) EditorGUI.PillButton(paper, "se_ro", "Read Only", false, () => { });
                else EditorGUI.PillButton(paper, "se_save", active.Editor.IsDirty ? "Save" : "Saved", active.Editor.IsDirty, () => Save(active));
            }

            // Keyed per tab so each script keeps its own scroll, caret and popups while tabs switch and files get renamed.
            active.Editor.Colors = CodeColorSchemes.Palette(Preferences.Get<ScriptEditorPreferences>().ColorScheme, EditorTheme.OrigamiTheme);
            active.Editor.Options.Keymap = Keymap();
            // The editor's mono font can change from the Theme tab at any time.
            ScriptEditorPreferences prefs = Preferences.Get<ScriptEditorPreferences>();
            if (prefs.Font.Length == 0) prefs.ApplyFonts(active.Editor.Options);
            Origami.CodeEditor(paper, "se_code_" + active.Id, active.Editor)
                .Size(width, Math.Max(0, height - tabsH - toolbarH)).Show();
            RememberEditorState(active.Editor);
        }
    }

    private bool _wasFocused = true;

    private void TickAutoSave()
    {
        ScriptEditorPreferences prefs = Preferences.Get<ScriptEditorPreferences>();
        bool focused = Runtime.Window.IsFocused;
        bool focusLost = _wasFocused && !focused;
        _wasFocused = focused;

        foreach (var tab in _tabs)
        {
            if (!tab.Editor.IsDirty) continue;
            bool due = prefs.AutoSave switch
            {
                ScriptAutoSave.AfterDelay => tab.Editor.TimeSinceLastEdit >= prefs.AutoSaveDelay,
                ScriptAutoSave.OnFocusLoss => focusLost,
                _ => false,
            };
            if (due) Save(tab, automatic: true);
        }
    }

    private void CloseTab(int index, bool save = true)
    {
        if (index < 0 || index >= _tabs.Count) return;
        var tab = _tabs[index];
        if (save && tab.Editor.IsDirty) Save(tab);
        tab.Editor.Dispose();
        _tabs.RemoveAt(index);
        if (_active > index || _active >= _tabs.Count) _active = Math.Max(0, _active - 1);
    }

    // Edits made outside, such as in an external IDE, ask whether to reload the file or close its tab.
    private void CheckDisk(Paper paper)
    {
        if (paper.Time < _nextDiskCheck) return;
        _nextDiskCheck = paper.Time + 1.0;

        foreach (var tab in _tabs.ToArray())
        {
            if (tab.Asking || tab.IsLibrary) continue;
            if (tab.ReadOnly)
            {
                // Nothing here to lose, so a read only file just follows its changes on disk.
                if (File.Exists(tab.Path) && File.GetLastWriteTimeUtc(tab.Path) != tab.DiskTime) Reload(tab);
                continue;
            }
            if (!File.Exists(tab.Path))
            {
                if (!tab.Gone) CheckMissing(tab);
                continue;
            }
            tab.Gone = false;

            DateTime time;
            try { time = File.GetLastWriteTimeUtc(tab.Path); }
            catch (IOException) { continue; }
            if (time == tab.DiskTime) continue;

            tab.DiskTime = time;
            if (tab.IsShader) ShaderWorkspaces.Invalidate();
            AskReload(tab, $"{tab.Name} was changed outside the Script Editor.");
        }
    }

    // A file gone from its path was renamed or deleted. The asset database is the judge, by GUID, once it has caught up.
    private void CheckMissing(ScriptTab tab)
    {
        var backend = EditorAssetBackend.Instance;
        string? relative = tab.Guid != Guid.Empty ? backend?.GuidToPath(tab.Guid) : null;
        if (relative != null && Project.Current is { } project)
        {
            string path = Path.GetFullPath(Path.Combine(project.AssetsPath, relative));
            if (SamePath(path, tab.Path)) return;
            string oldName = tab.Name;
            Retarget(tab, path);
            AskReload(tab, $"{oldName} was renamed to {tab.Name}.");
            return;
        }

        tab.Asking = true;
        var dialog = new DialogModal { Title = "Script deleted", Width = 420, OnDismissed = _ => tab.Asking = false };
        dialog.DrawContent = paper => Origami.Label(paper, "se_missing_msg", $"{tab.Name} was deleted outside the Script Editor. Close its tab, or keep it open to save it again?").Show();
        dialog.Button("Close", () => { Modal.Remove(dialog); tab.Asking = false; CloseTab(_tabs.IndexOf(tab), save: false); }, OrigamiVariant.Primary);
        dialog.Button("Keep Open", () => { Modal.Remove(dialog); tab.Asking = false; tab.Gone = true; tab.Guid = Guid.Empty; });
        Modal.Push(dialog);
    }

    private void AskReload(ScriptTab tab, string reason)
    {
        if (tab.Asking) return;
        tab.Asking = true;
        string detail = tab.Editor.IsDirty ? " Reloading discards your unsaved edits here." : "";
        var dialog = new DialogModal { Title = "Script changed", Width = 420, OnDismissed = _ => tab.Asking = false };
        dialog.DrawContent = paper => Origami.Label(paper, "se_reload_msg", $"{reason} Reload it, or close its tab?{detail}").Show();
        dialog.Button("Reload", () => { Modal.Remove(dialog); tab.Asking = false; Reload(tab); }, OrigamiVariant.Primary);
        dialog.Button("Close", () => { Modal.Remove(dialog); tab.Asking = false; CloseTab(_tabs.IndexOf(tab), save: false); });
        Modal.Push(dialog);
    }

    private void Reload(ScriptTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        var caret = tab.Editor.Caret;
        var reloaded = Load(tab.Path);
        if (reloaded == null) return;
        tab.Editor.Dispose();
        reloaded.Id = tab.Id;
        _tabs[index] = reloaded;
        reloaded.Editor.GoTo(reloaded.Editor.Document.Clamp(caret), center: false);
    }

    // Points a tab at the new path of its file, keeping its text, undo history and workspace.
    private static void Retarget(ScriptTab tab, string path)
    {
        string oldPath = tab.Path;
        tab.Path = path;
        tab.Editor.FilePath = path;
        switch (tab.Editor.Language)
        {
            case RoslynCSharpLanguage cs:
                tab.Editor.Language = new RoslynCSharpLanguage(cs.Workspace, path);
                cs.Workspace.RemoveDocument(oldPath);
                break;
            case ShaderLanguage shader:
                tab.Editor.Language = new ShaderLanguage(shader.Workspace, ShaderLanguage.KindFor(Path.GetExtension(path)) ?? shader.Kind, path);
                break;
        }
        try { tab.DiskTime = File.GetLastWriteTimeUtc(path); }
        catch (IOException) { }
    }

    /// <summary>Follows an asset rename or move into the tab of that file, by GUID, and asks whether to reload or close it.</summary>
    internal static void OnAssetMoved(string oldRelative, string newRelative)
    {
        if (s_window is not { } window || Project.Current is not { } project) return;
        Guid guid = EditorAssetBackend.Instance?.PathToGuid(newRelative) ?? Guid.Empty;
        string oldPath = Path.GetFullPath(Path.Combine(project.AssetsPath, oldRelative));
        var tab = window._tabs.Find(t => (guid != Guid.Empty && t.Guid == guid) || SamePath(t.Path, oldPath));
        if (tab == null) return;

        string newPath = Path.GetFullPath(Path.Combine(project.AssetsPath, newRelative));
        if (tab.Path == newPath) return;
        string oldName = tab.Name;
        tab.Guid = guid;
        Retarget(tab, newPath);
        window.AskReload(tab, $"{oldName} was renamed to {tab.Name}.");
    }

    /// <summary>The editor a file is open in, or null.</summary>
    internal static OrigamiUI.CodeEditor? EditorFor(string path)
        => s_window?._tabs.Find(t => SamePath(t.Path, Path.GetFullPath(path)))?.Editor;

    /// <summary>
    /// Applies edits made on the user's behalf: an open file gets them in its tab, saved when it had no unsaved edits,
    /// any other file is rewritten on disk.
    /// </summary>
    internal static void ApplyEditsAndSave(IReadOnlyDictionary<string, IReadOnlyList<CodeTextEdit>> edits)
    {
        foreach (var (path, fileEdits) in edits)
        {
            var open = s_window?._tabs.Find(t => SamePath(t.Path, Path.GetFullPath(path)));
            if (open == null)
            {
                RewriteOnDisk(path, fileEdits);
                continue;
            }
            bool wasClean = !open.Editor.IsDirty;
            open.Editor.ApplyEdits(fileEdits);
            if (wasClean) Save(open);
        }
    }

    private static void RewriteOnDisk(string path, IReadOnlyList<CodeTextEdit> fileEdits)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool bom = HasBom(bytes);
            var text = new StringBuilder(new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)));
            foreach (var e in fileEdits.OrderByDescending(e => e.Start))
                text.Remove(e.Start, e.Length).Insert(e.Start, e.NewText);
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(bom));
            ImportAndRecompile(path);
        }
        catch (Exception e)
        {
            Runtime.Debug.LogError($"[ScriptEditor] Could not update '{path}': {e.Message}");
        }
    }

    // How long a save waits for formatting or organising usings before writing the file without them.
    private const int SaveStepTimeoutMs = 3000;

    // The save steps the user picked, applied as edits so they can be undone, then the file in the chosen line endings and encoding.
    private static bool Save(ScriptTab tab, bool automatic = false)
    {
        if (tab.ReadOnly) return false;
        ScriptEditorPreferences prefs = Preferences.Get<ScriptEditorPreferences>();
        OrigamiUI.CodeEditor editor = tab.Editor;

        if (!automatic)
        {
            if (!tab.IsShader && editor.Session is { } session)
            {
                if (prefs.OrganizeUsingsOnSave) ApplyStep(editor, session.OrganizeUsingsAsync(CancellationToken.None));
                if (prefs.FormatOnSave) ApplyStep(editor, session.FormatAsync(0, -1, CancellationToken.None));
            }
            if (prefs.TrimTrailingWhitespaceOnSave) editor.TrimTrailingWhitespace();
            if (prefs.InsertFinalNewline)
            {
                CodeDocument doc = editor.Document;
                if (doc.Length > 0 && doc[doc.LineCount - 1].Length > 0)
                    editor.ApplyEdits([new CodeTextEdit(doc.Length, 0, "\n")]);
            }
        }

        if (prefs.LineEndings != LineEndingMode.Keep)
            editor.Document.LineEnding = prefs.LineEndings == LineEndingMode.CRLF ? "\r\n" : "\n";
        if (prefs.Encoding != FileEncodingMode.Keep)
            tab.HadBom = prefs.Encoding == FileEncodingMode.Utf8Bom;

        try
        {
            File.WriteAllText(tab.Path, editor.Text, new UTF8Encoding(tab.HadBom));
            tab.DiskTime = File.GetLastWriteTimeUtc(tab.Path);
        }
        catch (Exception e)
        {
            Toasts.Error("Save failed", e.Message);
            return false;
        }

        tab.Editor.Undo.MarkSaved();
        if (tab.IsShader) ShaderWorkspaces.Invalidate();
        ImportAndRecompile(tab.Path);
        return true;
    }

    private static void ApplyStep(OrigamiUI.CodeEditor editor, Task<IReadOnlyList<CodeTextEdit>> step)
    {
        try
        {
            if (step.Wait(SaveStepTimeoutMs) && step.Result.Count > 0) editor.ApplyEdits(step.Result);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[ScriptEditor] A save step failed, saving without it: {ex.Message}");
        }
    }

    private static void ImportAndRecompile(string path)
    {
        if (EditorAssetBackend.Instance is not { } backend) return;
        backend.ImportFile(backend.ToRelativePath(path));
    }

    // At the caret, in the external script editor when one is set, otherwise with the system's app.
    private static void OpenExternally(ScriptTab tab)
    {
        var caret = tab.Editor.Caret;
        if (!ExternalTools.OpenScript(tab.Path, caret.Line + 1, caret.Column + 1))
            ExternalTools.OpenWithSystem(tab.Path);
    }

    // Into scripts, shader library files and, through an assembly's PDB, engine sources on this machine. Nothing is decompiled.
    private static void Navigate(CodeLocation location)
    {
        string? path = location.Path;
        bool exists = path != null && (Path.IsPathRooted(path) ? File.Exists(path) : ShaderWorkspaces.Shared.Resolve(path, null) != null);
        if (exists)
        {
            OpenFor(path!, location.Line + 1, location.Column + 1);
            return;
        }

        string reason = location.Assembly != null
            ? $"It is compiled into {location.Assembly}, and no source file for it was found on this machine."
            : path != null ? $"{path} could not be found." : "Its source is not available.";
        Toasts.Info($"No source for {location.Display}", reason);
    }

    // Rename and some code actions touch other scripts. Open ones get the edits in their tab, closed ones are rewritten on disk.
    private void ApplyExternalEdits(IReadOnlyDictionary<string, IReadOnlyList<CodeTextEdit>> edits)
    {
        foreach (var (path, fileEdits) in edits)
        {
            var open = _tabs.Find(t => SamePath(t.Path, path));
            if (open is { ReadOnly: true }) continue;
            if (open != null) open.Editor.ApplyEdits(fileEdits);
            else if (Path.IsPathRooted(path) && InAssets(path)) RewriteOnDisk(path, fileEdits);
        }
    }

    public override bool SerializeState(JsonObject state)
    {
        if (_tabs.Count == 0 || Project.Current is not { } project) return false;
        var scripts = new JsonArray();
        foreach (var tab in _tabs)
            if (!tab.ReadOnly) scripts.Add(Path.GetRelativePath(project.AssetsPath, tab.Path));
        state["scripts"] = scripts;
        state["active"] = _active;
        return true;
    }

    public override void RestoreState(JsonObject state)
    {
        if (Project.Current is not { } project || state["scripts"] is not JsonArray scripts) return;
        foreach (var node in scripts)
        {
            if (node?.GetValue<string>() is not { } relative) continue;
            string path = Path.GetFullPath(Path.Combine(project.AssetsPath, relative));
            if (File.Exists(path) && Load(path) is { } tab) _tabs.Add(tab);
        }
        _active = state["active"]?.GetValue<int>() ?? 0;
    }

    public override void OnClosed()
    {
        if (s_window == this) s_window = null;
        ScriptWorkspaces.Refreshed -= OnWorkspacesRefreshed;
        foreach (var tab in _tabs)
        {
            if (tab.Editor.IsDirty) Save(tab);
            tab.Editor.Dispose();
        }
        _tabs.Clear();
    }
}
