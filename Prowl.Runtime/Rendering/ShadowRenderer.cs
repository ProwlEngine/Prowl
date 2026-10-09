// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.Rendering;

/// <summary>What a shadow update needs to know about the camera it renders for.</summary>
public readonly struct ShadowCamera
{
    /// <summary>Identifies the camera, directional cascades are kept per camera.</summary>
    public readonly object Key;
    public readonly Float3 Position;
    public readonly Frustum Frustum;
    public readonly bool Orthographic;

    /// <summary>The projection's y scale: the cotangent of half the vertical field of view, or 2 / height for orthographic.</summary>
    public readonly float ProjectionScaleY;

    public readonly float PixelHeight;
    public readonly ShadowFitView FitView;

    public ShadowCamera(object key, Float3 position, Frustum frustum, Float4x4 projection, float pixelHeight, in ShadowFitView fitView)
    {
        Key = key;
        Position = position;
        Frustum = frustum;
        Orthographic = projection.c3.W > 0.5f;
        ProjectionScaleY = projection.c1.Y;
        PixelHeight = pixelHeight;
        FitView = fitView;
    }

    /// <summary>Radius on screen, in pixels, of a sphere. Effectively unbounded once the camera is inside it.</summary>
    public float ScreenRadius(Float3 center, float radius)
    {
        float halfHeight = PixelHeight * 0.5f;
        if (Orthographic) return radius * ProjectionScaleY * halfHeight;

        float distSq = Float3.LengthSquared(center - Position);
        float inner = distSq - radius * radius;
        if (inner <= radius * radius * 0.1f) return float.MaxValue;
        return radius / MathF.Sqrt(inner) * ProjectionScaleY * halfHeight;
    }
}

/// <summary>
/// Produces every shadow map a scene needs, and keeps them between frames.
/// <para>
/// Each shadowed light owns tiles in <see cref="ShadowAtlas"/> that stay put across frames. A light is only redrawn
/// when its tiles are new, the light itself moved or changed, or the casters inside it changed. Casters are
/// compared by a hash of their model matrix, mesh, mesh version, material state and instance data, so nothing has
/// to report changes. Renderables that deform without any of those changing (skinning, or a shadow caster pass
/// tagged <c>"ShadowUpdate" = "EveryFrame"</c> for vertex animation) count as changed every frame.
/// </para>
/// <para>
/// Point and spot light tiles are sized from how big the light is on screen, see
/// <see cref="ShadowAtlas.TexelsPerPixel"/>. When they would not all fit, every light scales down together, and the
/// scale only climbs back once it would have fit for a while. A light keeps its tile until its wanted size moves
/// well past it, so small camera moves redraw nothing, and size changes are paced by
/// <see cref="ShadowAtlas.MaxResolutionChangesPerFrame"/>.
/// </para>
/// <para>
/// A point or spot light with both static and moving casters in range keeps a second set of tiles holding only its
/// static casters (<see cref="IRenderable.IsStatic"/>). When only moving casters changed, that layer is copied in and
/// just the moving ones are drawn on top, so a character walking past a lamp does not redraw the room.
/// </para>
/// <para>
/// Directional cascades are not cached: they follow the camera, and something in view is nearly always moving, so
/// checking would cost more than it saves. Cascade c simply redraws every c + 1 frames, keeping its tile per camera.
/// </para>
/// <para>
/// Casters drawn with an opaque Standard shader only need depth, so they skip their own materials and are drawn as
/// one instanced batch per mesh through a shared depth material. Everything else draws as itself.
/// </para>
/// </summary>
internal sealed class ShadowRenderer : IDisposable
{
    private const float ScaleStep = 1.41421356f;
    private const int FramesBeforeScaleRises = 30;
    private const float ResolutionHysteresis = 0.75f; // in powers of two
    private const float FitHeadroom = 0.8f;
    private const int ForgetAfterFrames = 600;

    private const byte MovingCaster = 1;
    private const byte EveryFrameCaster = 2;
    private const byte StaticCaster = 3;

    private sealed class LocalEntry
    {
        public Light Light = null!;
        public int Faces;
        public readonly ShadowTile[] Tiles = new ShadowTile[6];
        public readonly Float4x4[] Matrices = new Float4x4[6];
        public int TileSize;
        public bool ContentValid;
        public ulong LightKey;
        public ulong Signature;
        public int Generation;
        public long LastUsedFrame;
        public int SeenStamp;
        public int DataSlot = -1;
        public int ActiveSlot = -1;

        // The static casters alone, copied under the moving ones while both are in range
        public readonly ShadowTile[] StaticTiles = new ShadowTile[6];
        public bool StaticValid;
        public ulong StaticSignature;
        public ulong DynamicSignature;

        // This frame
        public bool Visible;
        public float Priority;
        public float Wanted;  // tile size for the light's screen size, before the global scale
        public float Desired; // after it
        public int MaxTile;
        public int Target;
        public float Fade;
        public bool NeedsDraw;
        public bool Split;
        public readonly List<int> Casters = new();

        public int Layers => Split ? 2 : 1;
    }

    private sealed class CascadeSet
    {
        public long LastUsedFrame;
        public int Count;
        public int Generation;
        public ulong LightKey;
        public readonly ShadowTile[] Tiles = new ShadowTile[4];
        public readonly bool[] Valid = new bool[4];
        public readonly Float4x4[] Matrices = new Float4x4[4];
        public readonly Float4[] AtlasParams = new Float4[4];
        public readonly Float4[] Spheres = new Float4[4];
    }

    private readonly Dictionary<Light, LocalEntry> _local = new(ReferenceEqualityComparer.Instance);
    private readonly List<LocalEntry> _visible = new();
    private readonly Dictionary<object, CascadeSet> _cascades = new();
    private readonly ShadowDataBlocks _data = new();
    private readonly CasterGrid _grid = new();
    private readonly Dictionary<Shader, byte> _shaderKinds = new();
    private readonly List<int> _staticCasters = new();
    private readonly List<int> _dynamicCasters = new();
    private readonly List<LocalEntry> _toForget = new();
    private readonly List<object> _cascadesToForget = new();

    private ulong[] _hash = [];
    private int[] _hashStamp = [];
    private byte[] _casterKind = []; // 0 casts nothing, then one of the Caster constants
    private int _stamp;
    private int _generation = -1;
    private int _scaleLevel;
    private int _framesAbove;
    private CascadeSet? _currentCascades;
    private DirectionalLight? _currentDirectional;

    /// <summary>Point light faces and spot lights drawn by the last update.</summary>
    public int FacesDrawn { get; private set; }

