// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A scene as stored in a .scene file. Holding one keeps only its stored data loaded, not what the scene uses:
/// <see cref="SceneInstance.Load(Scene)"/> and <see cref="AssetDatabase.Preload"/> load that.
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
    }
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

/// <summary>A stored scene loading in the background, made current at the end of the frame it finishes on.</summary>
public sealed class SceneLoad
{
    private readonly Scene _asset;
    private readonly AssetGroup _group;
    private readonly TaskCompletionSource<SceneInstance?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _allowed = true;

    internal SceneLoad(Scene asset)
    {
        _asset = asset;
        _group = AssetDatabase.Preload(asset);
    }

    public float Progress => _group.Progress;
    public bool IsDone => _completion.Task.IsCompleted;
    public SceneInstance? Scene { get; private set; }
    public System.Runtime.CompilerServices.TaskAwaiter<SceneInstance?> GetAwaiter() => _completion.Task.GetAwaiter();

    /// <summary>Keeps a finished load waiting until <see cref="Allow"/>, for a "press any key" screen.</summary>
    public bool WaitForActivation
    {
        get => !_allowed;
        set => _allowed = !value;
    }

    public void Allow() => _allowed = true;

    internal bool IsReady => _allowed && _group.IsDone;

    internal SceneInstance Activate()
    {
        Scene = SceneInstance.Instantiate(_asset);
        _group.Dispose();
        _completion.TrySetResult(Scene);
        return Scene;
    }

    internal void Cancel()
    {
        _group.Dispose();
        _completion.TrySetResult(null);
    }
}
