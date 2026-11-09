using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Importers;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Manages scene loading, saving, and tracking for the editor.
/// </summary>
public static class EditorSceneManager
{
    private static SceneInstance? s_editWorld;
    private static SceneInstance? s_playWorld;

    /// <summary>
    /// The world scenes are edited in. Bound as Main outside play mode, and dormant underneath during play and prefab
    /// editing, so it comes back exactly as it was.
    /// </summary>
    public static SceneInstance EditWorld
    {
        get => s_editWorld.IsValid() ? s_editWorld : throw new InvalidOperationException("The editor has no edit world bound.");
        internal set
        {
            s_editWorld = value;
            AdoptSections(value);
        }
    }

    /// <summary>The world built from the edit world for play mode, or null outside it.</summary>
    public static SceneInstance? PlayWorld => s_playWorld.IsValid() ? s_playWorld : null;

    /// <summary>The world panels act on: the prefab world while editing a prefab, otherwise Main, the edit world or the play world.</summary>
    public static SceneInstance EditedWorld => PrefabEditingMode.World.IsValid() ? PrefabEditingMode.World : SceneInstance.Main;

    /// <summary>
    /// The play copy of the edit world: each section captured into a section of a new dormant game world, in the same
    /// order and with the same active section, each keeping its source. Nothing changes if this throws.
    /// </summary>
    internal static SceneInstance BuildPlayWorld()
    {
        SceneInstance edit = EditWorld;
        SceneInstance play = SceneInstance.Create(edit.Name, SceneMode.Game);
        s_playCopies.Clear();
        try
        {
            SceneSection? first = null;
            foreach (SceneSection section in edit.Sections)
            {
                if (section.IsPersistent) continue;
                SceneContent content = SceneContent.Read(section.CaptureTree(), ImportHelper.CreateTrackingContext(out _));
                SceneSection copy = first == null
                    ? play.ReplaceContent(content, section.Name, section.Source)
                    : play.AddContent(content, section.Name, section.Source);
                first ??= copy;
                copy.HiddenInSceneView = section.HiddenInSceneView;
                s_playCopies[section] = copy;
                if (ReferenceEquals(section, edit.ActiveSection)) play.ActiveSection = copy;
            }
            return play;
        }
        catch
        {
            play.Dispose();
            throw;
        }
    }

    // Each edit section and the play world section copied from it.
    private static readonly Dictionary<SceneSection, SceneSection> s_playCopies = new(ReferenceEqualityComparer.Instance);

    /// <summary>The section of the edited world that shows <paramref name="scene"/>: its own, or in play the copy made of it.</summary>
    internal static SceneSection? SectionShowing(OpenScene scene)
    {
        if (scene.Section is not { IsLoaded: true } section) return null;
        if (PlayWorld is not { } play) return section;
        return s_playCopies.TryGetValue(section, out SceneSection? copy) && copy.IsLoaded && ReferenceEquals(copy.Instance, play) ? copy : null;
    }

    /// <summary>Sends the edit world dormant, restarts the session and binds <paramref name="play"/> as Main, running.</summary>
    internal static void EnterPlayWorld(SceneInstance play)
    {
        s_worldsBeforePlay = new();
        foreach (SceneInstance world in SceneInstance.Live)
            if (!ReferenceEquals(world, play)) s_worldsBeforePlay.Add(world, null);
        SceneBuild.Set(EditorRegistries.GetSettings<BuildSettings>().Scenes
            .Where(entry => entry.Enabled)
            .Select(entry => AssetDatabase.Get<Scene>(entry.SceneGuid))
            .Where(scene => scene.IsValid())!);
        EditWorld.Deactivate();
        Runtime.Tasks.MainThreadContext.Restart();
        s_playWorld = play;
        SceneInstance.Main = play;
        play.Activate();
    }

    // The worlds that existed when play started. Every other world is play's own and goes when it stops.
    private static ConditionalWeakTable<SceneInstance, object?> s_worldsBeforePlay = new();

    /// <summary>Destroys the play world and every world play opened, inside the session they ran in, and restarts the session.</summary>
    internal static void DestroyPlayWorld()
    {
        SceneInstance? play = s_playWorld;
        s_playWorld = null;
        if (play.IsValid()) play.Dispose();
        foreach (SceneInstance world in SceneInstance.Live)
        {
            if (s_worldsBeforePlay.TryGetValue(world, out _) || world.IsDisposed) continue;
            try { world.Dispose(); }
            catch (Exception e) { Debug.LogException($"[Play] Destroying '{world.Name}' threw", e); }
        }
        s_worldsBeforePlay = new();
        s_playCopies.Clear();
        Runtime.Tasks.MainThreadContext.Restart();
    }