    /// <summary>Faces of the static only layer drawn by the last update.</summary>
    public int StaticFacesDrawn { get; private set; }

    /// <summary>Instanced batches the last update drew through the shared depth material.</summary>
    public int BatchedDraws { get; private set; }

    /// <summary>Casters the last update drew one by one with their own material.</summary>
    public int UnbatchedDraws { get; private set; }

    /// <summary>Directional cascades drawn by the last update.</summary>
    public int CascadesDrawn { get; private set; }

    /// <summary>Point and spot lights that sample a shadow after the last update.</summary>
    public int LightsShadowed { get; private set; }

    /// <summary>The scale every local light's wanted resolution is multiplied by, below 1 while the atlas is crowded.</summary>
    public float ResolutionScale => MathF.Pow(ScaleStep, -_scaleLevel);

    /// <summary>Uploads the shadow blocks that changed and binds them for every shader.</summary>
    internal void BindData(CommandBuffer cmd) => _data.Table.Bind(cmd);

    /// <summary>Slot of the light's shadow block for this render, -1 while it has no shadow.</summary>
    public int GetDataSlot(Light light) => _local.TryGetValue(light, out LocalEntry? e) ? e.ActiveSlot : -1;

    /// <summary>Tile size each face of the light holds, 0 for none.</summary>
    public int GetTileSize(Light light) => _local.TryGetValue(light, out LocalEntry? e) ? e.TileSize : 0;

    // ---------------------------------------------------------------- directional data for the uniforms

    public int CascadeCount => _currentCascades == null ? 0 : _currentCascades.Count;
    public ReadOnlySpan<Float4x4> CascadeMatrices => _currentCascades == null ? default : _currentCascades.Matrices;
    public ReadOnlySpan<Float4> CascadeAtlasParams => _currentCascades == null ? default : _currentCascades.AtlasParams;
    public ReadOnlySpan<Float4> CascadeSpheres => _currentCascades == null ? default : _currentCascades.Spheres;

    // ---------------------------------------------------------------- update

    /// <summary>
    /// Brings every shadow map this camera sees up to date, drawing only what changed, and writes the per light
    /// shadow data. <paramref name="localLights"/> are the point and spot lights that cast shadows.
    /// </summary>
    public void Update(RenderPipeline pipeline, in ShadowCamera camera, DirectionalLight? directional,
                       IReadOnlyList<Light> localLights, IReadOnlyList<IRenderable> renderables)
    {
        FacesDrawn = 0;
        StaticFacesDrawn = 0;
        BatchedDraws = 0;
        UnbatchedDraws = 0;
        CascadesDrawn = 0;
        LightsShadowed = 0;
        _stamp++;
        long frame = Time.FrameCount;

        ShadowAtlas.TryInitialize();
        if (ShadowAtlas.Generation != _generation)
        {
            // The atlas was rebuilt, so every tile is gone with it
            foreach (LocalEntry e in _local.Values) ForgetTiles(e);
            foreach (CascadeSet set in _cascades.Values) ForgetTiles(set);
            _generation = ShadowAtlas.Generation;
        }

        PrepareCasters(pipeline, renderables);

        // Plan everything, then allocate, then draw, so a compaction can still move anything
        _currentDirectional = directional is not null && directional.IsValid() && directional.DoCastShadows() ? directional : null;
        _currentCascades = _currentDirectional == null ? null : GetCascadeSet(camera.Key, frame);
        PlanLocal(camera, localLights, frame);
        AllocateAll(camera.Key, frame);

        if (_currentCascades != null)
            DrawCascades(pipeline, camera, renderables, frame);
        DrawLocal(pipeline, renderables);

        WriteData();
        Forget(frame);
    }

    // ---------------------------------------------------------------- casters

    private void PrepareCasters(RenderPipeline pipeline, IReadOnlyList<IRenderable> renderables)
    {
        int count = renderables.Count;
        if (_hash.Length < count)
        {
            int size = Math.Max(count, _hash.Length * 2);
            Array.Resize(ref _hash, size);
            Array.Resize(ref _hashStamp, size);
            Array.Resize(ref _casterKind, size);
        }

        for (int i = 0; i < count; i++)
            _casterKind[i] = CasterKind(renderables[i]);

        pipeline.EnsureWorldBounds(renderables);
        _grid.Build(pipeline.WorldBounds, pipeline.HasWorldBounds, _casterKind, count);
    }

    private byte CasterKind(IRenderable renderable)
    {
        Material material = renderable.GetMaterial();
        if (material is not { IsLoaded: true }) return 0;
        Shader shader = material.Shader;
        if (shader is not { IsLoaded: true }) return 0;

        if (!_shaderKinds.TryGetValue(shader, out byte kind))
        {
            foreach (Shaders.ShaderPass pass in shader.LoadedPasses)
            {
                if (!pass.HasTag("LightMode", "ShadowCaster")) continue;
                kind = Math.Max(kind, pass.HasTag("ShadowUpdate", "EveryFrame") ? EveryFrameCaster : MovingCaster);
            }
            _shaderKinds[shader] = kind;
        }

        if (kind != MovingCaster) return kind;
        if (renderable.DeformsEveryFrame || renderable is IProceduralInstanced) return EveryFrameCaster;
        return renderable.IsStatic ? StaticCaster : MovingCaster;
    }

