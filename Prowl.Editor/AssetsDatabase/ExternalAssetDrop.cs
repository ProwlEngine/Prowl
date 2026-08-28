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
    private static readonly ConcurrentQueue<string[]> _pending = new();
    private static DateTime _forceProcessUntil = DateTime.MinValue;
    private static string[]? _resolving;
    private static int _resolveFrames;
    private static readonly List<(EditorAssetBackend Db, Action<string[]> Handler, DateTime Deadline)> _reveals = new();

    /// <summary>
    /// True briefly after a drop, so the import pump runs even when ReimportOnFocusOnly
    /// gates it - a drop does not necessarily focus the window.
    /// </summary>
    public static bool ForceProcessActive => DateTime.UtcNow < _forceProcessUntil;

    /// <summary>
    /// True while a dropped batch waits for Paper's hover pass to identify the folder under
    /// the drop point - opens the ProjectPanel hover gates without an internal drag.
    /// </summary>
    public static bool IsResolvingDropTarget => _resolving != null;

    public static void Enqueue(string[] paths) => _pending.Enqueue(paths);

    /// <summary>Called once per frame from the editor update loop.</summary>
    public static void ProcessPending()
    {
        SweepReveals();

        if (_resolving == null && _pending.IsEmpty) return;

        if (Project.Current == null || ProjectLauncher.IsOpen || EditorAssetBackend.Instance == null)
        {
            _resolving = null;
            _pending.Clear();
            return;
        }

        // One batch at a time: GLFW moves the cursor to the drop point before raising the drop,
        // so holding a few frames lets the panel's hover callbacks resolve the folder under it.
        if (_resolving == null)
        {
            if (!_pending.TryDequeue(out _resolving)) return;
            _resolveFrames = 0;
            ProjectPanel.Instance?.ClearDropHover();
        }
        if (++_resolveFrames < 3) return;

        string[] paths = _resolving;
        _resolving = null;
        try { HandleDrop(paths, ProjectPanel.Instance?.ExternalDropHoverFolder); }
        catch (Exception ex) { Runtime.Debug.LogError($"Failed to process dropped files: {ex.Message}"); }
    }

    private static void HandleDrop(string[] paths, string? targetFolder)
    {
        var db = EditorAssetBackend.Instance!;
        string assetsPath = Project.Current!.AssetsPath;

        string destRel = targetFolder ?? ProjectPanel.Instance?.CurrentFolder ?? "";
        if (!string.IsNullOrEmpty(destRel) && !Directory.Exists(Path.Combine(assetsPath, destRel)))
            destRel = "";

        CopyPlan plan = PlanCopy(paths, assetsPath, destRel);

        // Before the no-files return, so dropping an empty folder still creates it.
        foreach (string dir in plan.Directories)
        {
            try
            {
                Directory.CreateDirectory(dir);
                // The watcher ignores directory events, so folder metas are created here.
                MetaFile.EnsureMeta(dir, "DefaultImporter");
            }
            catch (Exception ex) { Runtime.Debug.LogError($"Failed to create folder '{dir}': {ex.Message}"); }
        }

        // In-project sources aren't copied - only revealed.
        if (plan.Files.Count == 0)
        {
            if (plan.AlreadyInProject.Count > 0)
            {
                Guid existing = db.PathToGuid(plan.AlreadyInProject[0]);
                if (existing != Guid.Empty) Selection.Ping(existing);
            }
            return;
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
        _forceProcessUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
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
            _reveals.RemoveAll(r => r.Handler == onImported);
            // Ping only when the user is already viewing the destination - pinging navigates,
            // and a drop targeted at another folder shouldn't yank them there.
            Guid guid = db.PathToGuid(first);
            if (guid != Guid.Empty && string.Equals(ProjectPanel.Instance?.CurrentFolder ?? "", destRel, StringComparison.OrdinalIgnoreCase))
                Selection.Ping(guid);
            Toasts.Show(
                Loc.Get("toast.drop_imported"),
                Loc.Get("toast.drop_imported_msg", new { count = copied.Count, folder = string.IsNullOrEmpty(destRel) ? "Assets" : destRel }),
                ToastType.Success);
        };
        db.OnAssetsImported += onImported;
        _reveals.Add((db, onImported, DateTime.UtcNow + TimeSpan.FromMinutes(2)));
    }

    /// <summary>Detach reveals whose backend was replaced (its event will never fire again)
    /// or whose files never imported before the deadline.</summary>
    private static void SweepReveals()
    {
        for (int i = _reveals.Count - 1; i >= 0; i--)
        {
            var (db, handler, deadline) = _reveals[i];
            if (ReferenceEquals(db, EditorAssetBackend.Instance) && DateTime.UtcNow < deadline) continue;
            db.OnAssetsImported -= handler;
            _reveals.RemoveAt(i);
        }
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
        string destAbs = Path.Combine(assetsPath, destRel);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in sources)
        {
            // One unreadable source (ACLs, cycles, malformed path) skips that item, not the batch.
            try
            {
                string src = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"Skipped dropped item '{raw}': {ex.Message}");
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
            plan.Files.Add((file, Path.Combine(destDir, name), CombineRel(destRel, name)));
        }

        foreach (string dir in Directory.EnumerateDirectories(srcDir))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            // Junctions/symlinks can cycle back into an ancestor.
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            AddTree(plan, dir, Path.Combine(destDir, name), CombineRel(destRel, name));
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