    /// <summary>
    /// Binds the edit world back as Main and runs it again. Nothing is deserialized, it never changed during play, but
    /// instances of prefabs imported meanwhile refresh.
    /// </summary>
    internal static void ReturnToEditWorld()
    {
        SceneInstance.Main = EditWorld;
        EditWorld.Activate();
        PrefabUtility.RefreshStaleInstances();
    }

    // ================================================================
    //  Open scenes
    // ================================================================

    private static readonly List<OpenScene> s_open = [];
    private static readonly List<Action> s_pending = [];
    private static bool s_hooked;

    /// <summary>Every scene in the hierarchy, loaded or not, in order.</summary>
    public static IReadOnlyList<OpenScene> OpenScenes => s_open;

    /// <summary>
    /// The scene new objects go into, whose environment the edit world shows. While a replacing open waits for the safe
    /// point it is the scene being opened.
    /// </summary>
    public static OpenScene ActiveScene
        => s_open.FirstOrDefault(o => o.IsActive) ?? s_open.FirstOrDefault(o => o.ActivatesOnLoad) ?? s_open.FirstOrDefault(o => o.IsLoaded)
           ?? throw new InvalidOperationException("No scene is open.");

    /// <summary>Fires when a scene is listed, loaded or not.</summary>
    public static event Action<OpenScene>? SceneOpened;

    /// <summary>Fires when a scene's section exists, at the safe point after it was asked for.</summary>
    public static event Action<OpenScene>? SceneLoaded;

    /// <summary>Fires before a scene unloads or leaves the hierarchy.</summary>
    public static event Action<OpenScene>? SceneClosing;

    /// <summary>Fires after a scene is written to its file.</summary>
    public static event Action<OpenScene>? SceneSaved;

    /// <summary>The open scene <paramref name="section"/> of the edit world belongs to, or null for any other section.</summary>
    public static OpenScene? SceneOf(SceneSection? section)
    {
        if (section == null) return null;
        foreach (OpenScene open in s_open)
            if (ReferenceEquals(open.Section, section)) return open;
        return null;
    }

    private static readonly List<OpenScene[]> s_drawing = [];

    /// <summary>The open scenes of the objects whose fields are drawn now, so a reference field knows where it is held.</summary>
    internal static IReadOnlyList<OpenScene> DrawingScenes => s_drawing.Count > 0 ? s_drawing[^1] : [];

    /// <summary>The one open scene the drawn objects are in, or null when there is none or several.</summary>
    internal static OpenScene? DrawingScene => DrawingScenes.Count == 1 ? DrawingScenes[0] : null;

    /// <summary>Marks <paramref name="roots"/> as the objects whose fields are drawn until the scope ends.</summary>
    internal static DrawingScope DrawingFor(IEnumerable<object> roots)
    {
        s_drawing.Add([.. roots.Select(root => root switch
        {
            GameObject go => SceneOf(go),
            Component c => c.GameObject.IsValid() ? SceneOf(c.GameObject) : null,
            _ => null,
        }).OfType<OpenScene>().Distinct()]);
        return new DrawingScope();
    }

    internal readonly struct DrawingScope : IDisposable
    {
        public void Dispose() => s_drawing.RemoveAt(s_drawing.Count - 1);
    }

    /// <summary>Whether a reference field held in every one of <paramref name="holders"/> refuses <paramref name="target"/>.</summary>
    internal static bool RefusesReference(IReadOnlyList<OpenScene> holders, GameObject? target, out string reason)
    {
        reason = "";
        foreach (OpenScene holder in holders)
            if (RefusesReference(holder, target, out reason)) return true;
        return false;
    }

    /// <summary>
    /// Whether a reference field in <paramref name="holder"/> refuses <paramref name="target"/>. In edit mode a saved
    /// reference never crosses scenes. The reason names the target's scene.
    /// </summary>
    internal static bool RefusesReference(OpenScene? holder, GameObject? target, out string reason)
    {
        reason = "";
        if (holder == null || target.IsNotValid()) return false;
        OpenScene? other = SceneOf(target);
        if (other == null || ReferenceEquals(other, holder)) return false;
        reason = $"'{target.Name}' is in the scene '{other.Name}', and a reference between scenes is never saved.";
        return true;
    }

