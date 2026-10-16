// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using BufferDescription = Prowl.Graphite.BufferDescription;
using BufferUsage = Prowl.Graphite.BufferUsage;
using CommandBuffer = Prowl.Graphite.CommandBuffer;
using DeviceBuffer = Prowl.Graphite.DeviceBuffer;
using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.ParticleSystem.Modules;

public enum ParticleRenderMode
{
    /// <summary>Quads facing the camera.</summary>
    Billboard,
    /// <summary>Quads facing the camera, stretched along the direction of travel.</summary>
    StretchedBillboard,
    /// <summary>Quads lying flat, facing up.</summary>
    HorizontalBillboard,
    /// <summary>Upright quads that turn around the world Y axis to face the camera.</summary>
    VerticalBillboard,
    /// <summary>A mesh per particle, using the particle's full 3D rotation and size.</summary>
    Mesh
}

public enum ParticleRenderAlignment
{
    /// <summary>Parallel to the camera's view plane.</summary>
    View,
    /// <summary>Turned toward the camera's position.</summary>
    Facing,
    /// <summary>Uses the particle's rotation in world axes.</summary>
    World,
    /// <summary>Uses the particle's rotation relative to the emitter.</summary>
    Local,
    /// <summary>Faces the direction of travel.</summary>
    Velocity
}

public enum ParticleSortMode
{
    None,
    /// <summary>Farthest first, the right order for alpha blending.</summary>
    ByDistance,
    OldestInFront,
    YoungestInFront
}

public enum ParticleBlendMode
{
    Alpha,
    Additive,
    /// <summary>For textures whose color is already multiplied by their alpha.</summary>
    Premultiplied
}

/// <summary>
/// How particles are drawn. Particles and trail segments are instances whose records the particle shader reads from a
/// buffer and turns to face each view, so a custom material has to read them the same way (see Particle.shader).
/// The shading options (blend, lighting, camera fade) are read by the built in particle shader.
/// Switching this module off hides the particles but not their trails.
/// </summary>
[Serializable]
public class RendererModule : ParticleSystemModule
{
    [Tooltip("Empty uses the built in particle material.")]
    public Material? Material;

    [Tooltip("Material for trails. Empty uses the particle material.")]
    public Material? TrailMaterial;

    public ParticleRenderMode RenderMode = ParticleRenderMode.Billboard;

    [ShowIf(nameof(IsMesh))]
    public Mesh? Mesh;

    [ShowIf(nameof(UsesAlignment))]
    public ParticleRenderAlignment Alignment = ParticleRenderAlignment.View;

    [ShowIf(nameof(IsStretched)), Tooltip("Length as a multiple of the particle's size.")]
    public float LengthScale = 2f;

    [ShowIf(nameof(IsStretched)), Tooltip("Extra length per unit of speed.")]
    public float VelocityScale = 0f;

    public ParticleSortMode SortMode = ParticleSortMode.ByDistance;

    [Tooltip("Moves the whole system in the transparent draw order. Negative draws it later, in front.")]
    public float SortingFudge = 0f;

    [Header("Shading")]
    public ParticleBlendMode BlendMode = ParticleBlendMode.Alpha;

    [Tooltip("Shade particles with the scene's lights and ambient.")]
    public bool Lit = false;

    [Tooltip("Fades particles out as they get this close to opaque geometry behind them. 0 turns it off.")]
    public float SoftParticleDistance = 0f;

    [Tooltip("Particles are invisible nearer than this distance from the camera.")]
    public float CameraFadeStart = 0f;

    [Tooltip("Particles are fully visible past this distance. At or below the start turns the fade off.")]
    public float CameraFadeEnd = 0f;

    [Header("Size")]
    [Range(0f, 1f), Tooltip("Smallest size, as a share of the screen height.")]
    public float MinParticleSize = 0f;

    [Range(0f, 1f), Tooltip("Largest size, as a share of the screen height.")]
    public float MaxParticleSize = 0.5f;

    [Tooltip("Offset of the particle's center, in multiples of its size.")]
    public Float3 Pivot = Float3.Zero;

    [Range(0f, 1f), Tooltip("Chance a particle's texture is mirrored left to right.")]
    public float FlipU = 0f;

