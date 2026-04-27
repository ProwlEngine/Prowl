using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Scripting;

/// <summary>
/// Manages script assembly compilation, loading, and editor restart.
/// Debounces recompile requests and orchestrates the compile → restart cycle.
/// </summary>
public static class ScriptAssemblyManager
{
    private static bool _recompileRequested;
    private static DateTime _lastScriptChange;
    private static bool _isCompiling;
    private static ScriptCompiler.CompileResult? _pendingResult;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

    private static readonly List<Assembly> s_scriptAssemblies = [];
    private static readonly object s_lock = new();

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
            var result = _pendingResult.Value;
            _pendingResult = null;
            _isCompiling = false;

            if (result.Success)
            {
                Runtime.Debug.Log("[ScriptAssemblyManager] Compilation successful. Restarting editor...");
                RestartEditor(Project.Current!);
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


    /// <summary>
    /// Returns all assemblies that registries should scan:
    /// default-context assemblies (engine, BCL) plus any loaded script assemblies.
    /// </summary>
    public static IEnumerable<Assembly> GetAllRelevantAssemblies()
    {
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


    /// <summary>Load pre-built script assemblies from Library/ScriptAssemblies/.
    /// Copies to a temp path first so the original DLL stays unlocked for recompilation.</summary>
    public static void LoadAssemblies(Project project)
    {
        LoadAssembly(project.GameAssemblyPath, "game");
        LoadAssembly(project.EditorAssemblyPath, "editor");
    }

    private static void LoadAssembly(string dllPath, string label)
    {
        if (!File.Exists(dllPath)) return;

        try
        {
            // Copy to a unique temp path so the original stays unlocked for recompilation
            // and we don't collide with leftover files from previous sessions
            string tempDir = Path.Combine(Path.GetDirectoryName(dllPath)!, ".loaded");
            Directory.CreateDirectory(tempDir);

            // Purge ALL leftover files from previous sessions (dll + pdb + anything). The
            // per-GUID unique naming scheme means these can't be loaded against, but they
            // pile up on disk across restarts and on Windows leftovers can be the very
            // same DLL the previous (dying) process still has mmap'd which can cause the
            // CLR to resolve to a stale copy if another code path loads by simple name.
            foreach (var f in Directory.EnumerateFiles(tempDir))
                try { File.Delete(f); } catch { /* held open by a dying process harmless */ }

            string stem = Path.GetFileNameWithoutExtension(dllPath);
            string tempPath = Path.Combine(tempDir, $"{stem}_{Guid.NewGuid():N}.dll");
            File.Copy(dllPath, tempPath, true);

            // Also copy the .pdb next to the .dll if present, so stack traces resolve to
            // user source lines in debug builds.
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                try { File.Copy(pdbPath, Path.ChangeExtension(tempPath, ".pdb"), true); } catch { }
            }

            Assembly.LoadFrom(tempPath);
            Runtime.Debug.Log($"[ScriptAssemblyManager] Loaded {label} assembly: {Path.GetFileName(dllPath)} (from {Path.GetFileName(tempPath)})");
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to load {label} assembly: {ex.Message}");
        }
    }

    /// <summary>Check if compiled assemblies exist for this project.</summary>
    public static bool HasScriptAssemblies(Project project)
        => File.Exists(project.GameAssemblyPath) || File.Exists(project.EditorAssemblyPath);

    /// <summary>Save all state and restart the editor process.</summary>
    private static void RestartEditor(Project project)
    {
        // Auto-save the current scene
        SaveSceneForRestart(project);

        // Save editor layout and settings
        EditorApplication.Instance?.SaveProjectState();

        // Get the editor executable path
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Runtime.Debug.LogError("[ScriptAssemblyManager] Cannot determine editor executable path for restart.");
            return;
        }

        // Build command-line args (same format on every platform only the launcher differs)
        string args = $"--project \"{project.RootPath}\"";
        if (File.Exists(project.AutoSaveScenePath))
            args += $" --restore-scene \"{project.AutoSaveScenePath}\"";

        try
        {
            System.Diagnostics.Process? child;

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                && TryFindAppBundle(exePath, out string? appBundle))
            {
                // macOS: route through `open -n -a` so LaunchServices activates the new
                // instance properly (Dock icon, focus, app-nap). Directly Process.Start'ing
                // the Mach-O inside the bundle often spawns a process that never presents
                // a window the classic "restart silently does nothing" symptom.
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

    /// <summary>
    /// Walk up from a Mach-O path looking for the enclosing .app bundle directory.
    /// Returns false if not running from a bundle (e.g. `dotnet run`, loose binary).
    /// </summary>
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

    private static void SaveSceneForRestart(Project project)
    {
        var scene = Scene.Current;
        if (scene == null) return;

        try
        {
            // Temporarily clear AssetID on scene so it serializes fully
            var savedId = scene.AssetID;
            scene.AssetID = Guid.Empty;

            var echo = Serializer.Serialize(scene);
            scene.AssetID = savedId;

            if (echo != null)
            {
                File.WriteAllText(project.AutoSaveScenePath, echo.WriteToString());

                // Sidecar records the scene's original Assets-relative path and AssetID so
                // the restored session knows where the subsequent Ctrl+S should write. Without
                // this, CurrentScenePath comes back null and Save falls through to SaveAs.
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
