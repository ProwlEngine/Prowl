using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Manages script assembly compilation, loading, and hot-reload. Recompiles migrate the live graph onto the
/// new types in place via <see cref="SceneHotReload"/>, which is the only reload path: there is no unload and
/// no restart, so there is no second path to keep in step with this one. A failed migration is logged and
/// toasted, and the previous code stays running until the next successful reload.
/// </summary>
public static class ScriptAssemblyManager
{
    private static bool _recompileRequested;
    private static DateTime _lastScriptChange;
    private static bool _isCompiling;
    private static readonly object _pendingLock = new();
    private static ScriptCompiler.CompileResult? _pendingResult; // published cross-thread under _pendingLock
    private static DateTime _compileStartedUtc; // when the in-flight compile began, for the summary timing
    private static TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);

    private static AssemblyLoadContext? s_scriptContext;
    private static readonly List<Assembly> s_scriptAssemblies = [];

    // Raw IL bytes of each loaded user assembly, kept so hot reload can read a swapped assembly's IL with
    // Cecil (for lambda/closure migration). Keyed weakly so an unloaded assembly's bytes can be collected.
    [Prowl.Ember.ReloadIgnore]
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, byte[]> s_assemblyBytes = new();

    /// <summary>The raw IL bytes an assembly was loaded from, or null if it wasn't loaded through here.</summary>
    internal static byte[]? GetAssemblyBytes(Assembly asm) => s_assemblyBytes.TryGetValue(asm, out var bytes) ? bytes : null;

    // Plugin resolution state, refreshed each time assemblies are (re)loaded.
    private static readonly Dictionary<string, string> s_managedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<PluginInfo> s_nativePlugins = [];
    private static DllImportResolver? s_nativeResolver;
    private static string s_pluginLoadDir = "";

    static ScriptAssemblyManager()
    {
        RuntimeUtils.AssemblySource = LiveAssemblies;

        // Echo resolves a persisted $type by scanning the domain, which finds the superseded build first. Its
        // override hook is consulted after the predefined and structural cases, so this only redirects the scan.
        Prowl.Echo.TypeNameRegistry.TypeResolver = RuntimeUtils.FindType;
    }

    /// <summary>
    /// Every loaded assembly worth reflecting over, the live script build first and superseded builds left out.
    /// A hot reload leaves the outgoing assembly loaded under the same simple name, so without this a lookup by
    /// name lands on the previous types and an enumeration reports every user type twice.
    /// </summary>
    public static IEnumerable<Assembly> LiveAssemblies()
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in s_scriptAssemblies)
        {
            live.Add(asm.GetName().Name ?? string.Empty);
            yield return asm;
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (s_scriptAssemblies.Contains(asm)) continue;

            // Sharing a name with a live script assembly without being one means it is a previous build.
            if (live.Contains(asm.GetName().Name ?? string.Empty)) continue;

            yield return asm;
        }
    }

    /// <summary>Signal that scripts have changed and need recompilation.</summary>
    public static void RequestRecompile()
    {
        _recompileRequested = true;
        _lastScriptChange = DateTime.UtcNow;
    }

    /// <summary>Call once per frame. Triggers compilation after debounce period.</summary>
    public static void Update()
    {
        if (ConsumeFinishedCompile()) return;

        if (!_recompileRequested || _isCompiling) return;
        if (Project.Current == null) return;

        // Only compile when the editor window is focused (don't spam while user is editing externally).
        if (!Window.IsFocused) return;

        // Wait for debounce
        if (DateTime.UtcNow - _lastScriptChange < DebounceDelay) return;

        _recompileRequested = false;

        var project = Project.Current;
        bool hasScripts = Directory.Exists(project.AssetsPath) &&
            Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories).Any();
        bool hasPackages = ScriptCompiler.ProjectDeclaresPackages(project);

        // Nothing to build and no packages to restore, so there is nothing to do.
        if (!hasScripts && !hasPackages)
            return;

        _isCompiling = true;
        _compileStartedUtc = DateTime.UtcNow;

        // Toast that a compile started, only when scripts will actually reload.
        if (hasScripts)
            EditorApplication.Instance?.NotifyCompiling();

        // Compile on a background thread; the result is published to the main thread under _pendingLock.
        Task.Run(() =>
        {
            ScriptCompiler.CompileResult result;
            try
            {
                result = ScriptCompiler.CompileAll(project);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"[ScriptAssemblyManager] Compilation exception: {ex.Message}");
                result = new ScriptCompiler.CompileResult { Success = false, Errors = ex.Message };
            }
            lock (_pendingLock) _pendingResult = result;
        });
    }

    /// <summary>
    /// If a background compile finished, act on it: hot-reload live (works in play mode too). A reload that
    /// fails is logged and toasted, and the previous code keeps running until the next successful reload, since
    /// nothing is unloaded and the editor never restarts. Returns true if a finished result was handled this
    /// frame.
    /// </summary>
    private static bool ConsumeFinishedCompile()
    {
        ScriptCompiler.CompileResult result;
        lock (_pendingLock)
        {
            if (_pendingResult == null) return false;
            result = _pendingResult.Value;
            _pendingResult = null;
        }
        _isCompiling = false;

        int elapsedMs = (int)(DateTime.UtcNow - _compileStartedUtc).TotalMilliseconds;

        if (!result.Success)
        {
            // The per line compiler errors are already in the console (the "why"); summarise and toast.
            Runtime.Debug.LogError("[Scripts] Compilation failed. See the errors above.");
            EditorApplication.Instance?.NotifyCompileFailed();
            return true;
        }
        if (!result.RequiresReload)
        {
            Runtime.Debug.LogSuccess($"[Scripts] Packages restored in {elapsedMs}ms.");
            return true;
        }

        // Guard the whole reload so nothing (a registry rebuild) can escape into the GUI frame.
        try
        {
            if (TryHotReload(Project.Current!))
            {
                Runtime.Debug.LogSuccess($"[Scripts] Recompiled and hot reloaded in {elapsedMs}ms.");
                EditorApplication.Instance?.NotifyScriptsReloaded($"Recompiled and reloaded in {elapsedMs}ms.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Hot-reload threw: {ex.Message}\n{ex.StackTrace}");
        }

        // The migration failed. The old assemblies are still the active set (the commit only happens on
        // success), so the editor keeps running on the old code; the user fixes it and saves to retry.
        EditorApplication.Instance?.NotifyReloadFailed();
        Runtime.Debug.LogWarning("[ScriptAssemblyManager] Hot reload failed; keeping the old code. Fix and save to retry.");
        return true;
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

    /// <summary>Load all pre-built user assemblies into a collectible AssemblyLoadContext.</summary>
    public static void LoadAssemblies(Project project)
    {
        RefreshPluginResolution(project);

        if (s_scriptContext == null)
        {
            s_scriptContext = new AssemblyLoadContext("ProwlScripts", isCollectible: true);
            // Resolve managed plugin dependencies of user assemblies (engine assemblies fall back
            // to the default context automatically; plugins are not in any context yet).
            s_scriptContext.Resolving += ResolveManagedPlugin;
        }

        // Load every produced user assembly in dependency order.
        foreach (var dll in ScriptCompiler.GetEditorAssemblyPaths(project))
            LoadAssembly(dll, Path.GetFileNameWithoutExtension(dll));

        if (ScriptsPredateEngine(project))
        {
            Runtime.Debug.Log("[Scripts] The engine has been rebuilt since these scripts were compiled; recompiling them.");
            RequestRecompile();
        }
    }

    /// <summary>
    /// Whether the compiled user assemblies predate the engine they are about to run against.
    /// <para/>
    /// They were built against whatever engine was running at the time, so one rebuilt since can have
    /// moved or dropped a member they still call - and nothing else notices, because the script files
    /// themselves are unchanged and that is the only thing the recompile rule looks at. The mismatch
    /// then waits until the affected code path runs and throws MissingMethodException, by which point
    /// nothing points back at the engine change that caused it.
    /// </summary>
    internal static bool ScriptsPredateEngine(Project project)
    {
        DateTime oldestBuilt = DateTime.MaxValue;

        foreach (var dll in ScriptCompiler.GetEditorAssemblyPaths(project))
        {
            if (!File.Exists(dll)) continue;

            DateTime built = File.GetLastWriteTimeUtc(dll);
            if (built < oldestBuilt) oldestBuilt = built;
        }

        return oldestBuilt != DateTime.MaxValue && ScriptCompiler.EngineBuildTimeUtc() > oldestBuilt;
    }

    /// <summary>Snapshot the project's plugins so the resolvers can satisfy user-assembly imports.</summary>
    private static void RefreshPluginResolution(Project project)
    {
        s_managedPlugins.Clear();
        s_nativePlugins.Clear();
        s_pluginLoadDir = Path.Combine(project.ScriptAssemblyPath, ".loaded");

        // Managed plugins are still copied into this folder (see ResolveManagedPlugin) to keep their
        // source unlocked, so clear stale copies from a previous load here now that the script
        // assemblies no longer use it. Safe: the old context was unloaded before we got here.
        try
        {
            if (Directory.Exists(s_pluginLoadDir))
                foreach (var f in Directory.EnumerateFiles(s_pluginLoadDir))
                    try { File.Delete(f); } catch { }
        }
        catch { }

        foreach (var plugin in PluginScanner.ScanAll(project))
        {
            if (plugin.IsManaged)
                s_managedPlugins[Path.GetFileNameWithoutExtension(plugin.FileName)] = plugin.AbsolutePath;
            else
                s_nativePlugins.Add(plugin);
        }

        s_nativeResolver ??= ResolveNativePlugin;
    }

    private static Assembly? ResolveManagedPlugin(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name == null || !s_managedPlugins.TryGetValue(name.Name, out var path) || !File.Exists(path))
            return null;

        try
        {
            // Copy to the unlocked .loaded directory (under ScriptAssemblies, NOT under Assets, so it
            // is never rescanned as a plugin) so the source plugin stays writable.
            string tempDir = string.IsNullOrEmpty(s_pluginLoadDir)
                ? Path.Combine(Path.GetDirectoryName(path)!, ".loaded")
                : s_pluginLoadDir;
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, $"{name.Name}_{Guid.NewGuid():N}.dll");
            File.Copy(path, tempPath, true);
            return context.LoadFromAssemblyPath(tempPath);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to resolve managed plugin '{name.Name}': {ex.Message}");
            return null;
        }
    }

    private static IntPtr ResolveNativePlugin(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        string requested = libraryName;
        foreach (var plugin in s_nativePlugins)
        {
            string stem = Path.GetFileNameWithoutExtension(plugin.FileName);
            if (stem.StartsWith("lib", StringComparison.Ordinal)) stem = stem[3..];

            bool matches = plugin.FileName.Equals(requested, StringComparison.OrdinalIgnoreCase)
                || stem.Equals(requested, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(plugin.FileName).Equals(requested, StringComparison.OrdinalIgnoreCase);

            if (matches && NativeLibrary.TryLoad(plugin.AbsolutePath, out IntPtr handle))
                return handle;
        }
        return IntPtr.Zero;
    }

    /// <summary>Load the user assemblies into a caller-owned context (not the active one) and return them.</summary>
    private static List<Assembly> LoadAssembliesInto(Project project, AssemblyLoadContext context)
    {
        var loaded = new List<Assembly>();
        foreach (var dll in ScriptCompiler.GetEditorAssemblyPaths(project))
        {
            if (!File.Exists(dll)) continue;

            byte[] bytes = File.ReadAllBytes(dll);
            using var stream = new MemoryStream(bytes);
            var asm = context.LoadFromStream(stream);
            s_assemblyBytes.AddOrUpdate(asm, bytes);
            loaded.Add(asm);

            if (s_nativeResolver != null && s_nativePlugins.Count > 0)
                try { NativeLibrary.SetDllImportResolver(asm, s_nativeResolver); } catch { }
        }
        return loaded;
    }

    private static void LoadAssembly(string dllPath, string label)
    {
        if (!File.Exists(dllPath) || s_scriptContext == null) return;

        try
        {
            // Load from the bytes, not the path, so the DLL on disk stays unlocked for the next
            // recompile (no on disk copy needed). The PDB is embedded in the image, so stack traces
            // still resolve to user source lines.
            byte[] bytes = File.ReadAllBytes(dllPath);
            using var stream = new MemoryStream(bytes);
            var asm = s_scriptContext.LoadFromStream(stream);
            s_assemblyBytes.AddOrUpdate(asm, bytes);
            s_scriptAssemblies.Add(asm);

            // Let this assembly's P/Invokes resolve against the project's native plugins.
            if (s_nativeResolver != null && s_nativePlugins.Count > 0)
                try { NativeLibrary.SetDllImportResolver(asm, s_nativeResolver); } catch { }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Failed to load {label} assembly: {ex.Message}");
        }
    }

    // ================================================================
    //  Hot-Reload
    // ================================================================

    /// <summary>Check if compiled assemblies exist for this project.</summary>
    public static bool HasScriptAssemblies(Project project)
        => Directory.Exists(project.ScriptAssemblyPath)
           && Directory.EnumerateFiles(project.ScriptAssemblyPath, "*.dll").Any();

    /// <summary>
    /// The single reload path: migrate the live graph onto the new types in place when there are loaded
    /// assemblies to swap away from, otherwise (first load) just load them and rebuild registries. Returns
    /// false on failure, leaving the old code active for the caller to toast and keep running.
    /// </summary>
    private static bool TryHotReload(Project project)
    {
        // Migration needs the currently loaded assemblies to swap away from.
        if (s_scriptContext != null && s_scriptAssemblies.Count > 0)
            return MigrateHotReload(project);

        return InitialLoad(project);
    }

    /// <summary>
    /// Load the new assemblies alongside the old, migrate the live scene and statics onto the new types, rebuild
    /// registries, then best-effort unload the old context. Never depends on that unload succeeding, so it
    /// reloads even when the old assemblies stay pinned. On failure returns false with the old code still active.
    /// </summary>
    private static bool MigrateHotReload(Project project)
    {
        try
        {
            var scene = Scene.Current;
            AssemblyLoadContext oldContext = s_scriptContext!;
            var oldAssemblies = s_scriptAssemblies.ToList();

            // Load the new assemblies into a fresh collectible context, keeping the old ones loaded.
            RefreshPluginResolution(project);
            var newContext = new AssemblyLoadContext("ProwlScripts", isCollectible: true);
            newContext.Resolving += ResolveManagedPlugin;

            var newAssemblies = LoadAssembliesInto(project, newContext);
            if (newAssemblies.Count == 0)
            {
                try { newContext.Unload(); } catch { }
                return false;
            }

            // Undo history holds closures + references captured against the old code; a recompile is a natural
            // boundary, so drop it. Do this before the walk so watching the editor doesn't migrate it needlessly.
            try { Undo.Clear(); } catch { }

            // Migrate the live scene onto the new types (both assemblies loaded, no unload needed). Do this
            // BEFORE committing the new context as active, so a mid-migration throw leaves the old assemblies
            // as the active set - the editor then keeps running on the old code. The walk also migrates the
            // editor's own references into the scene (selection, inspector, hierarchy), so no repointing needed.
            if (scene != null)
            {
                var pairs = new List<(Assembly, Assembly)>();
                foreach (var oldAsm in oldAssemblies)
                {
                    string? name = oldAsm.GetName().Name;
                    Assembly? match = newAssemblies.FirstOrDefault(a => a.GetName().Name == name);
                    if (match != null)
                        pairs.Add((oldAsm, match));
                }
                if (pairs.Count > 0)
                    SceneHotReload.Migrate(scene, pairs);
            }

            // Migration succeeded: the new assemblies become the active set that registries scan.
            s_scriptContext = newContext;
            s_scriptAssemblies.Clear();
            s_scriptAssemblies.AddRange(newAssemblies);
            DebounceDelay = TimeSpan.FromSeconds(1);

            // Rebuild the editor registries (custom editors, menu items, ...) against the new assemblies.
            EditorApplication.Instance?.ReinitializeAfterReload();

            // Per-type caches (RuntimeUtils, registries, ...) are ReloadCaches now and clear themselves
            // through the walk, so nothing to drop explicitly here.

            // Best-effort unload the old context. No forced GC and no restart if it stays pinned.
            try { oldContext.Unload(); } catch { }

            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Migration hot-reload failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// First load (nothing loaded yet to migrate from): load the assemblies into the script context and rebuild
    /// registries. No unload and no scene reserialize, because there is no previous graph to migrate off; this
    /// path only loads and starts watching.
    /// </summary>
    private static bool InitialLoad(Project project)
    {
        try
        {
            DebounceDelay = TimeSpan.FromSeconds(1);
            LoadAssemblies(project);
            EditorApplication.Instance?.ReinitializeAfterReload();
            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[ScriptAssemblyManager] Initial script load failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
}