    [Range(0f, 1f), Tooltip("Chance a particle's texture is mirrored top to bottom.")]
    public float FlipV = 0f;

    private bool IsMesh => RenderMode == ParticleRenderMode.Mesh;
    private bool IsStretched => RenderMode == ParticleRenderMode.StretchedBillboard;
    private bool UsesAlignment => RenderMode is ParticleRenderMode.Billboard or ParticleRenderMode.Mesh;

    private static Material? s_defaultMaterial;
    private static Mesh? s_quad;

    [NonSerialized] private ParticleRenderProvider? _provider;
    [NonSerialized] private RenderWorld? _world;
    [NonSerialized] private float[] _ageKeys = [];
    [NonSerialized] private int[] _ageOrder = [];
    [NonSerialized] private AABB _trailBounds;
    [NonSerialized] private bool _hasTrailBounds;

    public RendererModule() => Enabled = true;

    /// <summary>A unit quad facing +Z with normals for lit shading, the top of the texture at its top.</summary>
    private static Mesh Quad
    {
        get
        {
            if (s_quad.IsValid()) return s_quad;
            s_quad = new Mesh
            {
                Name = "Particle Quad",
                Vertices = [new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f)],
            };
            s_quad.UV = [new(0f, 1f), new(1f, 1f), new(1f, 0f), new(0f, 0f)];
            s_quad.Normals = [Float3.UnitZ, Float3.UnitZ, Float3.UnitZ, Float3.UnitZ];
            s_quad.Indices = [0, 1, 2, 0, 2, 3];
            s_quad.RecalculateBounds();
            return s_quad;
        }
    }

    private Material? ResolveMaterial(Material? preferred)
    {
        if (preferred.IsValid()) return preferred;
        if (Material.IsValid()) return Material;
        if (s_defaultMaterial.IsNotValid())
            s_defaultMaterial = BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Particle));
        return s_defaultMaterial;
    }

    private static GMaterial? GpuMaterial(Material? material)
    {
        if (material.IsNotValid()) return null;
        if (!material.IsLoaded)
        {
            if (material.State == AssetState.Unloaded) _ = material.LoadAsync();
            return null;
        }
        return material.GpuMaterial;
    }

    /// <summary>
    /// Builds this frame's draws: the particle records while this module is enabled, the trail segments whenever they
    /// exist. Main thread, before the frame is submitted.
    /// </summary>
    internal void Sync(ParticleSystemComponent system, RenderWorld world)
    {
        if (_world != world)
            Detach();
        if (_provider == null)
        {
            _provider = new ParticleRenderProvider(system, ParticleBuffers.Acquire(world));
            world.Providers.Add(_provider);
            _world = world;
        }

        ParticleRenderProvider provider = _provider;
        provider.Begin();
        uint layer = Rendering.RenderableState.LayerBit(system.GameObject.LayerIndex);
        uint objectId = (uint)system.InstanceID;

        if (Enabled && system.ParticleCount > 0 && system.HasBounds)
        {
            Mesh mesh = IsMesh && Mesh.IsValid() && Mesh.IsLoaded ? Mesh : Quad;
            if (IsMesh && Mesh.IsValid() && Mesh.State == AssetState.Unloaded) _ = Mesh.LoadAsync();
            mesh.Upload();
            GpuMesh? gpuMesh = mesh.GpuMesh;
            GMaterial? material = GpuMaterial(ResolveMaterial(null));
            if (gpuMesh != null && material != null)
            {
                AABB bounds = system.WorldBounds;
                Double3 origin = bounds.Center;
                int header = provider.NextRecord;
                WriteHeader(provider, 0, system.TextureSheet.ShaderParams);
                int count = WriteParticles(system, provider, (Float3)origin);
                bool sorted = SortMode == ParticleSortMode.ByDistance && BlendMode != ParticleBlendMode.Additive;
                provider.AddBatch(new ParticleBatch
                {
                    Mesh = gpuMesh,
                    SubMeshes = mesh.SubMeshCount,
                    Material = material,
                    Origin = origin,
                    Center = bounds.Center,
                    Radius = Float3.Length(bounds.Extents),
                    MinSize = IsMesh ? 0f : MinParticleSize,
                    Header = header,
                    Count = count,
                    Sorted = sorted,
                    SortFudge = SortingFudge,
                    LayerMask = layer,
                    ObjectId = objectId,
                });
            }
        }

        TrailModule trails = system.Trails;
        _hasTrailBounds = false;
        if (trails.Enabled || trails.HasOrphans)
        {
            GMaterial? material = GpuMaterial(ResolveMaterial(TrailMaterial));
            Mesh quad = Quad;
            quad.Upload();
            GpuMesh? gpuMesh = quad.GpuMesh;
            if (material != null && gpuMesh != null)
            {
                Double3 origin = system.Transform.Position;
                int header = provider.NextRecord;
                WriteHeader(provider, 1, new Float4(1f, 1f, 1f, 0f));
                int count = trails.BuildSegments(system, (Float3)origin, provider, ref _trailBounds, ref _hasTrailBounds);
                if (count > 0 && _hasTrailBounds)
                {
                    // Trails sort a hair behind their particles so the pair keeps a stable order
                    provider.AddBatch(new ParticleBatch
                    {
                        Mesh = gpuMesh,
                        SubMeshes = 1,
                        Material = material,
                        Origin = origin,
                        Center = _trailBounds.Center,
                        Radius = Float3.Length(_trailBounds.Extents),
                        Header = header,
                        Count = count,
                        SortFudge = SortingFudge + 0.01f,
                        LayerMask = layer,
                        ObjectId = objectId,
                    });
                }
                else
                {
                    provider.Truncate(header);
                }
            }
        }

        provider.Commit();
    }

    /// <summary>Takes this system's draws out of the world it was drawing into.</summary>
    internal void Detach()
    {
        if (_provider != null && _world != null)
        {
            _world.Providers.Remove(_provider);
            _provider.Release();
        }
        _provider = null;
        _world = null;
    }

    /// <summary>Whether any view drawn this frame sees the particles or trails, judged on the CPU.</summary>
    internal bool SeenThisFrame(ParticleSystemComponent system, RenderWorld world)
    {
        if (system.HasBounds && ParticleBuffers.AnyViewSees(world, system.WorldBounds, IsMesh ? 0f : MinParticleSize))
            return true;
        return _hasTrailBounds && ParticleBuffers.AnyViewSees(world, _trailBounds, 0f);
    }

    // Header: mode, render mode, alignment / sheet / blend, lit, fade / min size, max size, length scale, velocity scale / pivot
    private void WriteHeader(ParticleRenderProvider provider, int mode, Float4 sheet)
    {
        Span<Float4> rows = provider.Append(1);
        rows[0] = new Float4(mode, (float)RenderMode, (float)Alignment, 0f);
        rows[1] = sheet;
        bool fade = CameraFadeEnd > CameraFadeStart;
        rows[2] = new Float4((float)BlendMode, Lit ? 1f : 0f, fade ? CameraFadeStart : 0f, fade ? CameraFadeEnd : 0f);
        rows[3] = new Float4(MinParticleSize, MathF.Max(MinParticleSize, MaxParticleSize), LengthScale, VelocityScale);
        rows[4] = new Float4(Pivot, 0f);
        rows[5] = Float4.Zero;
    }

    private int WriteParticles(ParticleSystemComponent system, ParticleRenderProvider provider, Float3 origin)
    {
        ReadOnlySpan<Particle> particles = system.Particles;
        int count = particles.Length;
        Span<Float4> rows = provider.Append(count);

        // Age orders are the same for every view, so they are baked into the record order here
        bool byAge = BlendMode != ParticleBlendMode.Additive && SortMode is ParticleSortMode.OldestInFront or ParticleSortMode.YoungestInFront;
        if (_ageOrder.Length < count)
        {
            _ageOrder = new int[Math.Max(count, _ageOrder.Length * 2)];
            _ageKeys = new float[_ageOrder.Length];
        }
        for (int i = 0; i < count; i++)
        {
            _ageOrder[i] = i;
            _ageKeys[i] = SortMode == ParticleSortMode.OldestInFront ? particles[i].Age : -particles[i].Age;
        }
        if (byAge)
            Array.Sort(_ageKeys, _ageOrder, 0, count);

        Quaternion emitterRotation = system.Transform.Rotation;
        float sizeScale = system.SizeScale;
        for (int n = 0; n < count; n++)
        {
            ref readonly Particle p = ref particles[_ageOrder[n]];
            Float3 position = system.SimPointToWorld(p.Position) - origin;
            Float3 velocity = system.SimVectorToWorld(p.TotalVelocity);
            Float3 size = p.Size * sizeScale;
            Quaternion rotation = Quaternion.FromEuler(p.Rotation);
            if (Alignment == ParticleRenderAlignment.Local)
                rotation = emitterRotation * rotation;

            if (IsMesh)
            {
                if (Alignment == ParticleRenderAlignment.Velocity && Float3.LengthSquared(velocity) > 1e-10f)
                    rotation = Quaternion.LookRotation(Float3.Normalize(velocity), Float3.UnitY) * rotation;
            }
            else
            {
                if (FlipU > 0f && p.Random(0x161) < FlipU) size.X = -size.X;
                if (FlipV > 0f && p.Random(0x162) < FlipV) size.Y = -size.Y;
            }

            int row = n * ParticleRenderProvider.RecordRows;
            rows[row] = new Float4(position, size.X);
            rows[row + 1] = new Float4(velocity, size.Y);
            rows[row + 2] = new Float4(rotation.X, rotation.Y, rotation.Z, rotation.W);
            rows[row + 3] = new Float4(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
            rows[row + 4] = new Float4(p.UVFrame, p.NormalizedAge, p.CustomData.X, p.CustomData.Y);
            rows[row + 5] = new Float4(size.Z, p.Rotation.Z * Maths.Deg2Rad, 0f, 0f);
        }
        return count;
    }
}

