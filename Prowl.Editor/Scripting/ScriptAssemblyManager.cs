using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Editor.Importers;
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

        // Run on background thread — result polled on main thread via _pendingResult
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

            // Clean old temp files
            try { foreach (var f in Directory.GetFiles(tempDir, "*.dll")) File.Delete(f); } catch { }

            string tempPath = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(dllPath)}_{Guid.NewGuid():N}.dll");
            File.Copy(dllPath, tempPath, true);
            Assembly.LoadFrom(tempPath);
            Runtime.Debug.Log($"[ScriptAssemblyManager] Loaded {label} assembly: {Path.GetFileName(dllPath)}");
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

        // Build command-line args
        string args = $"--project \"{project.RootPath}\"";
        if (File.Exists(project.AutoSaveScenePath))
            args += $" --restore-scene \"{project.AutoSaveScenePath}\"";

        Runtime.Debug.Log($"[ScriptAssemblyManager] Restarting: {exePath} {args}");

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to restart: {ex.Message}");
            return;
        }

        Environment.Exit(0);
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
                Runtime.Debug.Log("[ScriptAssemblyManager] Scene auto-saved for restart.");
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"[ScriptAssemblyManager] Failed to auto-save scene: {ex.Message}");
        }
    }
}
