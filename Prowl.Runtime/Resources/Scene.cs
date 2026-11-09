// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Runtime.Tasks;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A scene as stored in a .scene file. Holding one keeps only its stored data loaded, not what the scene uses:
/// <see cref="Load"/> and <see cref="AssetDatabase.Preload"/> load that.
/// </summary>
public sealed class Scene : Asset
{
    [SerializeField] private EchoObject? _data;

    /// <summary>The stored scene, a serialized <see cref="SceneContent"/>.</summary>
    public EchoObject? Data
    {
        get { EnsureLoaded(); return _data; }
        set { EnsureLoaded(); _data = value; }
    }

    /// <summary>Every asset the stored scene names, loaded or not.</summary>
    public IReadOnlyList<Asset> ReferencedAssets
    {
        get { EnsureLoaded(); return StoredTree.ReferencedAssets(_data); }
    }

    /// <summary>Replaces <paramref name="into"/>'s content with this scene, Main when none is given, at the next safe point.</summary>
    public SceneLoad Load(SceneInstance? into = null, LoadOptions options = default)
        => Queue(new SceneLoad(this, SceneLoadKind.Load, ReferenceEquals(into, null) ? SceneInstance.Main : into, SceneMode.Game, options));

    /// <summary>Adds this scene as a section of <paramref name="into"/>, Main when none is given, at the next safe point.</summary>
    public SceneLoad LoadAdditive(SceneInstance? into = null, LoadOptions options = default)
        => Queue(new SceneLoad(this, SceneLoadKind.Additive, ReferenceEquals(into, null) ? SceneInstance.Main : into, SceneMode.Game, options));

    /// <summary>Creates a new dormant world from this scene, with it as the active section. The caller activates it.</summary>
    public SceneLoad Open(SceneMode mode = SceneMode.Game, LoadOptions options = default)
        => Queue(new SceneLoad(this, SceneLoadKind.Open, null, mode, options));

    private static SceneLoad Queue(SceneLoad load)
    {
        if (!load.IsFinished) SceneInstance.Queue(load);
        return load;
    }

    /// <summary>
    /// A detached copy of the stored scene, in no world, with its stored identifiers. Nothing in it gets
    /// <c>OnAddedToScene</c>, <c>OnEnable</c> or any tick.
    /// </summary>
    public SceneContent ReadContent()
    {
        EchoObject? data = Data;
        return data == null ? new SceneContent() : SceneContent.Read(data);
    }

    // A stored scene is an Echo tree, which in memory takes several times what its file does.
    protected internal override long EstimateBytes() => Math.Max(256 * 1024, (AssetDatabase.Backend?.GetEstimatedSize(AssetID) ?? 0) * 4);
}

/// <summary>The stored payload of a <see cref="Scene"/>: its objects and settings, plain data in no world.</summary>
public sealed class SceneContent : ISerializationCallbackReceiver
{
    /// <summary>
    /// Every object, children included, so every stored identifier is restored. An array, since Echo reads one element
    /// at a time and an object that fails to load leaves only its own slot empty.
    /// </summary>
    public GameObject[] Objects = [];
    public SceneEnvironment Environment = new();
    public SceneLighting Lighting = new();
    public LightmapBakeSettings LightmapBake = new();

    /// <summary>The objects with no parent, in stored order.</summary>
    public IEnumerable<GameObject> Roots
    {
        get
        {
            foreach (GameObject obj in Objects)
                if (obj.IsValid() && obj.Parent.IsNotValid())
                    yield return obj;
        }
    }

    /// <summary>Every object and component of this content by identifier, for resolving links in it.</summary>
    public Dictionary<Guid, EngineObject> IdentifierMap()
    {
        var map = new Dictionary<Guid, EngineObject>();
        foreach (GameObject obj in Objects)
        {
            if (obj.IsNotValid()) continue;
            map.TryAdd(obj.Identifier, obj);
            foreach (Component component in obj.GetComponents<Component>())
                map.TryAdd(component.Identifier, component);
        }
        return map;
    }

    /// <summary>Reads a stored tree back with its stored identifiers.</summary>
    public static SceneContent Read(EchoObject data, SerializationContext? context = null)
        => GameObject.PreservingIdentifiersWhile(() => (context == null ? Serializer.Deserialize<SceneContent>(data) : Serializer.Deserialize<SceneContent>(data, context)) ?? new SceneContent());

    /// <summary>Writes this content as a stored tree, as a .scene file holds it.</summary>
    public EchoObject Write() => Serializer.Serialize(typeof(object), this);

    /// <summary>Drops the objects destroyed since this content was read and adds the ones parented under it since.</summary>
    internal void Resync()
    {
        var kept = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);
        var objects = new List<GameObject>(Objects.Length);
        foreach (GameObject obj in Objects)
            if (IsKept(obj) && kept.Add(obj))
                objects.Add(obj);