/// <summary>One instanced draw of a system: its records in the world's buffer and what it is drawn with.</summary>
internal struct ParticleBatch
{
    public GpuMesh Mesh;
    public int SubMeshes;
    public GMaterial Material;
    public Double3 Origin;
    public Double3 Center;
    public float Radius;
    public float MinSize;

    /// <summary>The header record, relative to the system's first record. The instances follow it.</summary>
    public int Header;
    public int Count;

    /// <summary>Drawn back to front for each view.</summary>
    public bool Sorted;
    public float SortFudge;
    public uint LayerMask;
    public uint ObjectId;
}

/// <summary>
/// Draws one particle system into every view. The host writes the frame's records in Sync, Extract hands them to the
/// render thread, Cull adds the instanced draws per view with their back to front order, and Compute uploads the
/// records and orders into the world's buffers before any view draws.
/// </summary>
internal sealed class ParticleRenderProvider(ParticleSystemComponent system, ParticleBuffers buffers) : RenderProvider
{
    /// <summary>Float4 rows in one record: a header, a particle or a trail segment.</summary>
    public const int RecordRows = 6;

    private sealed class Frame
    {
        public Float4[] Rows = [];
        public int Count;
        public readonly List<ParticleBatch> Batches = [];
        public int First;
        public ulong Index = ulong.MaxValue;
    }

