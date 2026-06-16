using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Manages script assembly compilation, loading, and hot-reload.
/// Uses a collectible AssemblyLoadContext so assemblies can be fully unloaded
/// and replaced. Falls back to an editor restart if unloading fails.
/// </summary>
public static class ScriptAssemblyManager
{
    private static bool _recompileRequested;
    private static DateTime _lastScriptChange;
    private static bool _isCompiling;
    private static ScriptCompiler.CompileResult? _pendingResult;
    private static bool _restartDeferred;
    private static TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

    private static AssemblyLoadContext? s_scriptContext;
    private static readonly List<Assembly> s_scriptAssemblies = [];
    private const int MaxGCAttempts = 10;

    /// <summary>Signal that scripts have changed and need recompilation.</summary>
    public static void RequestRecompile()
    {
        _recompileRequested = true;
        _lastScriptChange = DateTime.UtcNow;
    }

    /// <summary>Call once per frame. Triggers compilation after debounce period.</summary>
    public static void Update()
    {
        // Check if background compilation finished
        if (_pendingResult.HasValue)
        {
            // A compile that kicked off during the debounce window can finish after the user
            // has entered play mode. Restarting now would snapshot the in-play scene over
            // the user's saved scene, so park the result until play mode ends.
            if (Application.IsPlaying)
            {
                if (!_restartDeferred)
                {
                    _restartDeferred = true;
                    Runtime.Debug.Log("[ScriptAssemblyManager] Compilation finished during play. Restart deferred until play mode ends.");
                }
                return;
            }

            var result = _pendingResult.Value;
            _pendingResult = null;
            _isCompiling = false;
            _restartDeferred = false;

            if (result.Success)
            {
                Runtime.Debug.Log("[ScriptAssemblyManager] Compilation successful. Attempting hot-reload...");

                if (!TryHotReload(Project.Current!))
                {
                    Runtime.Debug.LogWarning("[ScriptAssemblyManager] Hot-reload failed. Restarting editor...");
                    RestartEditor(Project.Current!);
                }
            }
            else
            {
                Runtime.Debug.LogError("[ScriptAssemblyManager] Compilation failed. Fix errors and save to retry.");
            }
            return;
        }

        if (!_recompileRequested || _isCompiling) return;
        if (Project.Current == null) return;

        // Only compile when the editor window is focused (don't spam while user is editing externally)
        if (!Window.IsFocused) return;

        // Only compile when not Playing
        if (Application.IsPlaying) return;

        // Wait for debounce
        if (DateTime.UtcNow - _lastScriptChange < DebounceDelay) return;

        _recompileRequested = false;

        // Quick check: any .cs files at all?
        var project = Project.Current;
        if (!Directory.Exists(project.AssetsPath) ||
            !Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories).Any())
        {
            Runtime.Debug.Log("[ScriptAssemblyManager] No scripts found, skipping compilation.");
            return;
        }

        _isCompiling = true;
        Runtime.Debug.Log("[ScriptAssemblyManager] Starting compilation...");

