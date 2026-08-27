// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Editor;

/// <summary>
/// Handles files dragged from the OS onto the editor window: copies them into the Project
/// panel's current folder, where the asset watcher picks them up and imports them.
/// </summary>
public static class ExternalAssetDrop
{
    private static readonly ConcurrentQueue<string[]> s_pending = new();
    private static DateTime s_forceProcessUntil = DateTime.MinValue;

    /// <summary>
    /// True briefly after a drop, so the import pump runs even when ReimportOnFocusOnly
    /// gates it - a drop does not necessarily focus the window.
    /// </summary>
    public static bool ForceProcessActive => DateTime.UtcNow < s_forceProcessUntil;

    public static void Enqueue(string[] paths) => s_pending.Enqueue(paths);

    /// <summary>Called once per frame from the editor update loop.</summary>
    public static void ProcessPending()
    {
        if (s_pending.IsEmpty) return;

        if (Project.Current == null || ProjectLauncher.IsOpen || EditorAssetBackend.Instance == null)
        {
            while (s_pending.TryDequeue(out _)) { }
            return;
        }

        while (s_pending.TryDequeue(out string[]? paths))
        {
            try { HandleDrop(paths); }
            catch (Exception ex) { Runtime.Debug.LogError($"Failed to process dropped files: {ex.Message}"); }
        }
    }

    private static void HandleDrop(string[] paths)
    {
        var db = EditorAssetBackend.Instance!;
        string assetsPath = Project.Current!.AssetsPath;

        string destRel = ProjectPanel.Instance?.CurrentFolder ?? "";
        if (!string.IsNullOrEmpty(destRel) && !Directory.Exists(Path.Combine(assetsPath, destRel)))
            destRel = "";

        CopyPlan plan = PlanCopy(paths, assetsPath, destRel);

        // Sources already inside Assets/ are not copied; just reveal the first one.
        if (plan.Files.Count == 0)
        {
            if (plan.AlreadyInProject.Count > 0)
            {
                Guid existing = db.PathToGuid(plan.AlreadyInProject[0]);
                if (existing != Guid.Empty) Selection.Ping(existing);
            }
            return;
        }

        foreach (string dir in plan.Directories)
        {
            Directory.CreateDirectory(dir);
            // The watcher ignores directory events, so folder metas are created here.
            try { MetaFile.EnsureMeta(dir, "DefaultImporter"); }
            catch (Exception ex) { Runtime.Debug.LogError($"Failed to create folder meta for '{dir}': {ex.Message}"); }
        }

        var copied = new List<string>();
        foreach ((string srcAbs, string destAbs, string rel) in plan.Files)
        {
            try
            {
                File.Copy(srcAbs, destAbs);
                copied.Add(rel);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"Failed to copy '{srcAbs}': {ex.Message}");
            }
        }

        if (copied.Count == 0) return;

        // No import here. The watcher registers the whole batch before importing any of it
        // (so a model finds its sibling textures), and importing now would import twice.
        s_forceProcessUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        RegisterReveal(db, destRel, copied);
    }

    /// <summary>Ping and toast once the watcher has imported the copied batch.</summary>
    private static void RegisterReveal(EditorAssetBackend db, string destRel, List<string> copied)
    {
        var awaiting = new HashSet<string>(copied, StringComparer.OrdinalIgnoreCase);
        Action<string[]>? onImported = null;
        onImported = imported =>
        {
            string? first = Array.Find(imported, awaiting.Contains);
            if (first == null) return;

            db.OnAssetsImported -= onImported;
            Guid guid = db.PathToGuid(first);
            if (guid != Guid.Empty) Selection.Ping(guid);
            Toasts.Show(
                Loc.Get("toast.drop_imported"),
                Loc.Get("toast.drop_imported_msg", new { count = copied.Count, folder = string.IsNullOrEmpty(destRel) ? "Assets" : destRel }),
                ToastType.Success);
        };
        db.OnAssetsImported += onImported;
    }

    internal sealed class CopyPlan
    {
        public readonly List<(string SourceAbs, string DestAbs, string DestRel)> Files = new();
        public readonly List<string> Directories = new();      // absolute, parents first
        public readonly List<string> AlreadyInProject = new(); // assets-relative
    }

    /// <summary>Resolve dropped paths to copy operations. Reads the filesystem, never writes it.</summary>
    internal static CopyPlan PlanCopy(IEnumerable<string> sources, string assetsPath, string destRel)
    {
        var plan = new CopyPlan();
        string destAbs = string.IsNullOrEmpty(destRel) ? assetsPath : Path.Combine(assetsPath, destRel);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in sources)
        {
            string src;
            try { src = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { continue; }

            string name = Path.GetFileName(src);
            if (ShouldSkip(name)) continue;

            if (IsSameOrUnder(assetsPath, src))
            {
                plan.AlreadyInProject.Add(Path.GetRelativePath(assetsPath, src).Replace('\\', '/'));
                continue;
            }

            if (Directory.Exists(src))
            {
                if (IsSameOrUnder(src, destAbs))
                {
                    Runtime.Debug.LogWarning($"Skipped dropped folder '{src}': it contains the destination.");
                    continue;
                }
                string unique = UniqueName(destAbs, name, "", claimed);
                AddTree(plan, src, Path.Combine(destAbs, unique), CombineRel(destRel, unique));
            }
            else if (File.Exists(src))
            {
                string unique = UniqueName(destAbs, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name), claimed);
                plan.Files.Add((src, Path.Combine(destAbs, unique), CombineRel(destRel, unique)));
            }
        }

        return plan;
    }

    private static void AddTree(CopyPlan plan, string srcDir, string destDir, string destRel)
    {
        plan.Directories.Add(destDir);

        foreach (string file in Directory.EnumerateFiles(srcDir))
        {
            string name = Path.GetFileName(file);
            if (ShouldSkip(name)) continue;
            plan.Files.Add((file, Path.Combine(destDir, name), destRel + "/" + name));
        }

        foreach (string dir in Directory.EnumerateDirectories(srcDir))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            AddTree(plan, dir, Path.Combine(destDir, name), destRel + "/" + name);
        }
    }

    // .meta is skipped because a foreign GUID colliding with a tracked one would silently
    // bind the new file to the existing asset; fresh metas are minted on import instead.
    private static bool ShouldSkip(string fileName)
        => fileName.Length == 0
        || fileName.StartsWith('.')
        || fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    private static string UniqueName(string folder, string baseName, string ext, HashSet<string> claimed)
    {
        string result = UniqueNames.MakeUnique(baseName, candidate =>
        {
            string full = candidate + ext;
            return claimed.Contains(full)
                || File.Exists(Path.Combine(folder, full))
                || Directory.Exists(Path.Combine(folder, full));
        }, stripExistingSuffix: false) + ext;
        claimed.Add(result);
        return result;
    }

    private static bool IsSameOrUnder(string parent, string path)
    {
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string c = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return c.Equals(p, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(p + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string CombineRel(string folder, string name)
        => string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
}
