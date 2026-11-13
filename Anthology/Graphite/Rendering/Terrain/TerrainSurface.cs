using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Draws a <see cref="TerrainData"/> by CDLOD: a quadtree of nodes, each a quarter of a page drawn as one grid, walked
/// per view. A node splits while its children's range reaches it, and where only some children are in range the rest of
/// it draws as quadrants at its own level. Toward the edge of a level's range vertices morph onto the next level's grid,
/// so levels meet without cracks or pops. Pages stream into GPU pools around the viewers, see <see cref="TerrainPool"/>,
/// and a node only splits into children whose pages are in. Adding it to a
/// world's providers adds its upload feature to the world's frame features. The material is a shader with Surface
/// "Terrain", see Graphite/Standard/Terrain.shader.
/// </summary>
public sealed class TerrainSurface : RenderProvider, IDisposable
{
    public const int NodeQuads = TerrainPage.Quads / 2;

    private readonly Renderer _renderer;
    private readonly GpuMesh _node, _quadrant;
    private readonly TerrainPool _pool;
    private readonly PagesFeature _pages;
    private readonly CacheFeature _cacheFeature;
    private readonly FarShadowFeature _farFeature;
    private readonly GrassFeature _grassFeature;
    private TerrainGrass? _grass;
    private TerrainGrass.Frame? _grassFrame;
    private GpuMesh? _card;
    private Material? _cardMaterial;
    private IReadOnlyList<TerrainGrassType> _grassTypes = [];
    private bool _grassChanged;
    private TerrainFarShadow? _far;
    private readonly Renderer _owner;
    private TerrainCache? _cache;
    private TerrainCache.Frame? _cacheFrame;
    private RenderWorld? _world;
    private readonly Material? _ownMaterial;
    private readonly Dictionary<RenderView, CullFrustum> _seen = new();
    private readonly List<Double3> _viewers = new();
    private RenderView?[] _slotViews = [];
    private long[] _slotFrames = [];
    private long _frame;
    private volatile Dictionary<RenderView, int> _slots = new();
    private volatile TerrainPool.Snapshot _snapshot;
    private Material _material;
    private Material _holeMaterial = null!;
    private Double3 _position;
    private Double3? _viewer;
    private double _lodQuality = 2.0;
    private TerrainLayers? _layers;
    private int _drawn;

    public TerrainSurface(Renderer renderer, TerrainData data, Material? material = null, int poolPages = 512)
        : base(ViewKinds.Cameras | ViewKinds.Captures | ViewKinds.Shadow | ViewKinds.Voxelization)
    {
        _renderer = renderer;
        Data = data;
        TerrainHeader header = data.Header;

        _pool = new TerrainPool(renderer.Device, data, poolPages);
        _snapshot = _pool.Current;
        _pages = new PagesFeature(this);
        _cacheFeature = new CacheFeature(this);
        _farFeature = new FarShadowFeature(this);
        _grassFeature = new GrassFeature(this);
        _owner = renderer;

        float reach = (float)Math.Max(Math.Abs(header.Bottom), Math.Abs(header.Bottom + header.Range)) + 1f;
        _node = renderer.CreateMesh(Grid(NodeQuads, reach));
        _quadrant = renderer.CreateMesh(Grid(NodeQuads / 2, reach));

        if (material == null)
        {
            Shader shader = renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Terrain/Default.shader")!));
            _ownMaterial = renderer.CreateMaterial(shader);
            material = _ownMaterial;
        }
        _material = material;
        Bind(material);
        data.Edited += OnEdited;
    }

    // Edited pages upload again where resident, refilling the cache over them, and the far shadow rebuilds
    private void OnEdited(TerrainEdited edited)
    {
        _pool.Refresh(edited.Pages);
        _far?.Invalidate();
    }

    public TerrainData Data { get; }