        // Run on background thread result polled on main thread via _pendingResult
        Task.Run(() =>
        {
            try
            {
                _pendingResult = ScriptCompiler.CompileAll(project);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"[ScriptAssemblyManager] Compilation exception: {ex.Message}");
                _pendingResult = new ScriptCompiler.CompileResult { Success = false, Errors = ex.Message };
            }
        });
    }

    // ================================================================
    //  Assembly Enumeration
    // ================================================================

    /// <summary>
    /// Returns all assemblies that registries should scan:
    /// script assemblies first (so hot-reloaded types are found), then default context.
    /// </summary>
    public static IEnumerable<Assembly> GetAllRelevantAssemblies()
    {
        foreach (var asm in s_scriptAssemblies)
            yield return asm;
        foreach (var asm in AssemblyLoadContext.Default.Assemblies)
            yield return asm;
    }

    /// <summary>
    /// Enumerates all types from all relevant assemblies (engine + scripts).
    /// Safely handles <see cref="ReflectionTypeLoadException"/>.
    /// </summary>
    public static IEnumerable<Type> GetAllTypes()
    {
        foreach (var assembly in GetAllRelevantAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }
            catch { continue; }

            foreach (var type in types)
                yield return type;
        }
    }

    // ================================================================
    //  Loading
    // ================================================================

    /// <summary>Load pre-built script assemblies into a collectible AssemblyLoadContext.</summary>
    public static void LoadAssemblies(Project project)
    {
        s_scriptContext ??= new AssemblyLoadContext("ProwlScripts", isCollectible: true);

        LoadAssembly(project.GameAssemblyPath, "game");
        LoadAssembly(project.EditorAssemblyPath, "editor");
    }

    private static void LoadAssembly(string dllPath, string label)
    {
        if (!File.Exists(dllPath) || s_scriptContext == null) return;

        try
        {
            // Copy to a unique temp path so the original stays unlocked for recompilation
            string tempDir = Path.Combine(Path.GetDirectoryName(dllPath)!, ".loaded");
            Directory.CreateDirectory(tempDir);

            // Purge leftover files from previous sessions
            foreach (var f in Directory.EnumerateFiles(tempDir))
                try { File.Delete(f); } catch { }

            string stem = Path.GetFileNameWithoutExtension(dllPath);
            string tempPath = Path.Combine(tempDir, $"{stem}_{Guid.NewGuid():N}.dll");
            File.Copy(dllPath, tempPath, true);

            // Also copy the .pdb so stack traces resolve to user source lines
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                try { File.Copy(pdbPath, Path.ChangeExtension(tempPath, ".pdb"), true); } catch { }
            }

            var asm = s_scriptContext.LoadFromAssemblyPath(Path.GetFullPath(tempPath));
            s_scriptAssemblies.Add(asm);
            Runtime.Debug.Log($"[ScriptAssemblyManager] Loaded {label} assembly: {Path.GetFileName(dllPath)}");
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to load {label} assembly: {ex.Message}");
        }
    }

    // ================================================================
    //  Unloading
    // ================================================================

    /// <summary>
    /// Unload the script assembly context, then verify it is truly gone via a forced GC loop.
    /// Returns false if it survives (caller should restart). The strong reference to the context
    /// is confined entirely to <see cref="UnloadContext"/>: this method only ever holds the
    /// <see cref="WeakReference"/>, so no live local roots the context across the GC loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool UnloadScriptAssemblies()
    {
        if (s_scriptContext == null)
            return true;

        // Drop framework reflection caches that key off the user assemblies.
        foreach (var asm in s_scriptAssemblies)
            TypeDescriptor.Refresh(asm);
        s_scriptAssemblies.Clear();

        WeakReference weakCtx = UnloadContext();

        // Forced GC loop. Collect, run finalizers, collect again so anything resurrected for
        // finalization is reclaimed within the same iteration.
        // In this section, it's useless to re-assign s_scriptContext as it pins down the ALC and, if it fails to reload,
        // the editor will restart anyway so there's no need to have it here
        for (int i = 0; i < MaxGCAttempts && weakCtx.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        if (weakCtx.IsAlive)
        {
            string hint = System.Diagnostics.Debugger.IsAttached
                ? " A DEBUGGER IS ATTACHED - the CLR keeps collectible assemblies alive for the whole debug " +
                  "session, so hot-reload cannot unload while debugging. Run without the debugger (Ctrl+F5 / " +
                  "launch the built exe) to get true hot-reload; under the debugger the editor restarts instead."
                : " No debugger attached - the pin is a runtime type/method handle (reflection or JIT/emit cache " +
                  "of a script type), which a heap dump's managed graph can't show. Falling back to editor restart.";
            Debug.LogError($"[ScriptAssemblyManager] Script context still alive after {MaxGCAttempts} GC attempts." + hint);
            return false;
        }

        Debug.Log("[ScriptAssemblyManager] Unload successful.");
        return true;
    }

    /// <summary>
    /// Nulls the static field and unloads the context. The only strong reference to the context
    /// lives in this method's <c>ctx</c> local, which goes out of scope on return — so the caller's
    /// GC loop can collect it. NoInlining keeps that local out of the caller's stack frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference UnloadContext()
    {
        AssemblyLoadContext ctx = s_scriptContext!;
        s_scriptContext = null;
        var weak = new WeakReference(ctx);
        ctx.Unload();
        return weak;
    }

    // ================================================================
    //  Hot-Reload
    // ================================================================

    /// <summary>Check if compiled assemblies exist for this project.</summary>
    public static bool HasScriptAssemblies(Project project)
        => File.Exists(project.GameAssemblyPath) || File.Exists(project.EditorAssemblyPath);

    /// <summary>
    /// Attempt to hot-reload script assemblies without restarting the editor.
    /// Saves the scene, unloads old assemblies, loads new ones, reinitializes
    /// registries, and restores the scene. Returns false if unloading fails.
    /// </summary>
    private static bool TryHotReload(Project project)
    {
        try
        {
            // 1. Save the current scene state
            SaveSceneForRestart(project);
            EditorApplication.Instance?.SaveProjectState();

            // 2. Drop every editor/runtime strong reference into the old assemblies. Without this
            //    the collectible context stays rooted and the unload below fails, forcing a restart.
            EditorApplication.Instance?.ReleaseScriptReferences();

            // 3. Unload old assemblies - if this fails, caller will restart.
            if (!UnloadScriptAssemblies())
                return false;

            DebounceDelay = TimeSpan.FromSeconds(1);

            // 4. Load the new assemblies into a fresh context.
            LoadAssemblies(project);

            // 5. Reinitialize all editor registries (re-scans assemblies for attributes)
            EditorApplication.Instance?.ReinitializeAfterReload();

            // 6. Restore the scene
            string autoSavePath = project.AutoSaveScenePath;
            if (File.Exists(autoSavePath))
                EditorApplication.Instance?.RestoreAutoSavedScene(autoSavePath);

            Runtime.Debug.LogSuccess("[ScriptAssemblyManager] Hot-reload successful!");
            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Hot-reload failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    // ================================================================
    //  Editor Restart (fallback)
    // ================================================================

    /// <summary>Save all state and restart the editor process.</summary>
    private static void RestartEditor(Project project)
    {
        SaveSceneForRestart(project);
        EditorApplication.Instance?.SaveProjectState();

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Runtime.Debug.LogError("[ScriptAssemblyManager] Cannot determine editor executable path for restart.");
            return;
        }

        string args = $"--project \"{project.RootPath}\"";
        if (File.Exists(project.AutoSaveScenePath))
            args += $" --restore-scene \"{project.AutoSaveScenePath}\"";

        try
        {
            System.Diagnostics.Process? child;

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                && TryFindAppBundle(exePath, out string? appBundle))
            {
                string openArgs = $"-n -a \"{appBundle}\" --args {args}";
                Runtime.Debug.Log($"[ScriptAssemblyManager] Restarting via open: {openArgs}");
                child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = openArgs,
                    UseShellExecute = false,
                });
            }
            else
            {
                Runtime.Debug.Log($"[ScriptAssemblyManager] Restarting: {exePath} {args}");
                child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                });
            }

            if (child != null)
                Runtime.Debug.Log($"[ScriptAssemblyManager] Spawned PID {child.Id}");
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to restart: {ex.Message}");
            return;
        }

        Environment.Exit(0);
    }

    private static bool TryFindAppBundle(string executablePath, out string? appBundle)
    {
        appBundle = null;
        var dir = new DirectoryInfo(Path.GetDirectoryName(executablePath) ?? "");
        while (dir != null)
        {
            if (dir.Extension.Equals(".app", StringComparison.OrdinalIgnoreCase))
            {
                appBundle = dir.FullName;
                return true;
            }
            dir = dir.Parent;
        }
        return false;
    }

    // ================================================================
    //  Scene Save/Restore
    // ================================================================

    private static void SaveSceneForRestart(Project project)
    {
        if (PrefabEditingMode.IsEditing)
            PrefabEditingMode.Exit();

        var scene = Scene.Current;
        if (scene == null) return;

        try
        {
            var savedId = scene.AssetID;
            scene.AssetID = Guid.Empty;

            var echo = Serializer.Serialize(scene);
            scene.AssetID = savedId;

            if (echo != null)
            {
                File.WriteAllText(project.AutoSaveScenePath, echo.WriteToString());

                string? originalPath = EditorSceneManager.CurrentScenePath;
                string sidecar = (originalPath ?? "") + "\n" + savedId.ToString();
                File.WriteAllText(project.AutoSaveScenePath + ".meta", sidecar);

                Runtime.Debug.Log("[ScriptAssemblyManager] Scene auto-saved for restart.");
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[ScriptAssemblyManager] Failed to auto-save scene: {ex.Message}");
        }
    }
}