    // The host fills one frame while the render thread reads the other, swapped at Extract
    private Frame _host = new();
    private Frame _render = new();

    private DeviceBuffer? _records;
    private DeviceBuffer? _order;
    private uint[] _orderData = [];
    private int _orderUsed;
    private readonly List<(int Offset, int Start, int Count)> _orderUploads = [];
    private float[] _sortKeys = [];

    public override ViewKinds RunsOn => ViewKinds.Cameras | ViewKinds.ObjectId;

    /// <summary>The next record to be written this frame.</summary>
    public int NextRecord => _host.Count / RecordRows;

    public void Begin()
    {
        _host.Batches.Clear();
        _host.Count = 0;
    }

    /// <summary>Room for records at the end of this frame's rows.</summary>
    public Span<Float4> Append(int records)
    {
        int needed = _host.Count + records * RecordRows;
        if (_host.Rows.Length < needed)
            Array.Resize(ref _host.Rows, Math.Max(needed, _host.Rows.Length * 2));
        Span<Float4> span = _host.Rows.AsSpan(_host.Count, records * RecordRows);
        _host.Count = needed;
        return span;
    }

    /// <summary>Drops the records written from <paramref name="record"/> on.</summary>
    public void Truncate(int record) => _host.Count = Math.Min(_host.Count, record * RecordRows);