    private ulong CasterHash(IReadOnlyList<IRenderable> renderables, int index)
    {
        if (_hashStamp[index] == _stamp) return _hash[index];

        // Skinning and the like change every frame anyway, so hashing their state would only cost time
        if (_casterKind[index] == EveryFrameCaster)
        {
            _hash[index] = PropertyState.Mix((ulong)Time.FrameCount * 0x9E3779B97F4A7C15UL + (ulong)index + 1);
            _hashStamp[index] = _stamp;
            return _hash[index];
        }

        IRenderable renderable = renderables[index];
        renderable.GetRenderingData(default, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instances);

        var hc = new HashCode();
        if (mesh is { IsLoaded: true })
        {
            hc.Add(RuntimeHelpers.GetHashCode(mesh));
            hc.Add(mesh.Version);
        }
        hc.Add(renderable.GetMaterial().GetStateHash());
        hc.Add(properties.ComputeHash());
        hc.Add(renderable.GetSubMeshIndex());
        hc.Add(renderable.VisualVersion);
        hc.AddBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref model, 1)));

        if (instances != null)
        {
            int live = Math.Min(renderable.GetInstanceCount(instances), instances.Length);
            hc.AddBytes(MemoryMarshal.AsBytes(instances.AsSpan(0, live)));
        }

        ulong hash = PropertyState.Mix((uint)hc.ToHashCode() * 0x9E3779B97F4A7C15UL + 1);
        _hash[index] = hash;
        _hashStamp[index] = _stamp;
        return hash;
    }

    // An order free combination, so the same casters always give the same signature, and the signature of all of
    // them can be built from the static and moving halves
    private ulong Sum(IReadOnlyList<IRenderable> renderables, List<int> casters)
    {
        ulong sum = 0;
        foreach (int i in casters)
            sum += CasterHash(renderables, i);
        return sum;
    }

    private static ulong Finish(ulong sum, int count) => PropertyState.Mix(sum + (ulong)count * 0x9E3779B97F4A7C15UL);

    private ulong Signature(IReadOnlyList<IRenderable> renderables, List<int> casters) =>
        Finish(Sum(renderables, casters), casters.Count);


    // ---------------------------------------------------------------- local planning

    private void PlanLocal(in ShadowCamera camera, IReadOnlyList<Light> lights, long frame)
    {
        _visible.Clear();
        float distance = Maths.Max(ShadowAtlas.LocalShadowDistance, 0.01f);
        float scale = ResolutionScale;
        int minTile = ShadowTileAllocator.RoundUp(Maths.Max(ShadowAtlas.MinTileSize, ShadowTileAllocator.SmallestTile));

        foreach (Light light in lights)
        {
            if (light.IsNotValid() || light is not (PointLight or SpotLight)) continue;
            int faces = light.GetLightType() == LightType.Point ? 6 : 1;
            if (_local.TryGetValue(light, out LocalEntry? e))
            {
                e.SeenStamp = _stamp;
                e.Visible = false;
                e.Faces = faces;
            }

            Float3 pos = light.GetLightPosition();
            float range = ShadowRange(light);
            float edge = Float3.Length(pos - camera.Position) - range;
            if (edge > distance || !camera.Frustum.Intersects(new Sphere(pos, range))) continue;

            // Only lights that have been in view hold an entry, so lights that never are cost nothing to keep
            if (e == null)
            {
                e = new LocalEntry { Light = light, SeenStamp = _stamp, Faces = faces };
                _local[light] = e;
            }

            int maxTile = Maths.Max(ShadowTileAllocator.RoundUp(light.MaxShadowResolution), minTile);
            float priority = camera.ScreenRadius(pos, range);

            e.Visible = true;
            e.LastUsedFrame = frame;
            e.Priority = priority;
            e.MaxTile = maxTile;
            e.Wanted = Maths.Clamp(Maths.Min(priority, 1e6f) * ShadowAtlas.TexelsPerPixel, minTile, maxTile);
            e.Desired = Maths.Clamp(e.Wanted * scale, minTile, maxTile);
            e.Fade = 1f - Maths.Smoothstep(distance * 0.9f, distance, Maths.Max(edge, 0f));
            e.Target = Hysteresis(e, minTile, maxTile);

            _grid.QuerySphere(pos, range, e.Casters);
            bool anyStatic = false, anyMoving = false;
            foreach (int i in e.Casters)
            {
                if (_casterKind[i] == StaticCaster) anyStatic = true;
                else anyMoving = true;
            }
            e.Split = anyStatic && anyMoving;
            _visible.Add(e);
        }

        _visible.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
        FitTargets(minTile);
        UpdateScale(minTile);
    }

    private static float ShadowRange(Light light) => light switch
    {
        PointLight p => Maths.Max(p.Range, 0.2f),
        SpotLight s => Maths.Max(s.Range, 0.2f),
        _ => 0f,
    };

    private static int RoundPow2(float size, int minTile, int maxTile)
    {
        int level = (int)MathF.Round(MathF.Log2(Maths.Max(size, 1f)));
        return Maths.Clamp(1 << Maths.Clamp(level, 0, 30), minTile, maxTile);
    }

    // A light keeps the size it holds until its wanted size is well past the next power of two either way
    private static int Hysteresis(LocalEntry e, int minTile, int maxTile)
    {
        int rounded = RoundPow2(e.Desired, minTile, maxTile);
        if (e.TileSize <= 0 || e.TileSize > maxTile || e.TileSize < minTile) return rounded;
        float apart = MathF.Abs(MathF.Log2(e.Desired) - MathF.Log2(e.TileSize));
        return apart < ResolutionHysteresis ? e.TileSize : rounded;
    }

    private long LocalCapacity()
    {
        long capacity = ShadowAtlas.Allocator.CapacityTexels;
        if (_currentCascades != null && _currentDirectional != null)
        {
            int res = _currentDirectional.ShadowMapResolution;
            capacity -= (long)res * res * (int)_currentDirectional.Cascades;
        }
        return (long)(Maths.Max(capacity, 0) * FitHeadroom);
    }

    private long TargetTexels()
    {
        long sum = 0;
        foreach (LocalEntry e in _visible)
            sum += (long)e.Faces * e.Layers * e.Target * e.Target;
        return sum;
    }

    // Sizes held above what is wanted give way first, then static layers, then the lowest priority lights halve,
    // then they drop
    private void FitTargets(int minTile)
    {
        long capacity = LocalCapacity();
        long sum = TargetTexels();
        if (sum <= capacity) return;

        for (int i = _visible.Count - 1; i >= 0 && sum > capacity; i--)
        {
            LocalEntry e = _visible[i];
            int rounded = RoundPow2(e.Desired, minTile, int.MaxValue);
            if (e.Target <= rounded) continue;
            sum -= (long)e.Faces * e.Layers * ((long)e.Target * e.Target - (long)rounded * rounded);
            e.Target = rounded;
        }

        for (int i = _visible.Count - 1; i >= 0 && sum > capacity; i--)
        {
            LocalEntry e = _visible[i];
            if (!e.Split) continue;
            sum -= (long)e.Faces * e.Target * e.Target;
            e.Split = false;
        }

        bool halved = true;
        while (sum > capacity && halved)
        {
            halved = false;
            for (int i = _visible.Count - 1; i >= 0 && sum > capacity; i--)
            {
                LocalEntry e = _visible[i];
                if (e.Target <= minTile) continue;
                int half = e.Target / 2;
                sum -= (long)e.Faces * e.Layers * ((long)e.Target * e.Target - (long)half * half);
                e.Target = half;
                halved = true;
            }
        }

        for (int i = _visible.Count - 1; i >= 0 && sum > capacity; i--)
        {
            LocalEntry e = _visible[i];
            sum -= (long)e.Faces * e.Layers * e.Target * e.Target;
            e.Target = 0;
        }
    }

    // The scale drops a step as soon as the wanted sizes stop fitting, and climbs one back only after they would
    // have fit a step higher for a while, so a crowded frame does not make every light flicker between sizes
    private void UpdateScale(int minTile)
    {
        long capacity = LocalCapacity();
        float current = ResolutionScale;

        long TexelsAt(float scale)
        {
            long sum = 0;
            foreach (LocalEntry e in _visible)
            {
                int t = RoundPow2(Maths.Clamp(e.Wanted * scale, minTile, e.MaxTile), minTile, e.MaxTile);
                sum += (long)e.Faces * e.Layers * t * t;
            }
            return sum;
        }

        if (TexelsAt(current) > capacity)
        {
            int steps = 0;
            float scale = current;
            while (TexelsAt(scale) > capacity && steps < 24)
            {
                scale /= ScaleStep;
                steps++;
            }
            _scaleLevel += steps;
            _framesAbove = 0;
        }
        else if (_scaleLevel > 0 && TexelsAt(current * ScaleStep) <= capacity)
        {
            if (++_framesAbove >= FramesBeforeScaleRises)
            {
                _scaleLevel--;
                _framesAbove = 0;
            }
        }
        else
        {
            _framesAbove = 0;
        }
    }

    // ---------------------------------------------------------------- allocation

    private void AllocateAll(object cameraKey, long frame)
    {
        if (!TryAllocateAll(cameraKey, frame, allowEviction: true))
        {
            Compact(cameraKey);
            TryAllocateAll(cameraKey, frame, allowEviction: false);
        }
    }

    private bool TryAllocateAll(object cameraKey, long frame, bool allowEviction)
    {
        if (_currentCascades != null && _currentDirectional != null)
        {
            int res = _currentDirectional.ShadowMapResolution;
            int count = (int)_currentDirectional.Cascades;
            for (int c = 0; c < 4; c++)
            {
                if (c >= count)
                {
                    FreeCascade(_currentCascades, c);
                    continue;
                }
                if (_currentCascades.Tiles[c].Size == res) continue;
                FreeCascade(_currentCascades, c);
                if (!AllocateWithEviction(res, out _currentCascades.Tiles[c], cameraKey, frame, allowEviction))
                {
                    if (allowEviction) return false;
                    _currentCascades.Tiles[c] = default;
                }
            }
        }

        int budget = ShadowAtlas.MaxResolutionChangesPerFrame;
        foreach (LocalEntry e in _visible)
        {
            bool contentInvalid = !e.ContentValid || e.Generation != _generation || e.LightKey != LightKey(e.Light);
            e.NeedsDraw = contentInvalid;

            if (e.Target == 0)
            {
                FreeTiles(e);
                continue;
            }

            // A light that has to be drawn anyway changes size for free, one that would only sharpen or soften waits for budget
            if (e.TileSize != e.Target)
            {
                bool mandatory = e.TileSize == 0 || contentInvalid;
                if (mandatory || budget >= e.Faces)
                {
                    if (!mandatory) budget -= e.Faces;
                    FreeTiles(e);
                    if (!AllocateFaces(e, e.Target, cameraKey, frame, allowEviction))
                    {
                        if (allowEviction) return false;
                        e.Target = 0;
                        continue;
                    }
                    e.NeedsDraw = true;
                }
            }

            // The static layer matches whatever size the light ended up holding, and is simply skipped when it does not fit
            if (!e.Split)
            {
                FreeStaticTiles(e);
            }
            else if (e.StaticTiles[0].Size != e.TileSize)
            {
                FreeStaticTiles(e);
                for (int f = 0; f < e.Faces && e.Split; f++)
                    e.Split = AllocateWithEviction(e.TileSize, out e.StaticTiles[f], cameraKey, frame, allowEviction);
                if (!e.Split) FreeStaticTiles(e);
            }
        }
        return true;
    }

    private bool AllocateFaces(LocalEntry e, int size, object cameraKey, long frame, bool allowEviction)
    {
        for (int f = 0; f < e.Faces; f++)
        {
            if (AllocateWithEviction(size, out e.Tiles[f], cameraKey, frame, allowEviction)) continue;
            for (int g = 0; g < f; g++)
            {
                ShadowAtlas.Allocator.Free(e.Tiles[g]);
                e.Tiles[g] = default;
            }
            return false;
        }
        e.TileSize = size;
        e.ContentValid = false;
        return true;
    }

    private bool AllocateWithEviction(int size, out ShadowTile tile, object cameraKey, long frame, bool allowEviction)
    {
        while (true)
        {
            if (ShadowAtlas.Allocator.TryAllocate(size, out tile)) return true;
            if (!allowEviction || !EvictOldest(cameraKey, frame)) return false;
        }
    }

    // Frees the tiles of whatever was used longest ago and is not needed for this render
    private bool EvictOldest(object cameraKey, long frame)
    {
        LocalEntry? oldestLocal = null;
        long oldest = long.MaxValue;
        foreach (LocalEntry e in _local.Values)
        {
            if (e.Visible || e.TileSize == 0 || e.LastUsedFrame >= oldest) continue;
            oldest = e.LastUsedFrame;
            oldestLocal = e;
        }

        CascadeSet? oldestSet = null;
        foreach (KeyValuePair<object, CascadeSet> kv in _cascades)
        {
            if (ReferenceEquals(kv.Key, cameraKey) || kv.Value.LastUsedFrame >= frame || kv.Value.LastUsedFrame >= oldest) continue;
            bool holds = false;
            foreach (ShadowTile t in kv.Value.Tiles) holds |= t.IsValid;
            if (!holds) continue;
            oldest = kv.Value.LastUsedFrame;
            oldestSet = kv.Value;
        }

        if (oldestSet != null)
        {
            ForgetTiles(oldestSet, free: true);
            return true;
        }
        if (oldestLocal != null)
        {
            FreeTiles(oldestLocal);
            return true;
        }
        return false;
    }

    // Packs everything again from an empty atlas, largest first, which cannot fragment. Every tile moves, so
    // everything this camera sees is drawn again
    private void Compact(object cameraKey)
    {
        foreach (LocalEntry e in _local.Values) FreeTiles(e);
        foreach (CascadeSet set in _cascades.Values) ForgetTiles(set, free: true);
        _visible.Sort(static (a, b) => b.Target != a.Target ? b.Target.CompareTo(a.Target) : b.Priority.CompareTo(a.Priority));
    }

    private void FreeTiles(LocalEntry e)
    {
        for (int f = 0; f < e.Tiles.Length; f++)
        {
            if (e.Tiles[f].IsValid) ShadowAtlas.Allocator.Free(e.Tiles[f]);
            e.Tiles[f] = default;
        }
        e.TileSize = 0;
        e.ContentValid = false;
        FreeStaticTiles(e);
    }

    private static void FreeStaticTiles(LocalEntry e)
    {
        for (int f = 0; f < e.StaticTiles.Length; f++)
        {
            if (e.StaticTiles[f].IsValid) ShadowAtlas.Allocator.Free(e.StaticTiles[f]);
            e.StaticTiles[f] = default;
        }
        e.StaticValid = false;
    }

    private static void FreeCascade(CascadeSet set, int c)
    {
        if (set.Tiles[c].IsValid) ShadowAtlas.Allocator.Free(set.Tiles[c]);
        set.Tiles[c] = default;
        set.Valid[c] = false;
    }

    // Drops tile references without freeing, for when the atlas itself was reset
    private static void ForgetTiles(LocalEntry e)
    {
        Array.Clear(e.Tiles);
        Array.Clear(e.StaticTiles);
        e.StaticValid = false;
        e.TileSize = 0;
        e.ContentValid = false;
    }

    private static void ForgetTiles(CascadeSet set, bool free = false)
    {
        for (int c = 0; c < 4; c++)
        {
            if (free) FreeCascade(set, c);
            set.Tiles[c] = default;
            set.Valid[c] = false;
        }
    }

    // ---------------------------------------------------------------- directional

    private CascadeSet GetCascadeSet(object key, long frame)
    {
        if (!_cascades.TryGetValue(key, out CascadeSet? set))
            _cascades[key] = set = new CascadeSet();
        set.LastUsedFrame = frame;
        return set;
    }

    private static ulong DirectionalKey(DirectionalLight light)
    {
        var hc = new HashCode();
        hc.Add(light.Transform.Forward);
        hc.Add(light.Transform.Up);
        hc.Add(light.ShadowMapResolution);
        hc.Add((int)light.Cascades);
        hc.Add(light.ShadowDistance);
        return PropertyState.Mix((uint)hc.ToHashCode());
    }

    private void DrawCascades(RenderPipeline pipeline, in ShadowCamera camera, IReadOnlyList<IRenderable> renderables, long frame)
    {
        CascadeSet set = _currentCascades!;
        DirectionalLight light = _currentDirectional!;
        int count = (int)light.Cascades;
        int res = light.ShadowMapResolution;
        float distance = Maths.Max(light.ShadowDistance, 0.01f);
        float splitNear = Maths.Min(Maths.Max(camera.FitView.Near, DirectionalLight.MinSplitNear), distance * 0.5f);

        ulong key = DirectionalKey(light);
        if (set.LightKey != key || set.Generation != _generation)
        {
            Array.Clear(set.Valid);
            set.LightKey = key;
            set.Generation = _generation;
        }
        set.Count = count;

        Float3 forward = light.Transform.Forward;
        Float3 right = light.Transform.Right;
        Float3 up = light.Transform.Up;

        for (int c = 0; c < count; c++)
        {
            ShadowTile tile = set.Tiles[c];
            if (!tile.IsValid)
            {
                set.Valid[c] = false;
                set.AtlasParams[c] = new Float4(-1, -1, 0, 0);
                continue;
            }

            // Cascade c refreshes every c + 1 frames, staggered so they rarely land on the same frame. Meanwhile it
            // keeps its own matrix and sphere, so what it holds stays consistent and only its edge lags
            int interval = c + 1;
            if (set.Valid[c] && (frame + c) % interval != 0) continue;

            float sliceNear = c == 0 ? camera.FitView.Near : DirectionalLight.GetCascadeSplit(c, count, splitNear, distance);
            float sliceFar = DirectionalLight.GetCascadeSplit(c + 1, count, splitNear, distance);
            camera.FitView.GetSliceSphere(sliceNear, sliceFar, out Float3 center, out float radius);
            light.GetShadowMatrix(center, res, radius, out Float4x4 view, out Float4x4 proj);

            bool[] culled = pipeline.CullRenderables(renderables, DirectionalLight.GetCasterFrustum(view, proj), LayerMask.Everything);
            DrawTile(pipeline, renderables, culled, view, proj, tile, new ViewerData(camera.Position, forward, right, up), depthClamp: true, "DirectionalCascade");
            pipeline.ReturnCullResult(culled);
            CascadesDrawn++;

            set.Valid[c] = true;
            set.Matrices[c] = RenderPipeline.ToGLClipDepth(proj * view);
            set.AtlasParams[c] = new Float4(tile.X, tile.Y, tile.Size, radius);
            set.Spheres[c] = new Float4(center, radius);
        }
    }

    // ---------------------------------------------------------------- local drawing

    private static ulong LightKey(Light light)
    {
        var hc = new HashCode();
        hc.Add(light.GetLightPosition());
        switch (light)
        {
            case PointLight p:
                hc.Add(p.Range);
                break;
            case SpotLight s:
                hc.Add(s.Range);
                hc.Add(s.SpotAngle);
                hc.Add(s.Transform.Forward);
                hc.Add(s.Transform.Up);
                break;
        }
        return PropertyState.Mix((uint)hc.ToHashCode() + 1UL);
    }

    private void DrawLocal(RenderPipeline pipeline, IReadOnlyList<IRenderable> renderables)
    {
        foreach (LocalEntry e in _visible)
        {
            if (e.TileSize == 0) continue;

            _staticCasters.Clear();
            _dynamicCasters.Clear();
            foreach (int i in e.Casters)
                (_casterKind[i] == StaticCaster ? _staticCasters : _dynamicCasters).Add(i);

            ulong staticSum = Sum(renderables, _staticCasters);
            ulong dynamicSum = Sum(renderables, _dynamicCasters);
            ulong signature = Finish(staticSum + dynamicSum, e.Casters.Count);

            if (!e.Split)
            {
                if (!e.NeedsDraw && signature == e.Signature) continue;
                DrawFaces(pipeline, renderables, e, e.Casters, e.Tiles, null);
                FacesDrawn += e.Faces;
                e.StaticValid = false;
            }
            else
            {
                ulong staticSignature = Finish(staticSum, _staticCasters.Count);
                ulong dynamicSignature = Finish(dynamicSum, _dynamicCasters.Count);
                bool staticDirty = e.NeedsDraw || !e.StaticValid || staticSignature != e.StaticSignature;
                if (!staticDirty && dynamicSignature == e.DynamicSignature && signature == e.Signature) continue;

                if (staticDirty)
                {
                    DrawFaces(pipeline, renderables, e, _staticCasters, e.StaticTiles, null);
                    StaticFacesDrawn += e.Faces;
                }
                DrawFaces(pipeline, renderables, e, _dynamicCasters, e.Tiles, e.StaticTiles);
                FacesDrawn += e.Faces;

                e.StaticValid = true;
                e.StaticSignature = staticSignature;
                e.DynamicSignature = dynamicSignature;
            }

            e.ContentValid = true;
            e.Signature = signature;
            e.LightKey = LightKey(e.Light);
            e.Generation = _generation;
        }
    }

    // Draws casters into each face's tile, either cleared or over a copy of another tile
    private void DrawFaces(RenderPipeline pipeline, IReadOnlyList<IRenderable> renderables, LocalEntry e,
                           List<int> casters, ShadowTile[] targets, ShadowTile[]? copyFrom)
    {
        Light light = e.Light;
        Float3 pos = light.GetLightPosition();
        bool[] culled = ArrayPool<bool>.Shared.Rent(renderables.Count);
        Array.Fill(culled, true, 0, renderables.Count);

        for (int f = 0; f < e.Faces; f++)
        {
            Float4x4 view, proj;
            if (light is PointLight point) point.GetShadowFace(f, out view, out proj);
            else ((SpotLight)light).GetShadowMatrix(out view, out proj);

            Frustum frustum = Frustum.FromMatrix(proj * view);
            foreach (int i in casters)
                culled[i] = !frustum.Intersects(_grid.Bounds(i));

            Float3 right = Float3.Normalize(new Float3(view.c0.X, view.c1.X, view.c2.X));
            Float3 up = Float3.Normalize(new Float3(view.c0.Y, view.c1.Y, view.c2.Y));
            Float3 forward = Float3.Normalize(new Float3(view.c0.Z, view.c1.Z, view.c2.Z));
            ShadowTile source = copyFrom == null ? default : copyFrom[f];
            DrawTile(pipeline, renderables, culled, view, proj, targets[f], new ViewerData(pos, forward, right, up), depthClamp: false, "LocalShadow", source);
            e.Matrices[f] = RenderPipeline.ToGLClipDepth(proj * view);
        }

        ArrayPool<bool>.Shared.Return(culled);
    }

    // Starts the tile cleared, or as a copy of copyFrom when that is a tile of the same size
    private void DrawTile(RenderPipeline pipeline, IReadOnlyList<IRenderable> renderables, bool[] culled,
                                 Float4x4 view, Float4x4 proj, ShadowTile tile, ViewerData viewer, bool depthClamp, string name,
                                 ShadowTile copyFrom = default)
    {
        // Each tile uploads its own view and projection into the global block, so tiles cannot share a buffer
        pipeline.AssignCameraMatrices(view, proj);

        using CommandBuffer cmd = Graphics.GetCommandBuffer(name);
        GraphicsFrameBuffer atlas = ShadowAtlas.FrameBuffer!;
        cmd.SetRenderTargets(atlas, atlas);
        cmd.SetViewport(tile.X, tile.Y, (uint)tile.Size, (uint)tile.Size);
        cmd.SetScissor(tile.X, tile.Y, (uint)tile.Size, (uint)tile.Size);
        if (copyFrom.IsValid)
        {
            // Two tiles of one atlas never overlap, which is what a blit within one framebuffer needs
            cmd.BlitFramebuffer(copyFrom.X, copyFrom.Y, copyFrom.X + copyFrom.Size, copyFrom.Y + copyFrom.Size,
                                tile.X, tile.Y, tile.X + tile.Size, tile.Y + tile.Size, ClearFlags.Depth, BlitFilter.Nearest);
        }
        else
        {
            cmd.ClearRenderTarget(ClearFlags.Depth, Color.Black);
        }
        cmd.SetDepthBias(Light.CasterSlopeBias, Light.CasterConstantBias);
        if (depthClamp) cmd.SetDepthClamp(true);
        DrawCasters(pipeline, cmd, renderables, culled, viewer);
        if (depthClamp) cmd.SetDepthClamp(false);
        cmd.SetDepthBias(0f, 0f);
        cmd.DisableScissor();
        Graphics.Submit(cmd);
    }

    // ---------------------------------------------------------------- depth only batching

    /// <summary>Instances of one mesh drawn through the shared depth material.</summary>
    private sealed class ShadowBatch : IRenderable
    {
        public Mesh Mesh = null!;
        public Material Material = null!;
        public int SubMesh;
        public InstanceData[] Instances = new InstanceData[16];
        public int Count;
        public Float4x4 First;

        public Material GetMaterial() => Material;
        public int GetLayer() => 0;
        public Float3 GetPosition() => Float3.Zero;
        public int GetSubMeshIndex() => SubMesh;
        public int GetInstanceCount(InstanceData[] instanceData) => Count;

        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = s_noProperties;
            mesh = Mesh;
            model = Float4x4.Identity;
            instanceData = Instances;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            isRenderable = true;
            bounds = default;
        }

        public void Add(in Float4x4 model)
        {
            if (Count == 0) First = model;
            if (Count == Instances.Length) Array.Resize(ref Instances, Count * 2);
            Instances[Count++] = new InstanceData(model);
        }
    }

    /// <summary>A mesh only one caster uses, drawn plainly through the shared depth material, since an instanced
    /// draw of one would only add an instance upload.</summary>
    private sealed class ShadowSingle : IRenderable
    {
        public Mesh Mesh = null!;
        public Material Material = null!;
        public int SubMesh;
        public Float4x4 Model;

        public Material GetMaterial() => Material;
        public int GetLayer() => 0;
        public Float3 GetPosition() => Float3.Zero;
        public int GetSubMeshIndex() => SubMesh;

        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = s_noProperties;
            mesh = Mesh;
            model = Model;
            instanceData = null;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            isRenderable = true;
            bounds = default;
        }
    }

    /// <summary>Merged static geometry of one buffer drawn through the shared depth material, every range in one draw.</summary>
    private sealed class ShadowRanges : IIndexRangeRenderable
    {
        public Mesh Mesh = null!;
        public Material Material = null!;
        public readonly List<IndexRange> Ranges = new();

        public Material GetMaterial() => Material;
        public int GetLayer() => 0;
        public Float3 GetPosition() => Float3.Zero;
        public Float4x4 GetWorldToObjectMatrix(in Float4x4 model) => Float4x4.Identity;

        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = s_noProperties;
            mesh = Mesh;
            model = Float4x4.Identity;
            instanceData = null;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            isRenderable = true;
            bounds = default;
        }

        public void AppendRanges(List<IndexRange> ranges)
        {
            foreach (IndexRange range in Ranges)
                IndexRange.Append(ranges, range.Start, range.Count);
        }
    }

    private static readonly PropertyState s_noProperties = new();

    private const byte NotBatched = 0;
    private const byte BatchedOneSided = 1;
    private const byte BatchedTwoSided = 2;

    private readonly Dictionary<(Mesh, int, bool), ShadowBatch> _batchLookup = new();
    private readonly List<ShadowBatch> _batchPool = new();
    private readonly List<ShadowBatch> _batches = new();
    private readonly List<ShadowSingle> _singlePool = new();
    private readonly Dictionary<(Mesh, bool), ShadowRanges> _rangesLookup = new();
    private readonly List<ShadowRanges> _rangesPool = new();
    private readonly List<ShadowRanges> _ranges = new();
    private readonly List<IRenderable> _depthDraws = new();
    private readonly Dictionary<Shader, byte> _batchKinds = new();
    private Material? _depthMaterial;
    private Material? _depthMaterialTwoSided;

    // The opaque Standard shaders only write depth in their shadow pass, so any of their materials draws the same
    private byte BatchKind(Shader shader)
    {
        if (_batchKinds.TryGetValue(shader, out byte kind)) return kind;

        kind = NotBatched;
        if (shader == Shader.LoadDefault(DefaultShader.Standard) || shader == Shader.LoadDefault(DefaultShader.StandardAnisotropic))
            kind = BatchedOneSided;
        else if (shader == Shader.LoadDefault(DefaultShader.StandardDoubleSided) || shader == Shader.LoadDefault(DefaultShader.StandardAnisotropicDoubleSided))
            kind = BatchedTwoSided;
        _batchKinds[shader] = kind;
        return kind;
    }

    private Material DepthMaterial(bool twoSided)
    {
        if (twoSided)
        {
            if (_depthMaterialTwoSided.IsNotValid()) _depthMaterialTwoSided = new Material(Shader.LoadDefault(DefaultShader.StandardDoubleSided));
            return _depthMaterialTwoSided!;
        }
        if (_depthMaterial.IsNotValid()) _depthMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        return _depthMaterial!;
    }

    private bool TryBatch(IRenderable renderable)
    {
        if (renderable is IIndexRangeRenderable ranged) return TryBatchRanges(ranged);
        if (renderable is not MeshRenderable) return false;
        byte kind = BatchKind(renderable.GetMaterial().Shader);
        if (kind == NotBatched) return false;

        renderable.GetRenderingData(default, out PropertyState _, out Mesh mesh, out Float4x4 model, out InstanceData[]? _);
        if (mesh is not { IsLoaded: true } || mesh.VertexCount <= 0) return false;
        // An instanced draw would skin a skinned mesh with bones nobody set
        if (mesh.HasBoneIndices && mesh.HasBoneWeights) return false;

        bool twoSided = kind == BatchedTwoSided;
        var key = (mesh, renderable.GetSubMeshIndex(), twoSided);
        if (!_batchLookup.TryGetValue(key, out ShadowBatch? batch))
        {
            if (_batches.Count == _batchPool.Count) _batchPool.Add(new ShadowBatch());
            batch = _batchPool[_batches.Count];
            batch.Mesh = mesh;
            batch.SubMesh = key.Item2;
            batch.Material = DepthMaterial(twoSided);
            batch.Count = 0;
            _batchLookup[key] = batch;
            _batches.Add(batch);
        }
        batch.Add(model);
        return true;
    }

    private bool TryBatchRanges(IIndexRangeRenderable renderable)
    {
        byte kind = BatchKind(renderable.GetMaterial().Shader);
        if (kind == NotBatched) return false;

        renderable.GetRenderingData(default, out PropertyState _, out Mesh mesh, out Float4x4 _, out InstanceData[]? _);
        if (mesh is not { IsLoaded: true } || mesh.VertexCount <= 0) return false;

        bool twoSided = kind == BatchedTwoSided;
        if (!_rangesLookup.TryGetValue((mesh, twoSided), out ShadowRanges? batch))
        {
            if (_ranges.Count == _rangesPool.Count) _rangesPool.Add(new ShadowRanges());
            batch = _rangesPool[_ranges.Count];
            batch.Mesh = mesh;
            batch.Material = DepthMaterial(twoSided);
            batch.Ranges.Clear();
            _rangesLookup[(mesh, twoSided)] = batch;
            _ranges.Add(batch);
        }
        renderable.AppendRanges(batch.Ranges);
        return true;
    }

    private void DrawCasters(RenderPipeline pipeline, CommandBuffer cmd, IReadOnlyList<IRenderable> renderables, bool[] culled, ViewerData viewer)
    {
        _batchLookup.Clear();
        _batches.Clear();
        _rangesLookup.Clear();
        _ranges.Clear();

        int count = renderables.Count;
        bool[] alone = ArrayPool<bool>.Shared.Rent(count);
        for (int i = 0; i < count; i++)
        {
            alone[i] = true;
            if (culled[i] || _casterKind[i] == 0 || TryBatch(renderables[i])) continue;
            alone[i] = false;
            UnbatchedDraws++;
        }

        pipeline.DrawRenderables(cmd, renderables, "LightMode", "ShadowCaster", viewer, alone, false);
        if (_batches.Count > 0 || _ranges.Count > 0)
        {
            _depthDraws.Clear();
            _depthDraws.AddRange(_ranges);
            int singles = 0;
            foreach (ShadowBatch batch in _batches)
            {
                if (batch.Count > 1)
                {
                    _depthDraws.Add(batch);
                    continue;
                }
                if (singles == _singlePool.Count) _singlePool.Add(new ShadowSingle());
                ShadowSingle single = _singlePool[singles++];
                single.Mesh = batch.Mesh;
                single.Material = batch.Material;
                single.SubMesh = batch.SubMesh;
                single.Model = batch.First;
                _depthDraws.Add(single);
            }
            pipeline.DrawRenderables(cmd, _depthDraws, "LightMode", "ShadowCaster", viewer, null!, false);
            BatchedDraws += _depthDraws.Count;
        }
        ArrayPool<bool>.Shared.Return(alone);
    }

    // ---------------------------------------------------------------- shader data

    // Block per shadowed local light, ShadowDataBlocks.BlockTexels texels:
    //   +0      header: x fade (1 full shadow, 0 none)
    //   +1..    one rect per face: xy tile position, z tile size, w texel size one unit from the light
    //   then    one matrix per face, four columns each, GL clip space
    private void WriteData()
    {
        foreach (LocalEntry e in _local.Values)
        {
            e.ActiveSlot = -1;
            if (!e.Visible || e.TileSize == 0 || !e.ContentValid) continue;

            if (e.DataSlot < 0) e.DataSlot = _data.AllocateSlot();
            int b = e.DataSlot * ShadowDataBlocks.BlockTexels;

            float texelPerUnit = e.Light is SpotLight spot
                ? 2f * spot.ShadowTanHalfAngle / e.TileSize
                : 2f / e.TileSize;

            _data.Write(b, new Float4(e.Fade, 0, 0, 0));
            for (int f = 0; f < e.Faces; f++)
            {
                ShadowTile t = e.Tiles[f];
                _data.Write(b + 1 + f, new Float4(t.X, t.Y, t.Size, texelPerUnit));
                int m = b + 1 + e.Faces + f * 4;
                Float4x4 mat = e.Matrices[f];
                _data.Write(m, mat.c0);
                _data.Write(m + 1, mat.c1);
                _data.Write(m + 2, mat.c2);
                _data.Write(m + 3, mat.c3);
            }

            e.ActiveSlot = e.DataSlot;
            LightsShadowed++;
        }
    }

    // ---------------------------------------------------------------- cleanup

    private void Forget(long frame)
    {
        _toForget.Clear();
        foreach (LocalEntry e in _local.Values)
        {
            if (e.SeenStamp != _stamp || e.Light.IsNotValid() || frame - e.LastUsedFrame > ForgetAfterFrames)
                _toForget.Add(e);
        }
        foreach (LocalEntry e in _toForget)
        {
            FreeTiles(e);
            if (e.DataSlot >= 0) _data.FreeSlot(e.DataSlot);
            _local.Remove(e.Light);
        }

        _cascadesToForget.Clear();
        foreach (KeyValuePair<object, CascadeSet> kv in _cascades)
            if (frame - kv.Value.LastUsedFrame > ForgetAfterFrames)
                _cascadesToForget.Add(kv.Key);
        foreach (object key in _cascadesToForget)
        {
            ForgetTiles(_cascades[key], free: true);
            _cascades.Remove(key);
        }
    }

    public void Dispose()
    {
        // Tiles from before an atlas rebuild are already gone, freeing them again would corrupt the allocator
        bool atlasCurrent = _generation == ShadowAtlas.Generation;
        foreach (LocalEntry e in _local.Values)
        {
            if (atlasCurrent) FreeTiles(e);
            else ForgetTiles(e);
        }
        foreach (CascadeSet set in _cascades.Values) ForgetTiles(set, free: atlasCurrent);
        _local.Clear();
        _cascades.Clear();
        _data.Dispose();
    }

    // ---------------------------------------------------------------- caster grid

    /// <summary>
    /// A uniform grid over this frame's shadow casters, so a light finds the casters in its range without testing
    /// every renderable. Objects too large for a few cells sit in one list every query checks.
    /// </summary>
    private sealed class CasterGrid
    {
        private const float CellSize = 16f;
        private const int MaxCellsPerObject = 64;

        private readonly Dictionary<long, List<int>> _cells = new();
        private readonly List<List<int>> _pool = new();
        private readonly List<int> _large = new();
        private int _poolUsed;
        private int[] _visit = [];
        private int _visitStamp;
        private AABB[] _bounds = [];

        public AABB Bounds(int index) => _bounds[index];

        public void Build(ReadOnlySpan<AABB> bounds, ReadOnlySpan<bool> hasBounds, byte[] casterKind, int count)
        {
            foreach (List<int> list in _cells.Values) list.Clear();
            _cells.Clear();
            _poolUsed = 0;
            _large.Clear();
            if (_visit.Length < count)
            {
                _visit = new int[Math.Max(count, _visit.Length * 2)];
                _bounds = new AABB[_visit.Length];
            }
            bounds[..count].CopyTo(_bounds);

            for (int i = 0; i < count; i++)
            {
                if (casterKind[i] == 0 || !hasBounds[i]) continue;
                AABB b = bounds[i];
                Cell(b.Min, out int x0, out int y0, out int z0);
                Cell(b.Max, out int x1, out int y1, out int z1);
                long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
                if (cells > MaxCellsPerObject)
                {
                    _large.Add(i);
                    continue;
                }
                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                            CellList(Key(x, y, z)).Add(i);
            }
        }

        public void QuerySphere(Float3 center, float radius, List<int> result)
        {
            result.Clear();
            if (++_visitStamp == int.MaxValue)
            {
                Array.Clear(_visit);
                _visitStamp = 1;
            }

            float radiusSq = radius * radius;
            Float3 extent = new(radius, radius, radius);
            Cell(center - extent, out int x0, out int y0, out int z0);
            Cell(center + extent, out int x1, out int y1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (_cells.TryGetValue(Key(x, y, z), out List<int>? list))
                            foreach (int i in list)
                                Test(i, center, radiusSq, result);
            foreach (int i in _large)
                Test(i, center, radiusSq, result);
        }

        private void Test(int i, Float3 center, float radiusSq, List<int> result)
        {
            if (_visit[i] == _visitStamp) return;
            _visit[i] = _visitStamp;
            AABB b = _bounds[i];
            Float3 closest = Maths.Clamp(center, b.Min, b.Max);
            if (Float3.LengthSquared(closest - center) <= radiusSq)
                result.Add(i);
        }

        private List<int> CellList(long key)
        {
            if (_cells.TryGetValue(key, out List<int>? list)) return list;
            if (_poolUsed == _pool.Count) _pool.Add(new List<int>());
            list = _pool[_poolUsed++];
            _cells[key] = list;
            return list;
        }

        private static void Cell(Float3 p, out int x, out int y, out int z)
        {
            x = (int)MathF.Floor(Maths.Clamp(p.X / CellSize, -1e6f, 1e6f));
            y = (int)MathF.Floor(Maths.Clamp(p.Y / CellSize, -1e6f, 1e6f));
            z = (int)MathF.Floor(Maths.Clamp(p.Z / CellSize, -1e6f, 1e6f));
        }

        private static long Key(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
    }

    // ---------------------------------------------------------------- shader data blocks

    /// <summary>The per light shadow blocks the shaders read, each written only when a value changes.</summary>
    private sealed class ShadowDataBlocks : IDisposable
    {
        public const int BlockTexels = 32;

        private readonly Stack<int> _freeSlots = new();
        private int _nextSlot;

        public ShaderDataTable Table { get; } = new("ProwlShadowData", "_ShadowDataTex", GraphicsFeature.FragmentStorageBuffers);

        public int AllocateSlot()
        {
            int slot = _freeSlots.Count > 0 ? _freeSlots.Pop() : _nextSlot++;
            Table.EnsureCapacity((slot + 1) * BlockTexels);
            return slot;
        }

        public void FreeSlot(int slot) => _freeSlots.Push(slot);

        public void Write(int texel, Float4 v) => Table[texel] = v;

        public void Dispose() => Table.Dispose();
    }
}