    /// <summary>The material the nodes draw with, a shader with Surface "Terrain".</summary>
    public Material Material
    {
        get => _material;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _material = value;
            Bind(value);
        }
    }

    /// <summary>A level's range over its nodes' width. Higher keeps finer levels further out. At least 1.5.</summary>
    public double LodQuality { get; set; } = 2.0;

    /// <summary>
    /// Where shadow and capture views measure their levels from, usually the main camera. Shadow views then draw the
    /// very nodes the camera does, so the ground never shadows itself. Null for each view's own position.
    /// </summary>
    public Double3? Viewer { get; set; }

    /// <summary>The material layers the control pages pick from, or null for plain ground.</summary>
    public TerrainLayers? Layers
    {
        get => _layers;
        set
        {
            _layers = value;
            Bind(_material);
        }
    }

    /// <summary>
    /// Whether the material cache is used once there are layers: levels of <see cref="CacheSize"/> texels a side, the
    /// finest <see cref="CacheTexel"/> metres a texel. Off, every pixel blends the layers itself.
    /// </summary>
    public bool UseCache { get; set; } = true;
    public int CacheLevels { get; init; } = 8;

    /// <summary>Camera views that each get their own cache. Further views blend the layers themselves.</summary>
    public int CacheViews { get; init; } = 2;

    /// <summary>The metres within which the strongest layer's normal map adds detail and steep ground blends its layers directly.</summary>
    public float DetailDistance { get; set; } = 8f;
    public int CacheSize { get; init; } = 1024;
    public double CacheTexel { get; init; } = 0.02;

    /// <summary>Texels the cache fills a frame at most.</summary>
    public int CacheBudget { get; set; } = 1 << 19;

    /// <summary>
    /// Whether the terrain shadows itself and everything on it from the sun far past the cascades, as its world's
    /// <see cref="RenderWorld.FarShadow"/> unless the host set another: <see cref="FarShadowCoverage"/> metres square around
    /// the viewer at <see cref="FarShadowResolution"/> texels a side, marching <see cref="FarShadowReach"/> metres toward
    /// the sun. It fades in from four fifths of the sun's cascade distance.
    /// </summary>
    public bool CastFarShadow { get; set; }
    public int FarShadowResolution { get; init; } = 1024;
    public double FarShadowCoverage { get; set; } = 8000;
    public double FarShadowReach { get; set; } = 3000;

    /// <summary>The kinds of grass the layers grow, see <see cref="TerrainLayerDesc.Grass"/>. Needs <see cref="Layers"/>.</summary>
    public IReadOnlyList<TerrainGrassType> GrassTypes
    {
        get => _grassTypes;
        set
        {
            _grassTypes = value ?? [];
            _grassChanged = true;
        }
    }

    /// <summary>The textures grass cards show, a slice each with alpha, sRGB colour multiplied by the type's colours.</summary>
    public Texture? GrassCards { get; set; }

    /// <summary>What the default material shows in place of the ground.</summary>
    public TerrainDebugView DebugView { get; set; }

    /// <summary>Nodes and quadrants the last camera view drew.</summary>
    public int Drawn => Volatile.Read(ref _drawn);

    /// <summary>Pages the pools hold.</summary>
    public int PoolPages => _pool.Capacity;

    /// <summary>The pages each frame may upload.</summary>
    public int UploadsPerFrame
    {
        get => _pool.UploadsPerFrame;
        set => _pool.UploadsPerFrame = Math.Max(value, 1);
    }

    public TerrainStreamingStats Streaming => _pool.Stats;

    public override void Extract()
    {
        _position = Data.Position;
        _viewer = Viewer;
        _lodQuality = Math.Max(LodQuality, 1.5);
        _ownMaterial?.SetFloat("_TerrainDebug", (float)DebugView);

        _frame++;
        _viewers.Clear();
        Dictionary<RenderView, CullFrustum> seen;
        lock (_seen)
        {
            seen = new Dictionary<RenderView, CullFrustum>(_seen);
            _seen.Clear();
        }
        foreach (CullFrustum frustum in seen.Values)
            _viewers.Add(frustum.Camera);
        if (Viewer is { } viewer)
            _viewers.Add(viewer);
        double quality = _lodQuality;
        _pool.Update(_viewers, level => NodeQuads * Data.SpacingAt(level) * quality);
        _snapshot = _pool.Current;

        if (_slotViews.Length == 0)
        {
            _slotViews = new RenderView?[Math.Max(CacheViews, 1)];
            _slotFrames = new long[_slotViews.Length];
        }
        (Double2?[] slotViewers, CullFrustum?[] slotFrustums) = AssignSlots(seen);

        bool cached = UseCache && _layers != null;
        if (cached && _cache == null)
        {
            _cache = new TerrainCache(_owner, CacheLevels, CacheSize, CacheTexel, _slotViews.Length);
            Bind(_material);
            _world?.FrameFeatures.Add(_cacheFeature);
        }
        _material.SetFloat4("_TerrainCacheParams", new Float4(CacheLevels, CacheSize, cached ? 1f : 0f, MathF.Max(DetailDistance, 0.01f)));
        if (cached)
        {
            _cache!.Budget = Math.Max(CacheBudget, 1024);
            _cache.Update(slotViewers, Data, _snapshot.Arrived);
        }
        _cacheFrame = cached ? _cache?.Current : null;
        UpdateGrass(slotFrustums);
        UpdateFarShadow();
    }

    private void UpdateGrass(CullFrustum?[] frustums)
    {
        bool grow = _grassTypes.Count > 0 && _layers != null;
        if (grow && _grass == null)
        {
            _grass = new TerrainGrass(_owner, _slotViews.Length);
            _card = _owner.CreateMesh(CardMesh());
            Shader shader = _owner.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Terrain/Grass.shader")!));
            _cardMaterial = _owner.CreateMaterial(shader);
            _cardMaterial.Surface = new SurfaceSettings(SurfaceType.Cutout, Cull: SurfaceCull.Off);
            _world?.FrameFeatures.Add(_grassFeature);
            _grassChanged = true;
        }
        if (_grass == null)
            return;
        if (_grassChanged)
        {
            _grass.SetTypes(_grassTypes);
            _grassChanged = false;
        }
        _cardMaterial!.SetTexture("_GrassInstances", _grass.Instances, SamplerDescription.Point);
        _cardMaterial.SetTexture("_GrassTypes", _grass.Types, SamplerDescription.Point);
        if (GrassCards != null)
            _cardMaterial.SetTexture("_GrassCards", GrassCards, SamplerDescription.Aniso4x);
        _grass.Update(grow ? frustums : new CullFrustum?[frustums.Length], _grassTypes, Data, _position);
        _grassFrame = grow ? _grass.Current : null;
    }

    // Two crossed quads, x across from -0.5 to 0.5, y up from 0 to 1 and z picking the second
    private static MeshData CardMesh()
    {
        Float3[] positions = new Float3[8];
        List<uint> indices = new();
        for (int plane = 0; plane < 2; plane++)
        {
            uint first = (uint)(plane * 4);
            positions[first] = new Float3(-0.5f, 0f, plane);
            positions[first + 1] = new Float3(0.5f, 0f, plane);
            positions[first + 2] = new Float3(-0.5f, 1f, plane);
            positions[first + 3] = new Float3(0.5f, 1f, plane);
            indices.AddRange([first, first + 2, first + 1, first + 1, first + 2, first + 3]);
        }
        return new MeshData
        {
            VertexCount = (uint)positions.Length,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = indices.ToArray(),
            Displacement = new Float3(200f, 200f, 200f),
        };
    }

    private void UpdateFarShadow()
    {
        RenderWorld? world = _world;
        bool cast = CastFarShadow && world != null && world.Environment.Sun.IsValid && world.IsAlive(world.Environment.Sun);
        if (!cast)
        {
            if (_far != null && world != null && world.FarShadow == _far.Map)
                world.FarShadow = null;
            if (_far != null)
                _far.Map.Enabled = false;
            return;
        }
        if (_far == null)
        {
            _far = new TerrainFarShadow(_owner, Math.Max(FarShadowResolution, 64));
            world!.FrameFeatures.Add(_farFeature);
        }
        if (world!.FarShadow == null)
            world.FarShadow = _far.Map;
        if (_viewers.Count == 0 && Viewer == null)
            return;

        LightDesc sun = world.GetLight(world.Environment.Sun);
        Double3 viewer = Viewer ?? _viewers[0];
        float fade = sun.Shadow.Enabled ? sun.Shadow.Distance * 0.8f : 0f;
        _far.Update(new Double2(viewer.X - _position.X, viewer.Z - _position.Z), sun.Direction, Math.Max(FarShadowCoverage, 1), Math.Max(FarShadowReach, 1), Data, fade);
    }

    // Keeps each seen camera view in its cache slot, giving new ones a slot no view has used for a second of frames,
    // and returns each slot's viewer this frame from the terrain's corner, null for slots not seen
    private (Double2?[] Viewers, CullFrustum?[] Frustums) AssignSlots(Dictionary<RenderView, CullFrustum> seen)
    {
        Double2?[] viewers = new Double2?[_slotViews.Length];
        CullFrustum?[] frustums = new CullFrustum?[_slotViews.Length];
        Dictionary<RenderView, int> slots = new();
        foreach ((RenderView view, CullFrustum frustum) in seen)
        {
            int slot = Array.IndexOf(_slotViews, view);
            if (slot < 0)
            {
                for (int i = 0; i < _slotViews.Length && slot < 0; i++)
                {
                    if (_slotViews[i] == null || _frame - _slotFrames[i] > 60)
                        slot = i;
                }
                if (slot < 0)
                    continue;
                _slotViews[slot] = view;
                _cache?.Reset(slot);
            }
            _slotFrames[slot] = _frame;
            slots[view] = slot;
            viewers[slot] = new Double2(frustum.Camera.X - _position.X, frustum.Camera.Z - _position.Z);
            frustums[slot] = frustum;
        }
        _slots = slots;
        return (viewers, frustums);
    }

    internal override void Attach(object set)
    {
        base.Attach(set);
        _world = ((ExtensionSet<RenderProvider>)set).World;
        _world?.FrameFeatures.Add(_pages);
        if (_cache != null)
            _world?.FrameFeatures.Add(_cacheFeature);
        if (_far != null)
            _world?.FrameFeatures.Add(_farFeature);
        if (_grass != null)
            _world?.FrameFeatures.Add(_grassFeature);
    }

    internal override void Detach(object set)
    {
        _world?.FrameFeatures.Remove(_pages);
        _world?.FrameFeatures.Remove(_cacheFeature);
        _world?.FrameFeatures.Remove(_farFeature);
        _world?.FrameFeatures.Remove(_grassFeature);
        if (_far != null && _world?.FarShadow == _far.Map)
            _world.FarShadow = null;
        _world = null;
        base.Detach(set);
    }

    public override void Cull(in ProviderView view, DrawItemSink sink)
    {
        ViewKinds kind = (ViewKinds)(1 << (int)view.View.Kind);
        bool camera = (kind & ViewKinds.Cameras) != 0;
        double scale = (kind & ViewKinds.Captures) != 0 ? 0.25 : 1.0;
        RenderView? owner = view.View.RenderView;
        int slot = camera && owner != null && _slots.TryGetValue(owner, out int found) ? found : -1;
        Walk walk = new(this, _snapshot, view, sink, camera ? view.Camera : _viewer ?? view.Camera, _lodQuality * scale, slot);
        if (camera && owner != null)
        {
            lock (_seen)
                _seen[owner] = view.Frustum;
        }

        int top = Data.Levels - 1;
        (int pagesX, int pagesZ) = Data.PagesAt(top);
        for (int nz = 0; nz < pagesZ * 2; nz++)
            for (int nx = 0; nx < pagesX * 2; nx++)
            {
                if (walk.Exists(top, nx, nz) && !walk.Select(top, nx, nz))
                    walk.Add(top, nx, nz, -1);
            }
        if (camera)
            Volatile.Write(ref _drawn, walk.Count);
        if (slot >= 0 && _grassFrame is { Any: true } grass)
            AddGrass(sink, slot, grass);
    }

    private void AddGrass(DrawItemSink sink, int slot, TerrainGrass.Frame grass)
    {
        DrawItem item = new()
        {
            Transform = Double4x4.CreateTranslation(grass.Origins[slot]),
            Flags = RenderableFlags.ReceiveShadows,
            Custom0 = new Float4(slot * TerrainGrass.Rows, 0, 0, 0),
        };
        sink.AddIndirect(_card!, 0, _cardMaterial!, item, TerrainGrass.CountsName, (uint)(slot * 4));
    }

    private ref struct Walk
    {
        private readonly TerrainSurface _surface;
        private readonly Dictionary<TerrainPageKey, int> _resident;
        private readonly HashSet<TerrainPageKey> _holes;
        private readonly ProviderView _view;
        private readonly DrawItemSink _sink;
        private readonly Double3 _viewer;
        private readonly double _quality;
        private readonly int _slot;
        public int Count;

        public Walk(TerrainSurface surface, TerrainPool.Snapshot snapshot, in ProviderView view, DrawItemSink sink, Double3 viewer, double quality, int slot)
        {
            _slot = slot;
            _surface = surface;
            _resident = snapshot.Pages;
            _holes = snapshot.Holes;
            _view = view;
            _sink = sink;
            _viewer = viewer;
            _quality = quality;
        }

        private readonly TerrainData Data => _surface.Data;

        private readonly double NodeSize(int level) => NodeQuads * Data.SpacingAt(level);

        public readonly double Range(int level) => NodeSize(level) * _quality;

        public readonly bool Exists(int level, int nx, int nz)
        {
            double size = NodeSize(level);
            return nx * size < Data.SizeX && nz * size < Data.SizeZ;
        }

        // The node's box: its corner in world space, its width, and its lowest and highest ground
        private readonly bool Box(int level, int nx, int nz, out Double3 min, out Double3 max)
        {
            double size = NodeSize(level);
            TerrainPageKey page = new(level, nx >> 1, nz >> 1);
            bool known = Data.TryGetQuarterBounds(page, (nx & 1) + 2 * (nz & 1), out double low, out double high);
            Double3 origin = _surface._position;
            min = new Double3(origin.X + nx * size, low, origin.Z + nz * size);
            max = new Double3(min.X + size, high, min.Z + size);
            return known;
        }

        private readonly bool Within(in Double3 min, in Double3 max, double range)
        {
            double dx = Math.Max(Math.Max(min.X - _viewer.X, _viewer.X - max.X), 0);
            double dy = Math.Max(Math.Max(min.Y - _viewer.Y, _viewer.Y - max.Y), 0);
            double dz = Math.Max(Math.Max(min.Z - _viewer.Z, _viewer.Z - max.Z), 0);
            return dx * dx + dy * dy + dz * dz < range * range;
        }

        // False when the node lies wholly out of its level's range or its page is not in yet, for its parent to draw
        public bool Select(int level, int nx, int nz)
        {
            if (!_resident.ContainsKey(new TerrainPageKey(level, nx >> 1, nz >> 1)))
                return false;
            if (!Box(level, nx, nz, out Double3 min, out Double3 max) || !Within(min, max, Range(level)))
                return false;
            Double3 center = (min + max) * 0.5;
            if (!_view.Sees(center, (float)Double3.Length(max - center)))
                return true;
            if (level == 0 || !Within(min, max, Range(level - 1)))
            {
                Add(level, nx, nz, -1);
                return true;
            }
            for (int q = 0; q < 4; q++)
            {
                int cx = nx * 2 + (q & 1), cz = nz * 2 + (q >> 1);
                if (Exists(level - 1, cx, cz) && !Select(level - 1, cx, cz) && Box(level - 1, cx, cz, out Double3 low, out Double3 high))
                {
                    Double3 middle = (low + high) * 0.5;
                    if (_view.Sees(middle, (float)Double3.Length(high - middle)))
                        Add(level, nx, nz, q);
                }
            }
            return true;
        }

        // Draws the whole node, or with quadrant 0 to 3 only that quarter of it
        public void Add(int level, int nx, int nz, int quadrant)
        {
            TerrainPageKey page = new(level, nx >> 1, nz >> 1);
            if (!_resident.TryGetValue(page, out int slice))
                return;
            double spacing = Data.SpacingAt(level);
            int half = NodeQuads / 2;
            int sx = nx * NodeQuads + (quadrant < 0 ? 0 : (quadrant & 1) * half);
            int sz = nz * NodeQuads + (quadrant < 0 ? 0 : (quadrant >> 1) * half);
            Double3 corner = _surface._position + new Double3(sx * spacing, 0, sz * spacing);
            TerrainPageKey parent = new(level + 1, sx / 2 / TerrainPage.Quads, sz / 2 / TerrainPage.Quads);
            float parentSlice = level + 1 < Data.Levels && _resident.TryGetValue(parent, out int found) ? found : -1f;

            // Morph from where the finer level's nodes can still reach to the edge of this level's range
            double end = Range(level);
            double start = Math.Min(end * (0.5 + 0.75 / _quality), end * 0.9);
            DrawItem item = new()
            {
                Transform = Double4x4.CreateTranslation(corner) * Double4x4.CreateScale(spacing, 1, spacing),
                Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows,
                Custom0 = new Float4(slice, _slot, 0, level),
                Custom1 = new Float4((float)start, (float)(1.0 / (end - start)), sx, sz),
                Color = new Float4((float)(_viewer.X - corner.X), (float)(_viewer.Y - corner.Y), (float)(_viewer.Z - corner.Z), parentSlice),
            };
            _sink.Add(quadrant < 0 ? _surface._node : _surface._quadrant, 0, _holes.Contains(page) ? _surface._holeMaterial : _surface._material, item);
            Count++;
        }
    }

    private void Bind(Material material)
    {
        if (_holeMaterial == null || _holeMaterial.Parent != material)
        {
            _holeMaterial?.Dispose();
            _holeMaterial = material.CreateChild();
            _holeMaterial.Set(material.Shader.Axis("TERRAIN_HOLES"), true);
        }
        TerrainHeader header = Data.Header;
        material.SetTexture("_TerrainHeights", _pool.Heights);
        material.SetTexture("_TerrainNormals", _pool.Normals);
        material.SetFloat4("_TerrainParams", new Float4((float)header.Bottom, (float)header.Range, header.QuadsX, header.QuadsZ));
        material.SetFloat4("_TerrainSpacing", new Float4((float)header.Spacing, _layers?.Count ?? 0, 0, 0));
        material.SetTexture("_TerrainControl", _pool.Control);
        if (_layers != null)
        {
            material.SetTexture("_TerrainAlbedo", _layers.Albedo, SamplerDescription.Aniso4x);
            material.SetTexture("_TerrainSurface", _layers.Surface, SamplerDescription.Aniso4x);
            material.SetTexture("_TerrainLayerData", _layers.Data, SamplerDescription.Point);
        }
        if (_cache != null)
        {
            material.SetTexture("_TerrainCacheAlbedo", _cache.Albedo, SamplerDescription.Linear);
            material.SetTexture("_TerrainCacheSurface", _cache.Surface, SamplerDescription.Linear);
            material.SetTexture("_TerrainCacheLevels", _cache.Data, SamplerDescription.Point);
        }
    }

    // A grid of quads, each split from its lowest x and z corner to its highest as TerrainTriangles does
    private static MeshData Grid(int quads, float reach)
    {
        int side = quads + 1;
        Float3[] positions = new Float3[side * side];
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                positions[z * side + x] = new Float3(x, 0f, z);
        uint[] indices = new uint[quads * quads * 6];
        int n = 0;
        for (int z = 0; z < quads; z++)
            for (int x = 0; x < quads; x++)
            {
                uint a = (uint)(z * side + x), b = a + 1, c = a + (uint)side, d = c + 1;
                indices[n++] = a; indices[n++] = c; indices[n++] = d;
                indices[n++] = a; indices[n++] = d; indices[n++] = b;
            }
        return new MeshData
        {
            VertexCount = (uint)positions.Length,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = indices,
            Displacement = new Float3(0f, reach, 0f),
        };
    }

    public void Dispose()
    {
        Data.Edited -= OnEdited;
        _node.Dispose();
        _quadrant.Dispose();
        _holeMaterial.Dispose();
        _ownMaterial?.Dispose();
        _pool.Dispose();
        _cache?.Dispose();
        _far?.Dispose();
        _grass?.Dispose();
        _card?.Dispose();
        _cardMaterial?.Dispose();
    }

    // Uploads the frame's new pages into the pools before the views draw, and hands the pools to the draws
    private sealed class PagesFeature(TerrainSurface owner) : FrameFeature(FrameSlot.BeforeViews, order: -5)
    {
        public override void Setup(FrameBuilder builder)
        {
            builder.ExportToDraws(builder.Write(owner._pool.Heights, TextureState.TransferDst));
            builder.ExportToDraws(builder.Write(owner._pool.Normals, TextureState.TransferDst));
            builder.ExportToDraws(builder.Write(owner._pool.Control, TextureState.TransferDst));
            builder.Write(owner._pool.PageTable, TextureState.TransferDst);
        }

        public override void Render(FrameContext context) => owner._pool.Upload(context.Command, owner._snapshot);
    }

    // Grows the frame's grass after the frame's pages are in, and hands the instances and counts to the draws
    private sealed class GrassFeature(TerrainSurface owner) : FrameFeature(FrameSlot.BeforeViews, order: 60)
    {
        public override bool IsActive() => owner._grass != null && owner._layers != null;

        public override void Setup(FrameBuilder builder)
        {
            builder.Read(owner._pool.Heights);
            builder.Read(owner._pool.Control);
            builder.Read(owner._pool.PageTable);
            owner._grass!.Setup(builder);
        }

        public override void Render(FrameContext context)
        {
            TerrainGrass grass = owner._grass!;
            grass.Render(context, owner._grassFrame ?? grass.Current, owner._pool, owner._layers!);
        }
    }

    // Fills the frame's rows of the far shadow being built, after the frame's pages are in
    private sealed class FarShadowFeature(TerrainSurface owner) : FrameFeature(FrameSlot.BeforeViews, order: 50)
    {
        public override bool IsActive() => owner._far is { Rows: > 0 };

        public override void Setup(FrameBuilder builder)
        {
            builder.Read(owner._pool.Heights);
            builder.Read(owner._pool.PageTable);
            builder.Write(owner._far!.Back, TextureState.Storage);
        }

        public override void Render(FrameContext context) => owner._far!.Render(context.Command, owner._pool);
    }

    // Fills the material cache from the pools after the frame's pages are in, and hands it to the draws
    private sealed class CacheFeature(TerrainSurface owner) : FrameFeature(FrameSlot.BeforeViews, order: -4)
    {
        public override bool IsActive() => owner._cache != null && owner._cacheFrame != null;

        public override void Setup(FrameBuilder builder)
        {
            TerrainCache cache = owner._cache!;
            builder.Read(owner._pool.Heights);
            builder.Read(owner._pool.Normals);
            builder.Read(owner._pool.Control);
            builder.Read(owner._pool.PageTable);
            builder.ExportToDraws(builder.Write(cache.Albedo, TextureState.Storage));
            builder.ExportToDraws(builder.Write(cache.Surface, TextureState.Storage));
            builder.ExportToDraws(builder.Write(cache.Data, TextureState.TransferDst));
        }

        public override void Render(FrameContext context)
            => owner._cache!.Render(context.Command, owner._cacheFrame!, owner._pool, owner.Data, owner._layers, owner._pool.Control);
    }
}

/// <summary>What <see cref="TerrainSurface.DebugView"/> shows on the default material.</summary>
public enum TerrainDebugView
{
    None,
    Levels,
    Layers,
    Weights,
}