    public void AddBatch(in ParticleBatch batch) => _host.Batches.Add(batch);

    /// <summary>Places this frame's records in the world's buffer. Main thread, before submit.</summary>
    public void Commit()
    {
        _host.First = _host.Count > 0 ? buffers.Reserve(_host.Count / RecordRows) : 0;
        _host.Index = Graphics.FrameIndex;
    }

    public void Release() => buffers.Release();

    public override void Extract()
    {
        (_host, _render) = (_render, _host);
        _records = buffers.Records;
        _order = buffers.Order;
        _orderUsed = 0;
        _orderUploads.Clear();
        buffers.BeginRenderFrame();

        if (_render.Index != Graphics.FrameIndex)
        {
            _render.Batches.Clear();
            _render.Count = 0;
            return;
        }
        system.CheckVisibility(buffers.World);
    }

    public override void Cull(in ProviderView view, DrawItemSink sink)
    {
        if (_render.Batches.Count == 0) return;

        ParticleBuffers.ViewInfo info = buffers.Find(view.View.ViewId);
        foreach (ParticleBatch batch in _render.Batches)
        {
            float radius = batch.Radius;
            if (batch.MinSize > 0f && info.Found)
            {
                float farthest = (float)Double3.Length(batch.Center - view.Camera) + radius;
                radius += batch.MinSize * ParticleBuffers.ScreenHeight(info.YScale, info.Orthographic, farthest) * 0.75f;
            }
            if (!view.Sees(batch.Center, radius)) continue;

            int orderBase = batch.Sorted && batch.Count > 1 ? Sort(batch, view.Camera) : -1;
            DrawItem item = new()
            {
                Transform = Double4x4.CreateTranslation(batch.Origin),
                Custom1 = new Float4(_render.First + batch.Header, orderBase + 1, 0f, 0f),
                LayerMask = batch.LayerMask,
                ObjectId = batch.ObjectId,
                Flags = RenderableFlags.ReceiveShadows,
                SortDepth = SortDepth(batch, view.Camera, info),
            };
            for (int s = 0; s < batch.SubMeshes; s++)
                sink.AddInstanced(batch.Mesh, s, batch.Material, item, (uint)batch.Count);
        }
    }

    /// <summary>The distance to the system's center, moved along the view by the sorting fudge but never past the camera.</summary>
    private static float SortDepth(in ParticleBatch batch, Double3 camera, in ParticleBuffers.ViewInfo info)
    {
        Float3 toCenter = (Float3)(batch.Center - camera);
        if (!info.Found || batch.SortFudge == 0f)
            return Float3.Length(toCenter);
        float depth = Float3.Dot(toCenter, info.Forward);
        float bias = batch.SortFudge;
        if (depth > 0.01f)
            bias = MathF.Max(bias, 0.01f - depth);
        return Float3.Length(toCenter + info.Forward * bias);
    }

    // Farthest first for this view. Returns the order's place in the order buffer, or -1 when it has no room this frame.
    private int Sort(in ParticleBatch batch, Double3 camera)
    {
        int count = batch.Count;
        int offset = buffers.AllocateOrder(count);
        if (offset < 0) return -1;

        if (_sortKeys.Length < count)
            _sortKeys = new float[Math.Max(count, _sortKeys.Length * 2)];
        if (_orderData.Length < _orderUsed + count)
            Array.Resize(ref _orderData, Math.Max(_orderUsed + count, _orderData.Length * 2));

        Float3 eye = (Float3)(camera - batch.Origin);
        Span<uint> order = _orderData.AsSpan(_orderUsed, count);
        Float4[] rows = _render.Rows;
        int first = (batch.Header + 1) * RecordRows;
        for (int i = 0; i < count; i++)
        {
            Float4 r = rows[first + i * RecordRows];
            Float3 d = new Float3(r.X, r.Y, r.Z) - eye;
            _sortKeys[i] = -Float3.Dot(d, d);
            order[i] = (uint)i;
        }
        _sortKeys.AsSpan(0, count).Sort(order);

        _orderUploads.Add((offset, _orderUsed, count));
        _orderUsed += count;
        return offset;
    }