        foreach (GameObject root in objects.ToArray())
            if (root.Parent.IsNotValid())
                AddChildren(root);
        Objects = [.. objects];

        void AddChildren(GameObject parent)
        {
            foreach (GameObject child in parent.Children)
            {
                if (child.IsDestroyQueued) continue;
                if (kept.Add(child)) objects.Add(child);
                AddChildren(child);
            }
        }

        static bool IsKept(GameObject obj)
        {
            for (GameObject? go = obj; go.IsValid(); go = go.Parent)
                if (go.IsDestroyQueued) return false;
            return obj.IsValid();
        }
    }

    /// <summary>Disposes every object in this content.</summary>
    internal void DisposeObjects()
    {
        foreach (GameObject root in Roots.ToArray())
            root.Dispose();
        Objects = [];
    }

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        Objects = [.. Objects.Where(obj => !ReferenceEquals(obj, null))];
        foreach (GameObject obj in Objects)
        {
            if (obj.LoadedIdentifier != Guid.Empty)
                obj.SetIdentifier(obj.LoadedIdentifier);

            foreach (Component comp in obj.GetComponents<Component>())
                if (comp._loadedIdentifier != Guid.Empty)
                    comp.Identifier = comp._loadedIdentifier;
        }
    }
}

/// <summary>Where one renderer's surface sits in the baked atlas.</summary>
public struct LightmapPlacement
{
    /// <summary>Which atlas page, indexing <see cref="SceneLighting.Lightmaps"/>.</summary>
    public int Index;
    /// <summary>Scale and offset taking the renderer's UVs into that page.</summary>
    public Float4 ScaleOffset;
}

/// <summary>Baked lightmaps + light-probe data for this scene, produced by the editor lightmap bake.</summary>
public sealed class SceneLighting
{
    /// <summary>Baked lightmap atlas pages (RGBM-encoded). A placement below selects one.</summary>
    public List<Texture2D> Lightmaps = [];

    /// <summary>What the decoded RGBM texels of every page are multiplied by to give irradiance over pi, in nits.</summary>
    public float LightmapScale = 1f;

    /// <summary>
    /// Where each baked renderer landed in the atlas, keyed by the identifier of the object it is on.
    /// Here rather than on the renderer because it belongs to this scene's bake and to nothing else,
    /// which is also what stops a prefab instance reading as modified the moment it is baked.
    /// </summary>
    public Dictionary<Guid, LightmapPlacement> Placements = [];
    /// <summary>World-space light-probe positions.</summary>
    public Float3[] ProbePositions = [];
    /// <summary>Baked SH per probe, indexed with <see cref="ProbePositions"/>.</summary>
    public SphericalHarmonicsL2[] ProbeSH = [];
    /// <summary>Tetrahedralization of the probes: 4 probe indices per tetrahedron.</summary>
    public int[] ProbeTetrahedra = [];
    /// <summary>Per-tetra neighbour links: 4 per tetra (across the face opposite vertex i), -1 = hull.</summary>
    public int[] ProbeTetNeighbours = [];

    public bool HasLightmaps => Lightmaps.Count > 0;
    public bool HasProbes => ProbeSH.Length > 0;

    /// <summary>Where this object's surface was baked, or nothing if it was not.</summary>
    public LightmapPlacement? PlacementFor(Guid objectIdentifier)
        => Placements.TryGetValue(objectIdentifier, out LightmapPlacement placement) ? placement : null;

    /// <summary>Drop every baked lightmap page and placement, leaving the probes alone.</summary>
    public void ClearLightmaps()
    {
        Lightmaps.Clear();
        Placements.Clear();
        MarkChanged();
    }

    // Bumped when the pages or placements change, so renderers resolve their placement again.
    internal int Version { get; private set; }

    /// <summary>Tells renderers that already resolved a placement that the pages or placements changed, like after a rebake.</summary>
    public void MarkChanged() => Version++;
}

/// <summary>
/// Per-scene lightmapper configuration, edited in the editor's Environment panel and consumed by
/// the bake. Persisted with the scene (it's a public field) so a bake's settings survive editor
/// reloads.
/// </summary>
public sealed class LightmapBakeSettings
{
    // Atlas / resolution
    public int AtlasSize = 1024;
    public float TexelsPerUnit = 20f;
    public int DilatePixels = 2;          // edge dilation to stop bilinear bleed at seams

    // Quality
    public int Bounces = 2;
    public int Samples = 64;              // progressive indirect iterations before finalize
    public int ProbeSamples = 256;
    public bool DoBackfaceCull = false;   // cull back faces on all bake rays (matches Prowl's backface-culled rendering)