    /// <summary>The open scene <paramref name="go"/> is in, or null outside the edit world.</summary>
    public static OpenScene? SceneOf(GameObject? go) => go.IsValid() ? SceneOf(go.Section) : null;

    // Lists every section of a newly bound edit world, so it always has its scenes.
    private static void AdoptSections(SceneInstance world)
    {
        s_open.Clear();
        s_pending.Clear();
        foreach (SceneSection section in world.Sections)
            if (!section.IsPersistent)
                s_open.Add(new OpenScene(section.Source, section.Name) { Section = section });
        if (!s_hooked)
        {
            SceneInstance.SafePoint += ApplyPending;
            ReflectionProbe.BakedChanged += probe => Core.EditorUtility.SetDirty(probe);
            s_hooked = true;
        }
    }

    // Loads and unloads asked for during a frame apply at the safe point, in the order they were asked for.
    private static void ApplyPending()
    {
        while (s_pending.Count > 0)
        {
            Action next = s_pending[0];
            s_pending.RemoveAt(0);
            try { next(); }
            catch (Exception e) { Debug.LogException("[Scene] Applying a scene change threw", e); }
            if (s_pending.Count == 0) RememberSetup();
        }
    }

    private static void RequireEditing(string operation)
    {
        if (Application.IsPlaying) throw new InvalidOperationException($"Cannot {operation} during play mode.");
        if (PrefabEditingMode.IsEditing) throw new InvalidOperationException($"Cannot {operation} while editing a prefab. Leave prefab editing mode first.");
    }

    private static void RefuseDirty(IEnumerable<OpenScene> closing, bool force)
    {
        if (force) return;
        OpenScene? dirty = closing.FirstOrDefault(o => o.IsDirty);
        if (dirty != null) throw new InvalidOperationException($"'{dirty.Name}' has unsaved changes. Save it first, or pass force to discard them.");
    }

    /// <summary>Whether scenes can be opened, closed and saved now: not during play or prefab editing.</summary>
    public static bool CanEditScenes => !Application.IsPlaying && !PrefabEditingMode.IsEditing;

    /// <summary>
    /// Replaces the setup with <paramref name="scene"/>, loaded and active, and clears the undo history. Refused when a
    /// scene it closes is dirty, unless <paramref name="force"/> is set.
    /// </summary>
    public static OpenScene Open(Scene scene, bool force = false)
    {
        RequireEditing("open a scene");
        SceneContent content = ReadContent(scene);
        RefuseDirty(s_open, force);
        return Replace(new OpenScene(scene, scene.Name), content);
    }

    /// <summary>
    /// Adds <paramref name="scene"/> after the open scenes, loaded unless <paramref name="load"/> is false. A scene that
    /// is already listed is returned as it is, so one asset is never two sections.
    /// </summary>
    public static OpenScene OpenAdditive(Scene scene, bool load = true)
    {
        RequireEditing("open a scene");
        OpenScene? listed = s_open.FirstOrDefault(o => ReferenceEquals(o.Scene, scene));
        if (listed != null)
        {
            // Points the hierarchy at its header instead.
            Selection.Ping(scene.AssetID);
            return listed;
        }

        var open = new OpenScene(scene, scene.Name);
        s_open.Add(open);
        SceneOpened?.Invoke(open);
        if (load) Load(open);
        SetupChanged();
        return open;
    }

    /// <summary>
    /// An untitled scene. The first replaces the setup with the default scene of a camera, a light and a floor, and
    /// clears the undo history. An additive one is empty, so it never adds a second main camera or sun.
    /// </summary>
    public static OpenScene New(bool additive = false, bool force = false)
    {
        RequireEditing("create a scene");
        if (!additive)
        {
            RefuseDirty(s_open, force);
            return Replace(new OpenScene(null, "Untitled"), CreateDefaultContent());
        }

        var open = new OpenScene(null, UntitledName());
        s_open.Add(open);
        SceneOpened?.Invoke(open);
        Queue(open, new SceneContent());
        return open;
    }

    private static string UntitledName()
    {
        string name = "Untitled";
        for (int i = 2; s_open.Any(o => o.Name == name); i++) name = $"Untitled {i}";
        return name;
    }

