// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.SceneView;
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
    private static readonly List<Reveal> _reveals = [];

    // A copied batch waiting on the watcher, with the in-project files dropped alongside it, so the whole drop is placed at once.
    private sealed record Reveal(EditorAssetBackend Db, string DestRel, List<string> Copied, List<string> InProject, SceneDropContext? Scene, DateTime Deadline);

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

    /// <summary> Enqueues file paths from an external drop for deferred processing. </summary>
    public static void Enqueue(string[] paths) => _pending.Enqueue(paths);

    /// <summary> Set by the scene view while a drop resolves over it; the dropped assets spawn there. </summary>
    internal static SceneDropContext? SceneTarget;

    /// <summary> Processes one batch of dropped files per frame, resolving the hover target folder and copying files into the project. </summary>
    public static void ProcessPending()
    {
        UpdateReveals();

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
            SceneTarget = null;
        }
        if (++_resolveFrames < 3) return;

        string[] paths = _resolving;
        _resolving = null;
        SceneDropContext? sceneTarget = SceneTarget?.Freeze();
        SceneTarget = null;
        try { HandleDrop(paths, ProjectPanel.Instance?.ExternalDropHoverFolder, sceneTarget); }
        catch (Exception ex) { Runtime.Debug.LogError($"Failed to process dropped files: {ex.Message}"); }
    }

    private static void HandleDrop(string[] paths, string? targetFolder, SceneDropContext? sceneTarget)
    {
        EditorAssetBackend db = EditorAssetBackend.Instance!;
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

        // In-project sources aren't copied. They're placed with the copied files once those import, so one drop is one grid.
        if (copied.Count == 0)
        {
            Place(db, plan.AlreadyInProject, sceneTarget, ping: true);
            return;
        }

        // No import here. The watcher registers the whole batch before importing any of it
        // (so a model finds its sibling textures), and importing now would import twice.
        _forceProcessUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        _reveals.Add(new Reveal(db, destRel, copied, plan.AlreadyInProject, sceneTarget, DateTime.UtcNow + TimeSpan.FromMinutes(2)));
    }

    /// <summary>Spawn <paramref name="paths"/> on a scene drop; when that spawns nothing (or there's no scene target), ping the first if <paramref name="ping"/>.</summary>
    private static void Place(EditorAssetBackend db, List<string> paths, SceneDropContext? sceneTarget, bool ping)
    {
        if (paths.Count == 0) return;
        if (sceneTarget is { } ctx && SpawnInScene(db, paths, ctx)) return;
        if (!ping) return;
        Guid guid = db.PathToGuid(paths[0]);
        if (guid != Guid.Empty) Selection.Ping(guid);
    }

    /// <summary>Hand the assets to the scene view's drop handlers, as an internal drag would. False when nothing was handled.</summary>
    private static bool SpawnInScene(EditorAssetBackend db, IEnumerable<string> paths, SceneDropContext ctx)
    {
        // The drop point and target were resolved in the scene dropped onto. If another scene is being edited by
        // now (play mode, a scene switch), they mean nothing there: skip the spawn, the files are still imported.
        if (!ReferenceEquals(ctx.Scene, EditorSceneManager.EditedWorld) || Runtime.EngineObjectExtensions.IsNotValid(ctx.Scene)) return false;

        List<AssetEntry> entries = [.. paths.Select(p => db.GetEntry(p)).OfType<AssetEntry>()];
        if (entries.Count == 0) return false;
        AssetEntry first = entries[0];
        return SceneDrop.Drop(new AssetDragPayload(first.Guid, Path.GetFileNameWithoutExtension(first.Path), first.MainAssetType,
            [.. entries.Select(e => e.Guid)], [.. entries.Select(e => e.Path)]), ctx);
    }

    /// <summary>
    /// Once every copied file is imported (or failed to), place the whole drop and toast. Drops a reveal whose backend
    /// was replaced or whose files never all showed up before the deadline.
    /// </summary>
    private static void UpdateReveals()
    {
        for (int i = _reveals.Count - 1; i >= 0; i--)
        {
            Reveal r = _reveals[i];
            if (!ReferenceEquals(r.Db, EditorAssetBackend.Instance) || DateTime.UtcNow >= r.Deadline)
            {
                _reveals.RemoveAt(i);
                continue;
            }
            // New entries start with NeedsReimport set; the importer clears it whether the import succeeds or fails.
            if (!r.Copied.TrueForAll(p => r.Db.GetEntry(p) is { NeedsReimport: false })) continue;
            _reveals.RemoveAt(i);

            // Ping only when the user is already viewing the destination - pinging navigates,
            // and a drop targeted at another folder shouldn't yank them there.
            bool viewingDest = string.Equals(ProjectPanel.Instance?.CurrentFolder ?? "", r.DestRel, StringComparison.OrdinalIgnoreCase);
            Place(r.Db, [.. r.Copied, .. r.InProject], r.Scene, viewingDest);
            Toasts.Show(
                Loc.Get("toast.drop_imported"),
                Loc.Get("toast.drop_imported_msg", new { count = r.Copied.Count, folder = string.IsNullOrEmpty(r.DestRel) ? "Assets" : r.DestRel }),
                ToastType.Success);
        }
    }

    internal sealed class CopyPlan
    {
        public readonly List<(string SourceAbs, string DestAbs, string DestRel)> Files = [];
        public readonly List<string> Directories = [];      // absolute, parents first
        public readonly List<string> AlreadyInProject = []; // assets-relative
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
