using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;
using Prowl.OrigamiUI.Graphite;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.PaperUI.LayoutEngine;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.ScriptEditor;

/// <summary>Whether a double clicked script opens in the editor's own code editor or the system's default app.</summary>
public enum ScriptEditorMode
{
    BuiltIn,
    External,
}

/// <summary>How the built in code editor looks and behaves. Changes apply to open scripts straight away.</summary>
[EditorPreferences("Script Editor", EditorIcons.Code, order: 10, ProjectOverridable = true)]
public sealed class ScriptEditorPreferences : EditorPreferences
{
    [Tooltip("Open double clicked scripts in the built in code editor, or in the system's default app.")]
    public ScriptEditorMode OpenScriptsIn = ScriptEditorMode.BuiltIn;

    [Header("Text")]
    [Range(6, 48)] public float FontSize = 15f;
    [Range(1, 2.5f), Tooltip("Row height as a multiple of the font size.")] public float LineHeight = 1.5f;
    [Range(1, 8)] public int TabSize = 4;
    [Tooltip("Tab and auto indent insert spaces instead of tab characters.")] public bool InsertSpaces = true;
    [Tooltip("Columns with a vertical guide line.")] public List<int> Rulers = [120];

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

    [Header("Scrolling and Caret")]
    public bool SmoothScrolling = true;
    public bool ScrollBeyondLastLine = true;
    [Range(0, 20), Tooltip("Lines kept visible above and below the caret.")] public int ScrollMargin = 3;
    public bool CaretBlink = true;

    [Header("Editing")]
    public bool AutoClosePairs = true;
    public bool AutoIndent = true;
    [Tooltip("Formats the statement or block a typed ';' or '}' closes.")] public bool FormatOnType = true;

    [Header("Completion and Analysis")]
    [Tooltip("Opens completion as soon as a word is typed, not only on '.' and Ctrl+Space.")] public bool CompletionOnTyping = true;
    [Range(0, 3), Tooltip("Seconds the mouse rests before hover info shows. Zero turns it off.")] public float HoverDelay = 0.5f;
    [Range(0.05f, 2), Tooltip("Seconds without edits before diagnostics and colours refresh.")] public float AnalysisDelay = 0.35f;

    public override void Apply() => ScriptEditorWindow.ApplyPreferences();

    /// <summary>Copies these values onto a code editor's options.</summary>
    public void ApplyTo(CodeEditorOptions options)
    {
        options.FontSize = FontSize;
        options.LineHeight = LineHeight;
        options.TabSize = TabSize;
        options.InsertSpaces = InsertSpaces;
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
        if (Preferences.Get<ScriptEditorPreferences>().OpenScriptsIn != ScriptEditorMode.BuiltIn || Project.Current is not { } project) return false;
        OpenFor(Path.Combine(project.AssetsPath, relativePath));
        return true;
    }

    /// <summary>Opens a script as a tab of the script window, or switches to its tab, optionally at a 1 based line and column.</summary>
    public static void OpenFor(string path, int line = 0, int column = 1)
    {
        path = Path.GetFullPath(path);
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

    internal static void ApplyPreferences()
    {
        if (s_window == null) return;
        ScriptEditorPreferences prefs = Preferences.Get<ScriptEditorPreferences>();
        foreach (var tab in s_window._tabs)
        {
            float oldSize = tab.Editor.Options.FontSize;
            prefs.ApplyTo(tab.Editor.Options);
            // A new font size replaces any Ctrl+wheel zoom.
            if (oldSize != prefs.FontSize) tab.Editor.ResetZoom();
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private ScriptTab? Load(string path)
    {
        string text;
        bool bom;
        DateTime time;
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

        bool onFallback = false;
        var shaderKind = ShaderLanguage.KindFor(Path.GetExtension(path));
        CodeLanguage language = shaderKind is { } kind
            ? new ShaderLanguage(ShaderWorkspaces.Shared, kind, path)
            : new RoslynCSharpLanguage(ScriptWorkspaces.Get(path, out onFallback), path);
        var editor = new OrigamiUI.CodeEditor(text, language) { FilePath = path };
        Preferences.Get<ScriptEditorPreferences>().ApplyTo(editor.Options);

        var backend = EditorAssetBackend.Instance;
        var tab = new ScriptTab
        {
            Path = path, Editor = editor, Guid = backend?.PathToGuid(backend.ToRelativePath(path)) ?? Guid.Empty, Id = ++s_nextTabId,
            OnFallback = onFallback, IsShader = shaderKind != null, HadBom = bom, DiskTime = time,
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
            foreach (var tab in _tabs) tabs.Tab(tab.Editor.IsDirty ? tab.Name + " *" : tab.Name);
            tabs.Show();

            using (paper.Row("se_bar").Width(UnitValue.Stretch()).Height(toolbarH)
                .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.SpacingMedium)
                .BackgroundColor(EditorTheme.Neutral200)
                .AlignItems(LayoutAlignment.Center).Enter())
            {
                string shown = Project.Current is { } project ? Path.GetRelativePath(project.AssetsPath, active.Path) : active.Path;
                paper.Box("se_path").Width(UnitValue.Stretch()).Height(toolbarH).IsNotInteractable()
                    .Text(shown, EditorTheme.FontMono ?? Origami.Current.Font!).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

                EditorGUI.PillButton(paper, "se_ext", "Open Externally", false, () => OpenExternally(active.Path));
                EditorGUI.PillButton(paper, "se_save", active.Editor.IsDirty ? "Save" : "Saved", active.Editor.IsDirty, () => Save(active));
            }

            // Keyed per tab so each script keeps its own scroll, caret and popups while tabs switch and files get renamed.
            Origami.CodeEditor(paper, "se_code_" + active.Id, active.Editor)
                .Size(width, Math.Max(0, height - tabsH - toolbarH)).Show();
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
            if (tab.Asking) continue;
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

    private static bool Save(ScriptTab tab)
    {
        try
        {
            File.WriteAllText(tab.Path, tab.Editor.Text, new UTF8Encoding(tab.HadBom));
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

    private static void ImportAndRecompile(string path)
    {
        if (EditorAssetBackend.Instance is not { } backend) return;
        backend.ImportFile(backend.ToRelativePath(path));
    }

    private static void OpenExternally(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Toasts.Error("Could not open", e.Message); }
    }

    private static void Navigate(CodeLocation location)
    {
        if (location.Path != null && File.Exists(location.Path))
        {
            OpenFor(location.Path, location.Line + 1, location.Column + 1);
            return;
        }
        Toasts.Info(location.Path != null ? "Built in file" : "Defined in an assembly", location.Display);
    }

    // Rename and some code actions touch other scripts. Open ones get the edits in their tab, closed ones are rewritten on disk.
    private void ApplyExternalEdits(IReadOnlyDictionary<string, IReadOnlyList<CodeTextEdit>> edits)
    {
        foreach (var (path, fileEdits) in edits)
        {
            var open = _tabs.Find(t => SamePath(t.Path, path));
            if (open != null) open.Editor.ApplyEdits(fileEdits);
            else RewriteOnDisk(path, fileEdits);
        }
    }

    public override bool SerializeState(JsonObject state)
    {
        if (_tabs.Count == 0 || Project.Current is not { } project) return false;
        var scripts = new JsonArray();
        foreach (var tab in _tabs) scripts.Add(Path.GetRelativePath(project.AssetsPath, tab.Path));
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