    private static OpenScene Replace(OpenScene open, SceneContent content)
    {
        foreach (OpenScene closing in s_open.ToArray()) SceneClosing?.Invoke(closing);
        foreach (OpenScene closing in s_open) closing.Section = null;
        s_open.Clear();
        s_pending.Clear();
        Undo.ClearAll();
        Selection.Clear();

        s_open.Add(open);
        open.ActivatesOnLoad = true;
        open.LoadPending = true;
        SceneOpened?.Invoke(open);
        s_pending.Add(() =>
        {
            open.LoadPending = false;
            if (!s_open.Contains(open)) return;
            open.Section = EditWorld.ReplaceContent(content, open.Name, open.Scene);
            open.Section.HiddenInSceneView = open.IsHiddenInSceneView;
            open.ActivatesOnLoad = false;
            open.Dirty = false;
            SceneLoaded?.Invoke(open);
        });
        return open;
    }

    /// <summary>Loads a listed scene's section at the safe point. Does nothing when it is loaded or loading.</summary>
    public static void Load(OpenScene scene)
    {
        RequireEditing("load a scene");
        if (!s_open.Contains(scene) || (scene.IsLoaded && !scene.UnloadPending) || scene.LoadPending) return;
        Queue(scene, scene.Scene.IsValid() ? ReadContent(scene.Scene) : new SceneContent());
    }

    private static void Queue(OpenScene open, SceneContent content)
    {
        open.LoadPending = true;
        s_pending.Add(() =>
        {
            if (!open.LoadPending) return;
            open.LoadPending = false;
            if (!s_open.Contains(open) || open.IsLoaded) return;
            SceneInstance edit = EditWorld;
            open.Section = edit.AddContent(content, open.Name, open.Scene);
            open.Section.HiddenInSceneView = open.IsHiddenInSceneView;
            edit.MoveSection(open.Section, SectionIndexOf(open));
            SceneLoaded?.Invoke(open);
        });
    }

    // Where a loaded scene's section sits: after Persistent and every loaded scene listed before it.
    private static int SectionIndexOf(OpenScene open)
    {
        int index = 1;
        foreach (OpenScene other in s_open)
        {
            if (ReferenceEquals(other, open)) break;
            if (other.IsLoaded) index++;
        }
        return index;
    }

    // Scenes loaded once the queued changes apply.
    private static int LoadedCount => s_open.Count(WillBeLoaded);

    private static bool WillBeLoaded(OpenScene scene) => (scene.IsLoaded && !scene.UnloadPending) || scene.LoadPending;

    private static void RefuseClosing(OpenScene scene)
    {
        if (LoadedCount <= 1) throw new InvalidOperationException($"'{scene.Name}' is the last loaded scene. Open another scene to replace it.");
        if (scene.ActivatesOnLoad && scene.LoadPending) throw new InvalidOperationException($"'{scene.Name}' is still opening.");
    }

    /// <summary>
    /// Unloads a scene's section and keeps it listed. Refused when it is dirty unless <paramref name="force"/> is set,
    /// and when it is the last loaded scene, which only <see cref="Open"/> replaces.
    /// </summary>
    public static void Unload(OpenScene scene, bool force = false)
    {
        RequireEditing("unload a scene");
        if (!s_open.Contains(scene) || !WillBeLoaded(scene)) return;
        RefuseClosing(scene);
        RefuseDirty([scene], force);
        Close(scene);
    }

    /// <summary>Unloads a scene and takes it out of the hierarchy. Refused like <see cref="Unload"/>.</summary>
    public static void Remove(OpenScene scene, bool force = false)
    {
        RequireEditing("remove a scene");
        if (!s_open.Contains(scene)) return;
        if (WillBeLoaded(scene))
        {
            RefuseClosing(scene);
            RefuseDirty([scene], force);
            Close(scene);
        }
        else SceneClosing?.Invoke(scene);
        s_open.Remove(scene);
        SetupChanged();
    }

    private static void Close(OpenScene scene)
    {
        SceneClosing?.Invoke(scene);
        scene.Dirty = false;
        scene.ActivatesOnLoad = false;
        if (scene.LoadPending)
        {
            scene.LoadPending = false;
            return;
        }

        SceneSection? section = scene.Section;
        if (section == null) return;
        foreach (object selected in Selection.Selected.ToArray())
            if (selected is GameObject go && (!go.IsValid() || ReferenceEquals(go.Section, section)))
                Selection.RemoveFromSelection(selected);
        Undo.DropScene(scene);
        scene.UnloadPending = true;
        s_pending.Add(() =>
        {
            scene.UnloadPending = false;
            if (section.IsLoaded) EditWorld.UnloadNow(section);
            if (ReferenceEquals(scene.Section, section)) scene.Section = null;
        });
    }