    // Trace one texel per NxN atlas cell and interpolate the rest. 1 traces everything; higher
    // values converge far faster and cost fine indirect detail. Contacts and corners always trace.
    public int SparseStride = 1;

    // Feed the scene's ambient colour in as ray-miss (sky) radiance.
    public bool BakeSkyLighting = false;

    // Debug: bake every surface as a white Lambertian (isolates light/GI from albedo).
    public bool IgnoreAlbedo = false;
}

/// <summary>Where a <see cref="SceneLoad"/> is.</summary>
public enum SceneLoadPhase
{
    /// <summary>The assets the level uses load in the background.</summary>
    Preloading,
    /// <summary>The stored content becomes detached objects.</summary>
    Instantiating,
    /// <summary>Ready, held back by <see cref="SceneLoad.AllowActivation"/>.</summary>
    WaitingForActivation,
    /// <summary>Queued for the next safe point.</summary>
    Activating,
    Done,
    Failed,
    Cancelled,
}

internal enum SceneLoadKind { Load, Additive, Open }

/// <summary>How a scene loads.</summary>
public struct LoadOptions
{
    /// <summary>Start with <see cref="SceneLoad.AllowActivation"/> false, for a "press any key" screen.</summary>
    public bool HoldActivation;
    /// <summary>After activation, free everything nothing reaches at once, without the grace period.</summary>
    public bool UnloadUnusedAssets;
}

/// <summary>A section that finished activating, and whether it was loaded additively.</summary>
public readonly record struct SceneLoadedArgs(SceneSection Section, bool Additive);

/// <summary>A scene load that failed: the asset was missing or unreadable, or instantiating it threw.</summary>
public sealed class SceneLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A scene loading. It completes exactly once: done, failed or cancelled. Awaiting it gives the loaded section, throws a
/// <see cref="SceneLoadException"/> when it failed, and an <see cref="OperationCanceledException"/> when it was cancelled.
/// </summary>
public sealed class SceneLoad
{
    private readonly TaskCompletionSource<SceneSection> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancel = new();
    private AssetGroup? _group;
    private SceneContent? _content;

    internal SceneLoad(Scene scene, SceneLoadKind kind, SceneInstance? target, SceneMode openMode, LoadOptions options)
    {
        Scene = scene;
        Kind = kind;
        Target = target;
        OpenMode = openMode;
        Options = options;
        AllowActivation = !options.HoldActivation;
        try { _group = AssetDatabase.Preload(scene); }
        catch (Exception e) { Fail(e); }
    }

    public Scene Scene { get; }
    internal SceneLoadKind Kind { get; }
    internal SceneInstance? Target { get; }
    internal SceneMode OpenMode { get; }
    internal LoadOptions Options { get; }

    public SceneLoadPhase Phase { get; private set; }

    /// <summary>How far the load is, 0 to 1. Preloading counts for most of it.</summary>
    public float Progress => Phase switch
    {
        SceneLoadPhase.Preloading => (_group?.Progress ?? 0f) * 0.9f,
        SceneLoadPhase.Instantiating => 0.9f,
        _ => 1f,
    };

    public Exception? Error { get; private set; }

    /// <summary>The loaded section once done. Its <see cref="SceneSection.Instance"/> is the world, a new one for an open.</summary>
    public SceneSection? Section { get; private set; }

    /// <summary>False holds a ready load back until it is set.</summary>
    public bool AllowActivation { get; set; }

    // True while its activation runs, when a Load queued from a callback must not cancel it.
    internal bool IsApplying { get; set; }

    internal bool IsFinished => Phase is SceneLoadPhase.Done or SceneLoadPhase.Failed or SceneLoadPhase.Cancelled;

    // The detached objects from instantiating until activation, a root of the asset walk.
    internal GameObject[]? PendingObjects => _content?.Objects;

    /// <summary>Cancels the load unless it is done or activating. Its detached objects go away.</summary>
    public void Cancel()
    {
        if (IsFinished || IsApplying) return;
        Phase = SceneLoadPhase.Cancelled;
        Release();
        _cancel.Cancel();
        _completion.TrySetCanceled(_cancel.Token);
    }

    /// <summary>
    /// Blocks until the load is ready to activate. Activation still happens at the next safe point, which outside an
    /// engine frame is at once.
    /// </summary>
    public void Wait()
    {
        MainThreadContext.AssertMainThread();
        if (IsFinished) return;
        _group?.Wait();
        Advance();
        if (!SceneInstance.FrameRunning) SceneInstance.ApplyQueue();
    }