    public override void ComputeBuffers(List<DeviceBuffer> list)
    {
        if (_render.Count > 0 && _records != null)
            list.Add(_records);
        if (_orderUploads.Count > 0 && _order != null)
            list.Add(_order);
    }

    public override void Compute(CommandBuffer command)
    {
        if (_render.Count > 0 && _records != null)
            command.UpdateBuffer<Float4>(_records, (uint)(_render.First * RecordRows * 16), _render.Rows.AsSpan(0, _render.Count));
        if (_order == null) return;
        foreach ((int offset, int start, int count) in _orderUploads)
            command.UpdateBuffer<uint>(_order, (uint)(offset * 4), _orderData.AsSpan(start, count));
    }
}

/// <summary>
/// The buffers a world's particle draws read, set as world globals: every system's records, and the per view orders of
/// sorted systems. Systems reserve their records in Sync, growing the buffer before the frame is submitted, and the
/// render thread hands out order space while it culls. Orders that do not fit draw unsorted for a frame and grow the
/// buffer for the next.
/// </summary>
internal sealed class ParticleBuffers
{
    private const string RecordsName = "ProwlParticleRecords";
    private const string OrderName = "ProwlParticleOrder";
    private const int RecordBytes = ParticleRenderProvider.RecordRows * 16;

    private static readonly ConditionalWeakTable<RenderWorld, ParticleBuffers> s_worlds = new();

    private readonly RenderWorld _world;
    private int _users;
    private int _recordCapacity;
    private int _orderCapacity;
    private ulong _frame = ulong.MaxValue;
    private int _recordsUsed;
    private int _orderNeeded;
    private ulong _renderFrame = ulong.MaxValue;
    private int _renderOrderCapacity;
    private int _orderUsed;
    private readonly List<ViewInfo> _views = [];

    private ParticleBuffers(RenderWorld world) => _world = world;

    public RenderWorld World => _world;

    public DeviceBuffer? Records { get; private set; }
    public DeviceBuffer? Order { get; private set; }

    /// <summary>What sorting and culling need from one view of this frame.</summary>
    public readonly record struct ViewInfo(int ViewId, Float3 Forward, float YScale, bool Orthographic, bool Found);

    public static ParticleBuffers Acquire(RenderWorld world)
    {
        ParticleBuffers buffers = s_worlds.GetValue(world, w => new ParticleBuffers(w));
        buffers._users++;
        return buffers;
    }

    /// <summary>Frees the buffers once no system in the world uses them.</summary>
    public void Release()
    {
        if (--_users > 0) return;
        _world.Globals.SetBuffer(RecordsName, null);
        _world.Globals.SetBuffer(OrderName, null);
        Graphics.DeferDispose(Records);
        Graphics.DeferDispose(Order);
        Records = null;
        Order = null;
        _recordCapacity = 0;
        _orderCapacity = 0;
        s_worlds.Remove(_world);
    }

    /// <summary>Records for one system this frame. Returns the first. Main thread, before submit.</summary>
    public int Reserve(int records)
    {
        if (_frame != Graphics.FrameIndex)
        {
            _frame = Graphics.FrameIndex;
            _recordsUsed = 0;
            int needed = Math.Max(Volatile.Read(ref _orderNeeded), 256);
            if (Order == null || needed > _orderCapacity)
            {
                _orderCapacity = Grow(_orderCapacity, needed, 256);
                Order = Replace(Order, OrderName, (uint)_orderCapacity * 4);
            }
        }

        int first = _recordsUsed;
        _recordsUsed += records;
        if (Records == null || _recordsUsed > _recordCapacity)
        {
            _recordCapacity = Grow(_recordCapacity, _recordsUsed, 64);
            Records = Replace(Records, RecordsName, (uint)(_recordCapacity * RecordBytes));
        }
        return first;
    }

    private static int Grow(int capacity, int needed, int minimum)
    {
        int size = Math.Max(capacity, minimum);
        while (size < needed)
            size *= 2;
        return size;
    }