    /// <summary>Makes a loaded scene the active one: new objects go into it, and the edit world shows its environment.</summary>
    public static void SetActive(OpenScene scene)
    {
        RequireEditing("change the active scene");
        if (scene.Section is not { IsLoaded: true } section) throw new InvalidOperationException($"'{scene.Name}' is not loaded.");
        EditWorld.ActiveSection = section;
        SetupChanged();
    }

    /// <summary>Moves a scene to <paramref name="index"/> in the hierarchy, and its section with it.</summary>
    public static void Move(OpenScene scene, int index)
    {
        RequireEditing("move a scene");
        if (!s_open.Remove(scene)) return;
        s_open.Insert(Math.Clamp(index, 0, s_open.Count), scene);
        if (scene.IsLoaded) EditWorld.MoveSection(scene.Section!, SectionIndexOf(scene));
        SetupChanged();
    }

    /// <summary>Whether a scene has changes its file does not.</summary>
    public static bool IsDirty(OpenScene scene) => scene.Dirty;

    /// <summary>Whether any open scene has unsaved changes.</summary>
    public static bool HasDirtyScenes => s_open.Any(o => o.Dirty);

    /// <summary>Marks a scene as changed. Nothing marks during play, since play scenes are never saved.</summary>
    public static void MarkDirty(OpenScene? scene)
    {
        if (scene == null || Application.IsPlaying || !scene.IsLoaded) return;
        scene.Dirty = true;
    }