    /// <summary>Awaits the load, giving up with an <see cref="OperationCanceledException"/> when <paramref name="cancellation"/> fires.</summary>
    public Task<SceneSection> WaitAsync(CancellationToken cancellation) => _completion.Task.WaitAsync(cancellation);

    public TaskAwaiter<SceneSection> GetAwaiter() => _completion.Task.GetAwaiter();

    /// <summary>Moves the load on as far as it can go now. True once it is ready to activate.</summary>
    internal bool Advance()
    {
        if (Phase == SceneLoadPhase.Preloading)
        {
            if (_group is { IsDone: false }) return false;
            if (Scene.IsMissing || Scene.State is AssetState.Failed or AssetState.Missing)
            {
                Fail(new SceneLoadException($"The scene '{Scene.Name}' is missing or could not be read."));
                return false;
            }
            Phase = SceneLoadPhase.Instantiating;
        }

        if (Phase == SceneLoadPhase.Instantiating)
        {
            try
            {
                if (Scene.Data == null) throw new SceneLoadException($"The scene '{Scene.Name}' holds no stored content.");
                _content = Scene.ReadContent();
            }
            catch (Exception e)
            {
                Fail(e as SceneLoadException ?? new SceneLoadException($"Instantiating '{Scene.Name}' threw.", e));
                return false;
            }
            Phase = SceneLoadPhase.WaitingForActivation;
        }

        if (Phase == SceneLoadPhase.WaitingForActivation && AllowActivation)
            Phase = SceneLoadPhase.Activating;
        return Phase == SceneLoadPhase.Activating;
    }

    internal SceneContent TakeContent()
    {
        SceneContent content = _content ?? throw new InvalidOperationException("The load has no content to activate.");
        _content = null;
        return content;
    }

    internal void Complete(SceneSection section)
    {
        if (IsFinished) return;
        Section = section;
        Phase = SceneLoadPhase.Done;
        Release();
        _completion.TrySetResult(section);
    }

    internal void Fail(Exception error)
    {
        if (IsFinished) return;
        Error = error;
        Phase = SceneLoadPhase.Failed;
        Release();
        _completion.TrySetException(error);
        Debug.LogException($"[Scene] Loading '{Scene.Name}' failed", error);
    }

    private void Release()
    {
        _group?.Dispose();
        _group = null;
        _content?.DisposeObjects();
        _content = null;
    }

    public override string ToString() => $"{Kind} {Scene.Name} ({Phase})";
}

/// <summary>Loading a scene through a soft reference, as through the <see cref="Scene"/> it names.</summary>
public static class SceneReferenceExtensions
{
    public static SceneLoad Load(this AssetRef<Scene> scene, SceneInstance? into = null, LoadOptions options = default) => Resolve(scene).Load(into, options);

    public static SceneLoad LoadAdditive(this AssetRef<Scene> scene, SceneInstance? into = null, LoadOptions options = default) => Resolve(scene).LoadAdditive(into, options);

    public static SceneLoad Open(this AssetRef<Scene> scene, SceneMode mode = SceneMode.Game, LoadOptions options = default) => Resolve(scene).Open(mode, options);

    private static Scene Resolve(AssetRef<Scene> scene)
    {
        Scene? found = AssetDatabase.Get<Scene>(scene.AssetID);
        return found.IsValid() ? found! : throw new SceneLoadException($"No scene is stored under {scene.AssetID}.");
    }
}

/// <summary>The scenes a build ships, in build order, and lookups by name.</summary>
public static class SceneBuild
{
    private static Scene[] s_list = [];

    /// <summary>The scenes in the build, in order. The player sets it from its manifest, the editor from the build settings.</summary>
    public static IReadOnlyList<Scene> List => s_list;

    internal static void Set(IEnumerable<Scene> scenes) => s_list = [.. scenes];

    /// <summary>The scene in the build list with this name, or whose path ends with it.</summary>
    public static Scene? Find(string nameOrPath)
    {
        foreach (Scene scene in s_list)
            if (string.Equals(scene.Name, nameOrPath, StringComparison.OrdinalIgnoreCase))
                return scene;

        string wanted = WithoutExtension(nameOrPath);
        foreach (Scene scene in s_list)
        {
            if (AssetDatabase.GetAssetPath(scene.AssetID) is not { } path) continue;
            string stored = WithoutExtension(path);
            if (stored.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || stored.EndsWith("/" + wanted, StringComparison.OrdinalIgnoreCase))
                return scene;
        }
        return null;
    }

    private static string WithoutExtension(string path)
    {
        path = path.Replace('\\', '/');
        return path.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ? path[..^".scene".Length] : path;
    }

    /// <summary>Where a scene is in the build list, -1 when it is not in it.</summary>
    public static int IndexOf(Scene scene) => Array.IndexOf(s_list, scene);
}