    private DeviceBuffer Replace(DeviceBuffer? old, string name, uint bytes)
    {
        DeviceBuffer buffer = Graphics.Device!.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.StructuredBufferReadWrite));
        buffer.Name = name;
        _world.Globals.SetBuffer(name, buffer);
        Graphics.DeferDispose(old);
        return buffer;
    }

    /// <summary>Starts the render side of a frame once, from the first provider's Extract. Host thread, render thread idle.</summary>
    public void BeginRenderFrame()
    {
        if (_renderFrame == Graphics.FrameIndex) return;
        _renderFrame = Graphics.FrameIndex;
        _renderOrderCapacity = Order != null ? _orderCapacity : 0;
        _orderUsed = 0;

        _views.Clear();
        foreach (ViewDesc view in Graphics.FrameViews)
        {
            if (view.World != _world) continue;
            ViewLayer layer = view.Layers[0];
            Float4x4 v = layer.View;
            _views.Add(new ViewInfo(view.ViewId, new Float3(v.c0.Z, v.c1.Z, v.c2.Z), layer.Projection.c1.Y, layer.Projection.c3.W > 0.5f, true));
        }
    }

    public ViewInfo Find(int viewId)
    {
        foreach (ViewInfo info in _views)
            if (info.ViewId == viewId)
                return info;
        return default;
    }

    /// <summary>Room for one view's order. Render thread, while culling.</summary>
    public int AllocateOrder(int count)
    {
        if (_orderUsed + count > _renderOrderCapacity)
        {
            Volatile.Write(ref _orderNeeded, Math.Max(Volatile.Read(ref _orderNeeded), _orderUsed + count));
            return -1;
        }
        int offset = _orderUsed;
        _orderUsed += count;
        return offset;
    }

    /// <summary>World height the screen covers at a distance in front of the camera.</summary>
    public static float ScreenHeight(float yScale, bool orthographic, float depth)
        => 2f / MathF.Max(MathF.Abs(yScale), 1e-6f) * (orthographic ? 1f : MathF.Max(depth, 1e-4f));

    /// <summary>
    /// Whether a camera view of the world added this frame sees a box, grown by how far a minimum screen size can
    /// enlarge a particle. Host thread, once the views are added.
    /// </summary>
    public static bool AnyViewSees(RenderWorld world, AABB box, float minSize)
    {
        foreach (ViewDesc view in Graphics.FrameViews)
        {
            if (view.World != world || view.Kind is not (ViewKind.Game or ViewKind.SceneView or ViewKind.Preview or ViewKind.RenderTexture)) continue;
            for (int l = 0; l < Math.Max((int)view.LayerCount, 1); l++)
            {
                ViewLayer layer = view.Layers[l];
                Float3 min = box.Min - (Float3)layer.Position;
                Float3 max = box.Max - (Float3)layer.Position;
                if (minSize > 0f)
                {
                    float farthest = Float3.Length((min + max) * 0.5f) + Float3.Length((max - min) * 0.5f);
                    float pad = minSize * ScreenHeight(layer.Projection.c1.Y, layer.Projection.c3.W > 0.5f, farthest) * 0.75f;
                    min -= new Float3(pad);
                    max += new Float3(pad);
                }
                if (Inside(layer.Projection * layer.View, min, max))
                    return true;
            }
        }
        return false;
    }

    // The box against the clip planes of a reversed Z projection: the sides, near (z below w) and far (z above 0)
    private static bool Inside(Float4x4 m, Float3 min, Float3 max)
    {
        Float4 r0 = new(m.c0.X, m.c1.X, m.c2.X, m.c3.X);
        Float4 r1 = new(m.c0.Y, m.c1.Y, m.c2.Y, m.c3.Y);
        Float4 r2 = new(m.c0.Z, m.c1.Z, m.c2.Z, m.c3.Z);
        Float4 r3 = new(m.c0.W, m.c1.W, m.c2.W, m.c3.W);
        return !(Outside(r3 + r0, min, max) || Outside(r3 - r0, min, max) || Outside(r3 + r1, min, max)
            || Outside(r3 - r1, min, max) || Outside(r3 - r2, min, max) || Outside(r2, min, max));
    }

    private static bool Outside(Float4 plane, Float3 min, Float3 max)
    {
        Float3 p = new(plane.X >= 0f ? max.X : min.X, plane.Y >= 0f ? max.Y : min.Y, plane.Z >= 0f ? max.Z : min.Z);
        return plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z + plane.W < 0f;
    }
}