    /// <summary>Writes a loaded scene to its file. False for an untitled scene, which needs <see cref="SaveAs"/>.</summary>
    public static bool Save(OpenScene scene)
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot save scenes during play mode."); return false; }
        string? path = scene.Path;
        return path != null && Write(scene, path);
    }

    /// <summary>
    /// Writes a loaded scene to <paramref name="relativePath"/> and makes that file its own from then on. Refused when
    /// another listed scene is already that file.
    /// </summary>
    public static bool SaveAs(OpenScene scene, string relativePath)
    {
        if (Application.IsPlaying) { Debug.LogWarning("Cannot save scenes during play mode."); return false; }
        string normalized = EditorAssetBackend.NormalizePath(relativePath);
        if (s_open.Any(o => !ReferenceEquals(o, scene) && string.Equals(o.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            Debug.LogError($"'{normalized}' is another open scene's file, so '{scene.Name}' was not saved over it.");
            return false;
        }
        return Write(scene, normalized);
    }

    /// <summary>Saves every dirty loaded scene that has a file. False when one failed or is untitled.</summary>
    public static bool SaveAll()
    {
        bool all = true;
        foreach (OpenScene scene in s_open.ToArray())
            if (scene.Dirty && scene.IsLoaded && !Save(scene)) all = false;
        return all;
    }

    private static bool Write(OpenScene scene, string relativePath)
    {
        if (Project.Current == null || scene.Section is not { IsLoaded: true } section) return false;
        if (PrefabEditingMode.IsEditing)
        {
            Debug.LogWarning("Cannot save a scene while editing a prefab. Leave prefab editing mode first.");
            return false;
        }

        string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolutePath)!);

        try
        {
            // Anything edited on a prefab instance without the inspector noticing is not in its override list yet, and a
            // save without it would lose those edits on the next refresh.
            PrefabUtility.ReconcileOpenScene();

            File.WriteAllText(absolutePath, section.CaptureTree().WriteToString());

            // The stored scene takes what was written, so a build or a Scene.Load of it sees this save.
            Guid guid = EditorAssetBackend.Instance?.ImportFile(relativePath) ?? Guid.Empty;
            if (guid != Guid.Empty)
            {
                scene.Scene = AssetDatabase.Get<Scene>(guid);
                section.Source = scene.Scene;
            }
            section.Name = scene.Name;
            if (ReferenceEquals(section, EditWorld.ActiveSection)) EditWorld.Name = scene.Name;
            scene.Dirty = false;

            SceneSaved?.Invoke(scene);
            SetupChanged();
            Debug.Log($"Saved scene: {relativePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save scene: {ex.Message}");
            return false;
        }
    }

    /// <summary>The listed scenes with whether each is loaded and active. Untitled scenes have no file and are left out.</summary>
    public static SceneSetup[] GetSetup()
        => [.. s_open.Where(o => o.Scene.IsValid()).Select(o => new SceneSetup
        {
            Asset = o.Scene!.AssetID,
            Path = o.Path ?? "",
            IsLoaded = WillBeLoaded(o),
            IsActive = ReferenceEquals(o, ActiveScene),
        })];

    /// <summary>
    /// Replaces the setup with <paramref name="setup"/>, clearing the undo history. A scene that is gone is skipped with a
    /// warning, and when that leaves no active scene the first loaded one becomes active. Refused like <see cref="Open"/>.
    /// </summary>
    public static void RestoreSetup(SceneSetup[] setup, bool force = false)
    {
        RequireEditing("restore a scene setup");
        var entries = new List<(Scene Scene, SceneSetup Entry, SceneContent? Content)>();
        foreach (SceneSetup entry in setup)
        {
            Scene? scene = Resolve(entry);
            if (scene.IsNotValid())
            {
                Debug.LogWarning($"[Scene] '{entry.Path}' is gone, so it was left out of the restored scenes.");
                continue;
            }
            if (entries.Any(e => ReferenceEquals(e.Scene, scene))) continue;
            SceneContent? content = null;
            if (entry.IsLoaded)
            {
                try { content = ReadContent(scene); }
                catch (Exception e) { Debug.LogWarning($"[Scene] '{scene.Name}' could not be read, so it was restored unloaded: {e.Message}"); }
            }
            entries.Add((scene, entry, content));
        }
        if (!entries.Any(e => e.Content != null))
            throw new InvalidOperationException("None of the scenes to restore can be loaded.");
        RefuseDirty(s_open, force);

        int active = entries.FindIndex(e => e.Content != null && e.Entry.IsActive);
        if (active < 0) active = entries.FindIndex(e => e.Content != null);

        OpenScene first = Replace(new OpenScene(entries[active].Scene, entries[active].Scene.Name), entries[active].Content!);
        s_open.Clear();
        foreach ((Scene scene, SceneSetup _, SceneContent? content) in entries)
        {
            OpenScene open = ReferenceEquals(scene, first.Scene) ? first : new OpenScene(scene, scene.Name);
            s_open.Add(open);
            if (!ReferenceEquals(open, first))
            {
                SceneOpened?.Invoke(open);
                if (content != null) Queue(open, content);
            }
        }
        s_pending.Add(() => { if (first.IsLoaded) EditWorld.MoveSection(first.Section!, SectionIndexOf(first)); });
    }

    private static Scene? Resolve(SceneSetup entry)
    {
        if (entry.Asset != Guid.Empty && AssetDatabase.Get<Scene>(entry.Asset) is { IsMissing: false } byId) return byId;
        return string.IsNullOrEmpty(entry.Path) ? null : SceneAt(entry.Path);
    }

    /// <summary>The scene asset at a path relative to Assets, or null.</summary>
    public static Scene? SceneAt(string relativePath)
    {
        Guid guid = EditorAssetBackend.Instance?.PathToGuid(EditorAssetBackend.NormalizePath(relativePath)) ?? Guid.Empty;
        return guid == Guid.Empty ? null : AssetDatabase.Get<Scene>(guid);
    }

    // Reads the scene's file as it is on disk, through the layout migration, ready to fill a section.
    private static SceneContent ReadContent(Scene scene)
    {
        string? path = EditorAssetBackend.Instance?.GetEntry(scene.AssetID)?.Path;
        if (path == null || Project.Current == null) throw new InvalidOperationException($"'{scene.Name}' has no file in the project.");
        string absolutePath = System.IO.Path.Combine(Project.Current.AssetsPath, path);
        if (!File.Exists(absolutePath)) throw new InvalidOperationException($"Scene file not found: {absolutePath}");

        var echo = EchoObject.ReadFromString(File.ReadAllText(absolutePath));
        Migration.Releases.Release_1_0.SceneContentLayout(echo);
        return SceneContent.Read(echo, ImportHelper.CreateTrackingContext(out _));
    }

    // ================================================================
    //  Editor flows
    // ================================================================

    /// <summary>The save toast label for one scene.</summary>
    public static string LabelOf(OpenScene scene) => Rosetta.Loc.Get("save.scene", new { name = scene.Name });

    /// <summary>
    /// Runs <paramref name="proceed"/> after asking to save whichever of <paramref name="closing"/> has changes, every
    /// open scene when none are named. Saving an untitled scene asks for its path first.
    /// </summary>
    public static void ConfirmClosing(Action proceed, IEnumerable<OpenScene>? closing = null)
    {
        List<OpenScene> dirty = [.. (closing ?? s_open).Where(o => o.Dirty)];
        if (dirty.Count == 0 || !CanEditScenes)
        {
            proceed();
            return;
        }

        SaveManager.AskToSave([.. dirty.Select(LabelOf)], null,
            save: () => SaveThen(dirty, proceed),
            discard: proceed);
    }

    private static void SaveThen(List<OpenScene> scenes, Action proceed)
    {
        foreach (OpenScene scene in scenes)
        {
            if (!scene.Dirty) continue;
            if (Save(scene)) continue;
            if (scene.Path == null) EditorApplication.PromptSaveAs(scene, () => SaveThen(scenes, proceed));
            return;
        }
        proceed();
    }

    /// <summary>Opens the scene at a path relative to Assets, after asking to save changes. Logs why when it cannot.</summary>
    public static void OpenWithPrompt(string relativePath)
    {
        if (!CanEditScenes) { Debug.LogWarning("Cannot open scenes during play mode or prefab editing."); return; }
        Scene? scene = SceneAt(relativePath);
        if (scene.IsNotValid()) { Debug.LogError($"No scene at '{relativePath}'."); return; }
        ConfirmClosing(() => Run(() => Open(scene, force: true)));
    }

    /// <summary>Replaces the setup with a new default scene, after asking to save changes.</summary>
    public static void NewWithPrompt()
    {
        if (!CanEditScenes) { Debug.LogWarning("Cannot create a scene during play mode or prefab editing."); return; }
        ConfirmClosing(() => Run(() => New(force: true)));
    }

    // Logs a refusal from an editor menu rather than letting it escape into the frame.
    private static void Run(Action action)
    {
        try { action(); }
        catch (InvalidOperationException e) { Debug.LogWarning(e.Message); }
    }

    /// <summary>Opens the project's remembered setup, or a new default scene. Called once the project is open.</summary>
    public static void EnsureSceneLoaded()
    {
        if (EditorRegistries.SettingsEntries.Count > 0)
        {
            GeneralSettings general = EditorRegistries.GetSettings<GeneralSettings>();
            if (general.LastSetup.Count > 0)
            {
                try
                {
                    RestoreSetup([.. general.LastSetup], force: true);
                    foreach (OpenScene open in s_open)
                        if (open.Scene.IsValid() && general.HiddenScenes.Contains(open.Scene.AssetID)) open.IsHiddenInSceneView = true;
                    return;
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogWarning($"[Scene] The remembered scenes could not be restored: {e.Message}");
                    // Listed unloaded under the new scene, so the remembered setup is not lost.
                    New(force: true);
                    foreach (SceneSetup entry in general.LastSetup)
                        if (Resolve(entry) is { } scene && scene.IsValid()) OpenAdditive(scene, load: false);
                    return;
                }
            }
        }

        New(force: true);
    }

    // Remembered now, or once the queued changes apply.
    internal static void SetupChanged()
    {
        if (s_pending.Count == 0) RememberSetup();
    }

    // The setup is remembered whenever it changes, so the next session reopens it.
    internal static void RememberSetup()
    {
        if (EditorRegistries.SettingsEntries.Count == 0 || !CanEditScenes) return;
        try
        {
            GeneralSettings general = EditorRegistries.GetSettings<GeneralSettings>();
            SceneSetup[] setup = GetSetup();
            Guid[] hidden = [.. s_open.Where(o => o.IsHiddenInSceneView && o.Scene.IsValid()).Select(o => o.Scene!.AssetID)];
            if (general.LastSetup.SequenceEqual(setup) && general.HiddenScenes.SequenceEqual(hidden)) return;
            general.LastSetup = [.. setup];
            general.HiddenScenes = [.. hidden];
            EditorRegistries.SaveSettings();
        }
        catch (Exception e) { Debug.LogWarning($"[Scene] Remembering the open scenes failed: {e.Message}"); }
    }

    /// <summary>The default scene's content, for a new scene and for writing a new .scene file.</summary>
    public static SceneContent CreateDefaultContent()
    {
        SceneInstance world = CreateDefaultScene();
        try { return world.ActiveSection.Capture(); }
        finally { world.Dispose(); }
    }

    /// <summary>
    /// Build a new default scene with camera, light, floor, and cubes.
    /// </summary>
    public static SceneInstance CreateDefaultScene()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        scene.Name = "Untitled Scene";

        Material? defaultMat = AssetDatabase.Get<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
        Mesh? cubeMesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Cube));
        Mesh? planeMesh = AssetDatabase.Get<Mesh>(BuiltInAssets.GuidForMesh(DefaultModel.Plane));

        var camGo = new GameObject("Main Camera");
        camGo.Tag = "Main Camera";
        camGo.Transform.Position = new Float3(0, 5, -15);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        Camera cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.Profile = VolumeProfile.CreateCameraDefault("Main Camera Profile");
        scene.Add(camGo);

        var lightGo = new GameObject("Directional Light");
        lightGo.Transform.LocalEulerAngles = new Float3(45, 225, 0);
        lightGo.AddComponent<DirectionalLight>();
        scene.Add(lightGo);

        var floorGo = new GameObject("Floor");
        floorGo.Transform.Position = new Float3(0, 0, 0);
        floorGo.Transform.LocalScale = new Float3(1, 1, 1);
        MeshRenderer floorRenderer = floorGo.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = planeMesh;
        floorRenderer.Material = defaultMat;
        scene.Add(floorGo);

        var cube1 = new GameObject("Cube");
        cube1.Transform.Position = new Float3(0, 0.5f, 0);
        MeshRenderer cube1Renderer = cube1.AddComponent<MeshRenderer>();
        cube1Renderer.Mesh = cubeMesh;
        cube1Renderer.Material = defaultMat;
        scene.Add(cube1);

        var cube2 = new GameObject("Cube (1)");
        cube2.Transform.Position = new Float3(2, 0.5f, 1);
        MeshRenderer cube2Renderer = cube2.AddComponent<MeshRenderer>();
        cube2Renderer.Mesh = cubeMesh;
        cube2Renderer.Material = defaultMat;
        scene.Add(cube2);

        return scene;
    }

    [AssetDoubleClickHandler(".scene")]
    private static bool OpenSceneHandler(string relativePath, Guid guid)
    {
        OpenWithPrompt(relativePath);
        return true;
    }

    [AssetDoubleClickHandler(".prefab")]
    private static bool OpenPrefabHandler(string relativePath, Guid guid)
    {
        PrefabEditingMode.Enter(guid);
        return true;
    }
}

