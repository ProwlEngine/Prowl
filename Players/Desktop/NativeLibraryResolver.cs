// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Prowl.Player;

/// <summary>
/// Finds managed and native dependencies in a published player layout.
/// </summary>
/// <remarks>
/// A published build puts managed dependencies under <c>runtimes/</c> and natives under
/// <c>runtimes/{rid}/native</c>, neither of which the default probing logic looks in. This belongs in the
/// platform layer of a real assembly rather than being emitted as text into a generated program.
/// </remarks>
public static class NativeLibraryResolver
{
    private static bool s_installed;
    private static readonly object s_lock = new();

    /// <summary>Diagnostic log path. Null disables logging, which is the default for a shipped game.</summary>
    public static string? LogPath { get; set; }

    /// <summary>
    /// Registers the resolvers. Safe to call more than once. Must run before anything triggers a load,
    /// which is why the entry point calls it first.
    /// </summary>
    /// <remarks>
    /// Mentions no engine type on purpose. Preparing a method loads every type named in its body before
    /// its first statement runs, so naming one here would demand that assembly's dependencies out of
    /// <c>runtimes/</c> before the resolver that finds them exists. The engine's own import resolver is
    /// registered later, once loading works, from <see cref="DesktopPlayer.Initialize"/>.
    /// </remarks>
    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            s_installed = true;
        }

        AssemblyLoadContext.Default.Resolving += ResolveManaged;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveUnmanaged;

        if (Assembly.GetEntryAssembly() is { } entry)
            NativeLibrary.SetDllImportResolver(entry, ResolveForImport);
    }

    private static IntPtr ResolveUnmanaged(Assembly assembly, string libraryName) => Resolve(libraryName);

    private static Assembly? ResolveManaged(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        string probePath = Path.Combine(AppContext.BaseDirectory, "runtimes", assemblyName.Name + ".dll");
        Log($"[Resolver] Probing: {probePath}");

        if (!File.Exists(probePath))
        {
            Log($"[Resolver] Not found: {probePath}");
            return null;
        }

        Log($"[Resolver] Loading: {probePath}");
        return context.LoadFromAssemblyPath(probePath);
    }

    public static IntPtr ResolveForImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => Resolve(libraryName);

    private static IntPtr Resolve(string libraryName)
    {
        string baseDir = AppContext.BaseDirectory;

        foreach (string rid in RuntimeIdentifierChain())
        {
            string nativeSubDir = string.IsNullOrEmpty(rid)
                ? Path.Combine("runtimes", "native")
                : Path.Combine("runtimes", rid, "native");

            foreach (string candidate in FileNameCandidates(libraryName))
            {
                string fullPath = Path.Combine(baseDir, nativeSubDir, candidate);
                if (TryLoad(fullPath, out IntPtr handle)) return handle;
            }
        }

        // Last chance: beside the executable.
        foreach (string candidate in FileNameCandidates(libraryName))
            if (TryLoad(Path.Combine(baseDir, candidate), out IntPtr handle))
                return handle;

        Log($"[Native] Not found: {libraryName}");
        return IntPtr.Zero;
    }

    /// <summary>The runtime identifier and each less specific form of it, then the catch alls.</summary>
    private static IEnumerable<string> RuntimeIdentifierChain()
    {
        string rid = RuntimeInformation.RuntimeIdentifier;

        while (!string.IsNullOrEmpty(rid))
        {
            yield return rid;

            int dash = rid.IndexOf('-');
            rid = dash > 0 ? rid[..dash] : string.Empty;
        }

        yield return "any";
        yield return string.Empty;
    }

    private static IEnumerable<string> FileNameCandidates(string libraryName)
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        string[] extensions = windows ? [".dll", ""]
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? [".dylib", ""]
            : [".so", ".so.1", ""];

        string[] names = windows ? [libraryName] : [libraryName, "lib" + libraryName];

        foreach (string name in names)
            foreach (string extension in extensions)
                yield return name + extension;
    }

    private static bool TryLoad(string path, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!File.Exists(path)) return false;

        Log($"[Native] Probing: {path}");

        if (NativeLibrary.TryLoad(path, out handle))
        {
            Log($"[Native] Loaded: {path}");
            return true;
        }

        // Present but unloadable almost always means one of its own dependencies is missing.
        Log($"[Native] Load failed, likely a missing dependency: {path}");
        return false;
    }

    private static void Log(string message)
    {
        if (LogPath is not { } path) return;

        // A file, because the console may not exist yet when resolution starts.
        try { File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { }
    }
}
