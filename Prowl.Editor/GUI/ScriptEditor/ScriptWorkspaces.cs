using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.OrigamiUI.CSharp;

namespace Prowl.Editor.GUI.ScriptEditor;

/// <summary>
/// One Roslyn workspace per script compilation unit, built from what the last editor compile used, so the
/// code editor sees the same references, defines and sibling scripts as the real build. Refreshed after
/// every compile. A script that has not been compiled yet lands in a fallback workspace on the editor's
/// own assemblies until the next compile places it.
/// </summary>
internal static class ScriptWorkspaces
{
    private sealed class Entry
    {
        public required CSharpWorkspace Workspace;
        public HashSet<string> Scripts = new(StringComparer.OrdinalIgnoreCase);
    }

    private const string FallbackKey = "";

    private static readonly Dictionary<string, Entry> s_entries = new(StringComparer.OrdinalIgnoreCase);
    private static bool s_hooked;

    /// <summary>Raised after a compile refreshed the workspaces, so editors on the fallback can move to their real unit.</summary>
    public static event Action? Refreshed;

    /// <summary>The workspace a script belongs to, and whether it is the real unit or the fallback.</summary>
    public static CSharpWorkspace Get(string scriptPath, out bool isFallback)
    {
        Hook();
        if (!ScriptAssemblyManager.IsCompiling && Project.Current is { } project
            && RoslynScriptBackend.TryGetScriptContext(project.RootPath, scriptPath, out var context) && context != null)
        {
            isFallback = false;
            return Sync(context).Workspace;
        }

        isFallback = true;
        if (!s_entries.TryGetValue(FallbackKey, out var fallback))
        {
            fallback = new Entry { Workspace = new CSharpWorkspace(assemblyName: "Scripts") { Analyzers = RoslynScriptBackend.ScriptAnalyzers } };
            fallback.Workspace.SetPreprocessorSymbols(ScriptCompiler.EditorDefines);
            s_entries[FallbackKey] = fallback;
        }
        return fallback.Workspace;
    }

    private static void Hook()
    {
        if (s_hooked) return;
        s_hooked = true;
        ScriptAssemblyManager.CompileFinished += _ => Refresh();
    }

    private static void Refresh()
    {
        if (Project.Current is not { } project) return;

        foreach (var (key, entry) in s_entries.ToArray())
        {
            if (key == FallbackKey) continue;
            string? any = entry.Scripts.FirstOrDefault(File.Exists);
            if (any != null && RoslynScriptBackend.TryGetScriptContext(project.RootPath, any, out var context) && context != null)
                Sync(context);
        }
        Refreshed?.Invoke();
    }

    private static Entry Sync(RoslynScriptBackend.ScriptContext context)
    {
        if (!s_entries.TryGetValue(context.UnitName, out var entry))
        {
            entry = new Entry { Workspace = new CSharpWorkspace(context.References, context.UnitName) { Analyzers = RoslynScriptBackend.ScriptAnalyzers } };
            s_entries[context.UnitName] = entry;
        }
        else entry.Workspace.SetReferences(context.References);

        var ws = entry.Workspace;
        ws.SetCompilationOptions(context.Options);
        ws.SetPreprocessorSymbols(context.ParseOptions.PreprocessorSymbolNames);
        ws.LanguageVersion = context.ParseOptions.LanguageVersion;

        var current = new HashSet<string>(context.Scripts, StringComparer.OrdinalIgnoreCase);
        foreach (string gone in entry.Scripts.Where(s => !current.Contains(s)).ToArray())
            ws.RemoveDocument(gone);

        // Open scripts keep the editor's live text, the workspace only swaps in disk text for closed ones.
        foreach (string script in current)
        {
            try { ws.AddOrUpdateDocument(script, File.ReadAllText(script)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        entry.Scripts = current;
        return entry;
    }
}