/// <summary>A scene listed in the editor's hierarchy, from open until removed, across any number of loads and unloads.</summary>
public sealed class OpenScene
{
    private readonly string _untitledName;

    internal OpenScene(Scene? scene, string name)
    {
        Scene = scene;
        _untitledName = name;
    }

    /// <summary>The scene asset, or null while untitled.</summary>
    public Scene? Scene { get; internal set; }

    /// <summary>Its section of the edit world, or null while unloaded.</summary>
    public SceneSection? Section { get; internal set; }

    public string Name => Scene.IsValid() ? Scene.Name : _untitledName;

    /// <summary>Its file relative to Assets, or null while untitled.</summary>
    public string? Path => Scene.IsValid() ? EditorAssetBackend.Instance?.GetEntry(Scene.AssetID)?.Path : null;

    public bool IsLoaded => Section is { IsLoaded: true };

    public bool IsActive => Section is { IsLoaded: true } section && ReferenceEquals(section, section.Instance.ActiveSection);

    public bool IsDirty => Dirty;

    private bool _hidden;

    /// <summary>Whether its objects are left out of the scene view and its picking. Editor state, kept across loads and sessions.</summary>
    public bool IsHiddenInSceneView
    {
        get => _hidden;
        set
        {
            _hidden = value;
            if (Section != null) Section.HiddenInSceneView = value;
            EditorSceneManager.SetupChanged();
        }
    }

    internal bool Dirty;
    internal bool LoadPending;
    internal bool UnloadPending;
    internal bool ActivatesOnLoad;

    public override string ToString() => Name;
}

/// <summary>One listed scene of a remembered setup.</summary>
public record struct SceneSetup
{
    /// <summary>The scene asset, which survives a rename between sessions.</summary>
    public Guid Asset;

    /// <summary>Its file relative to Assets, for when the asset is gone.</summary>
    public string Path;

    public bool IsLoaded;
    public bool IsActive;
}
