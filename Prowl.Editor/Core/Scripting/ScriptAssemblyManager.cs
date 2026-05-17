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
using Prowl.Editor.AssetsDatabase;
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
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

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
    /// Unload the script assembly context. Uses GC retries to verify the context
    /// is truly gone. Returns false if unloading fails (caller should restart).
    /// NoInlining prevents the JIT from keeping references on the stack that would
    /// prevent the GC from collecting the context.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool UnloadScriptAssemblies()
    {
        if (s_scriptContext == null)
            return true;

        // Clear framework-level type caches that hold references into the assembly
        foreach (var asm in s_scriptAssemblies)
        {
            try { TypeDescriptor.Refresh(asm); } catch { }
        }

        s_scriptAssemblies.Clear();

        // Unload in a separate method so the local reference to the context
        // doesn't stay on this method's stack frame during the GC loop.
        UnloadContextInternal(out WeakReference contextRef);

        for (int i = 0; contextRef.IsAlive; i++)
        {
            if (i >= MaxGCAttempts)
            {
                Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to unload script assemblies after {MaxGCAttempts} GC attempts.");
                // Context is still alive - recover the reference so we don't leak it
                s_scriptContext = contextRef.Target as AssemblyLoadContext;
                return false;
            }

            Runtime.Debug.Log($"[ScriptAssemblyManager] GC attempt ({i + 1}/{MaxGCAttempts})...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Runtime.Debug.Log("[ScriptAssemblyManager] Script assemblies unloaded successfully.");
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadContextInternal(out WeakReference contextRef)
    {
        s_scriptContext!.Unload();
        contextRef = new WeakReference(s_scriptContext);
        s_scriptContext = null;
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

            // 2. Clear all caches that hold Type references or reflection data
            Echo.Serializer.ClearCache();
            Runtime.RuntimeUtils.ClearCache();
            Runtime.GraphTools.NodeRegistry.Reinitialize();
            Runtime.MeshFeatures.MeshFeatureRegistry.Reinitialize();

            // Editor ComponentIconRegistry cache
            typeof(ComponentIconRegistry)
                .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null)
                ?.GetType().GetMethod("Clear")?
                .Invoke(typeof(ComponentIconRegistry)
                    .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)?
                    .GetValue(null), null);

            // 3. Unload old assemblies - if this fails, caller will restart
            if (!UnloadScriptAssemblies())
                return false;

            // 4. Load the new assemblies into a fresh context
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
