using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Four wide trees over a world's renderables for culling: one over the static ones, built with the binned search when
/// they change, and one over the rest, built in Morton order on frames something in it moved. A static renderable that
/// moves is culled as moving from then on. A node wholly inside the frustum gives everything under it without testing.
/// </summary>
internal sealed class RenderableTree(RenderWorld.RenderSide world)
{
    private const int LeafSize = 4;
    private const byte None = 0, Static = 1, Moving = 2;

    private sealed class Tree(bool morton)
    {
        public readonly bool Morton = morton;
        public readonly List<int> Members = new();
        public readonly LightTreeBuilder Builder = new();
        public int[] Leaves = [];
        public Double3 Origin;
        public bool Dirty;
        private LightBounds[] _boxes = [];

        public void Build(RenderWorld.RenderSide world)
        {
            Dirty = false;
            int count = Members.Count;
            if (_boxes.Length < count)
                _boxes = new LightBounds[Math.Max(count, _boxes.Length * 2)];
            Origin = count > 0 ? world.WorldCenter[Members[0]] : default;
            for (int i = 0; i < count; i++)
            {
                int index = Members[i];
                Double3 d = world.WorldCenter[index] - Origin;
                System.Numerics.Vector3 center = new((float)d.X, (float)d.Y, (float)d.Z);
                Float3 e = world.WorldExtents[index];

                // Float rounding of the center is covered so a box never shrinks off what it holds
                System.Numerics.Vector3 pad = System.Numerics.Vector3.Abs(center) * 1e-6f + new System.Numerics.Vector3(1e-4f);
                System.Numerics.Vector3 extents = new System.Numerics.Vector3(e.X, e.Y, e.Z) + pad;
                _boxes[i] = new LightBounds { Min = center - extents, Max = center + extents };
            }
            Builder.Build(_boxes.AsSpan(0, count), LeafSize, Morton);
            ReadOnlySpan<int> order = Builder.Order;
            if (Leaves.Length < order.Length)
                Leaves = new int[Math.Max(order.Length, Leaves.Length * 2)];
            for (int i = 0; i < order.Length; i++)
                Leaves[i] = Members[order[i]];
        }
    }

    private readonly Tree _static = new(morton: false), _moving = new(morton: true);
    private byte[] _in = [];
    private int[] _slot = [];
    private bool[] _demoted = [];
    private readonly Stack<uint> _stack = new();

    public void Update(IReadOnlyList<int> drawChanged, IReadOnlyList<int> boundsChanged)
    {
        if (_in.Length < world.Capacity)
        {
            Array.Resize(ref _in, world.Capacity);
            Array.Resize(ref _slot, world.Capacity);
            Array.Resize(ref _demoted, world.Capacity);
        }
        foreach (int index in boundsChanged)
        {
            if (_in[index] == Static)
                _demoted[index] = true;
        }
        foreach (int index in drawChanged)
            Place(index);
        foreach (int index in boundsChanged)
        {
            Place(index);
            if (_in[index] == Moving)
                _moving.Dirty = true;
        }
        if (_static.Dirty)
            _static.Build(world);
        if (_moving.Dirty)
            _moving.Build(world);
    }

    private void Place(int index)
    {
        if (!world.Alive[index])
            _demoted[index] = false;
        byte want = !world.Alive[index] ? None : (world.Flags[index] & RenderableFlags.Static) != 0 && !_demoted[index] ? Static : Moving;
        if (want == _in[index])
            return;
        if (_in[index] != None)
        {
            Tree from = _in[index] == Static ? _static : _moving;
            int last = from.Members[^1];
            from.Members[_slot[index]] = last;
            _slot[last] = _slot[index];
            from.Members.RemoveAt(from.Members.Count - 1);
            from.Dirty = true;
        }
        _in[index] = want;
        if (want == None)
            return;
        Tree to = want == Static ? _static : _moving;
        _slot[index] = to.Members.Count;
        to.Members.Add(index);
        to.Dirty = true;
    }

    /// <summary>Every renderable whose box may touch the frustum, a superset of those that do.</summary>
    public void Cull(in CullFrustum frustum, List<int> into)
    {
        into.Clear();
        Cull(_static, frustum, into);
        Cull(_moving, frustum, into);
    }

    private void Cull(Tree tree, in CullFrustum frustum, List<int> into)
    {
        if (tree.Builder.NodeCount == 0)
            return;
        ReadOnlySpan<LightTreeNode> nodes = tree.Builder.Nodes;
        Float3 offset = frustum.Relative(tree.Origin);
        _stack.Clear();
        _stack.Push(0);
        while (_stack.Count > 0)
        {
            ref readonly LightTreeNode node = ref nodes[(int)_stack.Pop()];
            for (int i = 0; i < 4; i++)
            {
                uint child = node.Child(i);
                if (child == LightTreeBuilder.Invalid)
                    continue;
                LightBounds box = node.Box(i);
                System.Numerics.Vector3 c = (box.Min + box.Max) * 0.5f, e = (box.Max - box.Min) * 0.5f;
                int side = frustum.Classify(new Float3(c.X + offset.X, c.Y + offset.Y, c.Z + offset.Z), new Float3(e.X, e.Y, e.Z));
                if (side == 0)
                    continue;
                if ((child & LightTreeBuilder.LeafBit) != 0)
                    AddRun(tree, child, node.Count(i), into);
                else if (side == 2)
                    AddAll(tree, nodes, child, into);
                else
                    _stack.Push(child);
            }
        }
    }

    private static void AddRun(Tree tree, uint child, uint count, List<int> into)
    {
        int first = (int)(child & ~LightTreeBuilder.LeafBit);
        into.AddRange(tree.Leaves.AsSpan(first, (int)count));
    }

    private static void AddAll(Tree tree, ReadOnlySpan<LightTreeNode> nodes, uint start, List<int> into)
    {
        ref readonly LightTreeNode node = ref nodes[(int)start];
        for (int i = 0; i < 4; i++)
        {
            uint child = node.Child(i);
            if (child == LightTreeBuilder.Invalid)
                continue;
            if ((child & LightTreeBuilder.LeafBit) != 0)
                AddRun(tree, child, node.Count(i), into);
            else
                AddAll(tree, nodes, child, into);
        }
    }
}

/// <summary>What a draw record tells the vertex shader: the chunk, the view layer block, flags and the submesh for the material lookup.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawRecord
{
    /// <summary>Every instance draws Object rather than the instance list's entry.</summary>
    public const uint SharedObject = 1;

    /// <summary>SubMesh holds the material slot itself rather than the submesh whose slot the object's slice holds.</summary>
    public const uint DirectSlot = 2;

    public uint Chunk;
    public uint Object;
    public uint Flags;
    public uint SubMesh;

    public uint FirstInstance;
}

/// <summary>One multi draw indirect call: consecutive commands sharing program, page, topology and winding.</summary>
internal readonly record struct DrawCall(ushort Program, int Page, PrimitiveTopology Topology, bool Mirrored, int FirstCommand, int CommandCount);

/// <summary>
/// A material pass resolved up to its global keywords: shader pass, material variant and state override. Buckets and
/// merges compare these ids. The final program picks the global keywords per view.
/// </summary>
internal readonly record struct DrawProgram(Shader Shader, int PassIndex, ulong Variant, PassState? StateOverride, Keyword[] Keywords)
{
    public bool Equals(DrawProgram other) => Shader == other.Shader && PassIndex == other.PassIndex && Variant == other.Variant && Equals(StateOverride, other.StateOverride);

    public override int GetHashCode() => HashCode.Combine(Shader, PassIndex, Variant, StateOverride);
}

/// <summary>
/// Gives every distinct draw program a dense id for bucket keys. Buckets and transparent items hold references. An id
/// nothing references stays parked for <see cref="IdleFrames"/> frames, so a renderable moving between buckets keeps its
/// compiled programs, then is freed for reuse, its compiled programs dropped through <see cref="Freed"/>.
/// </summary>
internal sealed class DrawPrograms
{
    private readonly Dictionary<DrawProgram, ushort> _ids = new();
    private readonly List<DrawProgram> _programs = new();
    private readonly List<int> _references = new();
    private readonly Stack<ushort> _free = new();
    private readonly Dictionary<ushort, int> _idle = new();
    private readonly List<ushort> _expired = new();
    private int _frame;

    /// <summary>Frames an unreferenced id is kept before it is freed.</summary>
    public int IdleFrames { get; set; } = 120;

    public DrawProgram this[ushort id] => _programs[id];

    /// <summary>Ids in use.</summary>
    public int Count => _ids.Count;

    /// <summary>Raised when an id is freed, before it can be handed out again.</summary>
    public event Action<ushort>? Freed;

    /// <summary>Called once when every id is in use and a new program cannot get one.</summary>
    public Action<RendererError>? Reporter { get; set; }

    /// <summary>
    /// The depth only shader opaque materials without a ShadowCaster pass of their own cast through, its passes culling
    /// back faces, front faces and none, picked by the material's Forward pass.
    /// </summary>
    public Shader? DepthCaster { get; set; }

    public void Acquire(ushort id)
    {
        _references[id]++;
        _idle.Remove(id);
    }

    public void Release(ushort id)
    {
        if (--_references[id] > 0)
            return;
        _idle[id] = _frame;
    }

    /// <summary>Starts a frame, freeing ids unreferenced for longer than <see cref="IdleFrames"/>.</summary>
    public void Sweep()
    {
        _frame++;
        foreach ((ushort id, int since) in _idle)
        {
            if (_frame - since > IdleFrames)
                _expired.Add(id);
        }
        foreach (ushort id in _expired)
        {
            _idle.Remove(id);
            _ids.Remove(_programs[id]);
            _programs[id] = default;
            Freed?.Invoke(id);
            _free.Push(id);
        }
        _expired.Clear();
    }

    /// <summary>
    /// The program a material draws a pass type with, or false when its shader has no such pass. A transmissive draw
    /// takes its Transmission pass for Forward, and only volumes draw a BackDepth pass.
    /// </summary>
    public bool TryGet(MaterialDraw draw, PassType pass, out ushort id)
    {
        id = 0;
        if (pass == PassType.BackDepth && !draw.Volume)
            return false;
        if (pass == PassType.Forward && draw.Transmissive && draw.Shader.PassIndex(PassType.Transmission) >= 0)
            pass = PassType.Transmission;
        int passIndex = draw.Shader.PassIndex(pass);
        DrawProgram program;
        if (passIndex >= 0)
            program = new(draw.Shader, passIndex, draw.Shader.PassVariant(passIndex, draw.Keywords), draw.StateOverride, draw.Keywords);
        else if (pass == PassType.ShadowCaster && DepthCasterPass(draw) is int depthPass)
            program = new(DepthCaster!, depthPass, 0, null, []);
        else
            return false;
        if (_ids.TryGetValue(program, out id))
            return true;

        if (_free.Count > 0)
        {
            id = _free.Pop();
            _programs[id] = program;
        }
        else if (_programs.Count <= ushort.MaxValue)
        {
            id = (ushort)_programs.Count;
            _programs.Add(program);
            _references.Add(0);
        }
        else
        {
            Reporter?.Invoke(new RendererError("programs.full", "Every one of the 65536 draw program ids is in use, so new programs draw nothing."));
            Reporter = null;
            return false;
        }
        _ids[program] = id;
        return true;
    }

    // Whether a pass's own code discards fragments outside comments, a cutout whose shape depth alone would not keep; a
    // discard inside an included file is not seen, so such a pass needs a ShadowCaster pass of its own
    private static bool Discards(ShaderPass pass)
        => pass.Source != null && System.Text.RegularExpressions.Regex.IsMatch(System.Text.RegularExpressions.Regex.Replace(pass.Source, @"//[^\n]*|/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline), @"\bdiscard\b");

    // The depth only pass with the culling of the material's Forward pass, or null when it has none, opts out, blends,
    // writes no depth or discards
    private int? DepthCasterPass(MaterialDraw draw)
    {
        int forward = draw.Shader.PassIndex(PassType.Forward);
        if (DepthCaster == null || forward < 0)
            return null;
        ShaderPass pass = draw.Shader.Definition.Passes![forward];
        if (ShaderDefinition.PassHasTag(pass, "Shadows", "Off") || Discards(pass))
            return null;
        PassState state = draw.StateOverride is { } over ? over.Apply(pass.State) : pass.State;
        BlendAttachmentDescription blend = state.ToBlendState(BlendStateDescription.SingleOverrideBlend).AttachmentStates[0];
        bool blended = blend.BlendEnabled && !(blend.SourceColorFactor == BlendFactor.One && blend.DestinationColorFactor == BlendFactor.Zero);
        if (blended || !state.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyGreaterEqual).DepthWriteEnabled)
            return null;
        return state.ToRasterizerState(RasterizerStateDescription.Default).CullMode switch
        {
            FaceCullMode.Front => 1,
            FaceCullMode.None => 2,
            _ => 0,
        };
    }
}

/// <summary>
/// The planes of a view's frustums, one per layer, built from each layer's camera relative view projection: the view
/// without the camera's translation, so objects are tested at their position minus the first layer's camera in double
/// precision. Something is visible when any layer sees it. Planes a reversed infinite projection makes degenerate are dropped.
/// </summary>
/// <summary>Which renderables a view culls in: all, or for a shadow's static copy and the moving casters over it, one kind.</summary>
internal enum CasterFilter : byte
{
    All,
    StaticOnly,
    MovingOnly,
}

internal readonly struct CullFrustum
{
    private const int MaxLayers = 6;

    [InlineArray(MaxLayers * 6)]
    private struct Planes
    {
        private Float4 _first;
    }

    [InlineArray(MaxLayers)]
    private struct Counts
    {
        private int _first;
    }

    private readonly Planes _planes;
    private readonly Counts _counts;
    private readonly int _layers;

    public CullFrustum(in Float4x4 viewProjection, Double3 camera)
    {
        Camera = camera;
        _layers = 1;
        _counts[0] = AddLayer(ref _planes, 0, viewProjection, Float3.Zero, true);
    }

    /// <summary>One view, without its near plane when nearPlane is false, so a directional shadow keeps casters toward the light.</summary>
    public CullFrustum(in Float4x4 viewProjection, Double3 camera, bool nearPlane)
    {
        Camera = camera;
        _layers = 1;
        _counts[0] = AddLayer(ref _planes, 0, viewProjection, Float3.Zero, nearPlane);
    }

    public CullFrustum(in ViewLayers layers, int count)
    {
        if (count < 1 || count > MaxLayers)
            throw new ArgumentOutOfRangeException(nameof(count));
        Camera = layers[0].Position;
        _layers = count;
        for (int l = 0; l < count; l++)
        {
            ViewLayer layer = layers[l];
            _counts[l] = AddLayer(ref _planes, l * 6, layer.Projection * layer.View, Relative(layer.Position), true);
        }
    }

    // A layer's planes moved from its own camera to the first layer's, offset being its camera from the first
    private static int AddLayer(ref Planes planes, int first, in Float4x4 viewProjection, Float3 offset, bool nearPlane)
    {
        Float4 row0 = new(viewProjection.c0.X, viewProjection.c1.X, viewProjection.c2.X, viewProjection.c3.X);
        Float4 row1 = new(viewProjection.c0.Y, viewProjection.c1.Y, viewProjection.c2.Y, viewProjection.c3.Y);
        Float4 row2 = new(viewProjection.c0.Z, viewProjection.c1.Z, viewProjection.c2.Z, viewProjection.c3.Z);
        Float4 row3 = new(viewProjection.c0.W, viewProjection.c1.W, viewProjection.c2.W, viewProjection.c3.W);

        int count = 0;
        Span<Float4> candidates = [row3 + row0, row3 - row0, row3 + row1, row3 - row1, row2, row3 - row2];
        if (!nearPlane)
            candidates = candidates[..5];
        foreach (Float4 plane in candidates)
        {
            float length = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
            if (length <= 1e-6f)
                continue;
            Float4 p = plane / length;
            p.W -= p.X * offset.X + p.Y * offset.Y + p.Z * offset.Z;
            planes[first + count++] = p;
        }
        return count;
    }

    /// <summary>The first layer's camera position, which positions are tested relative to.</summary>
    public Double3 Camera { get; }

    public Float3 Relative(Double3 world) => new((float)(world.X - Camera.X), (float)(world.Y - Camera.Y), (float)(world.Z - Camera.Z));

    public bool Sphere(Double3 center, float radius)
    {
        Float3 c = Relative(center);
        for (int l = 0; l < _layers; l++)
        {
            bool inside = true;
            for (int i = 0; i < _counts[l] && inside; i++)
            {
                Float4 p = _planes[l * 6 + i];
                inside = p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W >= -radius;
            }
            if (inside)
                return true;
        }
        return false;
    }

    /// <summary>Whether a box relative to the camera is outside every layer (0), crosses one (1) or is wholly inside one (2).</summary>
    public int Classify(Float3 center, Float3 extents)
    {
        int result = 0;
        for (int l = 0; l < _layers; l++)
        {
            int layer = 2;
            for (int i = 0; i < _counts[l] && layer > 0; i++)
            {
                Float4 p = _planes[l * 6 + i];
                float distance = p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W;
                float radius = MathF.Abs(p.X) * extents.X + MathF.Abs(p.Y) * extents.Y + MathF.Abs(p.Z) * extents.Z;
                if (distance < -radius)
                    layer = 0;
                else if (distance < radius)
                    layer = 1;
            }
            if (layer == 2)
                return 2;
            result = Math.Max(result, layer);
        }
        return result;
    }

    public bool Box(Double3 worldCenter, Float3 extents)
    {
        Float3 center = Relative(worldCenter);
        for (int l = 0; l < _layers; l++)
        {
            bool inside = true;
            for (int i = 0; i < _counts[l] && inside; i++)
            {
                Float4 p = _planes[l * 6 + i];
                float distance = p.X * center.X + p.Y * center.Y + p.Z * center.Z + p.W;
                float radius = MathF.Abs(p.X) * extents.X + MathF.Abs(p.Y) * extents.Y + MathF.Abs(p.Z) * extents.Z;
                inside = distance >= -radius;
            }
            if (inside)
                return true;
        }
        return false;
    }
}

/// <summary>What one pass of one view draws this frame: the instance list, the indirect commands, their draw records and the calls over them.</summary>
internal sealed class DrawList
{
    public uint[] Instances = new uint[256];
    public int InstanceCount;
    public IndirectDrawIndexedArguments[] Commands = new IndirectDrawIndexedArguments[64];
    public DrawRecord[] Records = new DrawRecord[64];
    public int CommandCount;
    public readonly List<DrawCall> Calls = new();

    public void Clear()
    {
        InstanceCount = 0;
        CommandCount = 0;
        Calls.Clear();
    }

    public void AddInstance(uint objectIndex)
    {
        if (InstanceCount == Instances.Length)
            Array.Resize(ref Instances, Instances.Length * 2);
        Instances[InstanceCount++] = objectIndex;
    }

    public void AddCommand(in IndirectDrawIndexedArguments command, in DrawRecord record)
    {
        if (CommandCount == Commands.Length)
        {
            Array.Resize(ref Commands, Commands.Length * 2);
            Array.Resize(ref Records, Records.Length * 2);
        }
        Commands[CommandCount] = command with { FirstInstance = (uint)CommandCount };
        Records[CommandCount] = record with { FirstInstance = command.FirstInstance };
        CommandCount++;
    }

    /// <summary>Draws one more instance with the last command, whose instances end where the list ends.</summary>
    public void ExtendLast() => Commands[CommandCount - 1].InstanceCount++;

    /// <summary>Adds a command to the last call when program, page, topology and winding all match, else starts a call.</summary>
    public void Merge(ushort program, int page, PrimitiveTopology topology, bool mirrored)
    {
        int command = CommandCount - 1;
        if (Calls.Count > 0)
        {
            DrawCall last = Calls[^1];
            if (last.Program == program && last.Page == page && last.Topology == topology && last.Mirrored == mirrored && last.FirstCommand + last.CommandCount == command)
            {
                Calls[^1] = last with { CommandCount = last.CommandCount + 1 };
                return;
            }
        }
        Calls.Add(new DrawCall(program, page, topology, mirrored, command, 1));
    }
}

/// <summary>
/// The persistent draw buckets of one world. A bucket is one indirect command: a mesh chunk drawn by one program, with
/// the objects that use it. Buckets change only when renderables, their meshes, materials or flags change, never when
/// objects move. Per view, culling and a compaction turn them into a <see cref="DrawList"/> per pass. Transparent
/// submeshes are items instead, sorted per view back to front.
/// </summary>
internal sealed class BucketSet
{
    /// <summary>
    /// Pass types drawn from buckets: the built in three, opaque only, then any a feature asked for, drawn for every
    /// material with that pass. Transparent forward items are sorted per object.
    /// </summary>
    private readonly List<PassType> _passes = [PassType.Prepass, PassType.Forward, PassType.ShadowCaster];
    private const int BuiltInPasses = 3;

    internal readonly record struct BucketId(int Pass, ushort Program, int Queue, int Page, PrimitiveTopology Topology, bool Mirrored, uint Chunk);

    private readonly record struct Membership(Bucket Bucket, int Position);

    internal readonly record struct ChunkRef(uint Chunk, int ChunkInMesh, uint FirstIndex, uint IndexCount);

    /// <summary>One transparent submesh of one object, with the chunks it draws, or a provider's item for one view.</summary>
    internal sealed class TransparentItem
    {
        public bool Provided;
        public float Depth;
        public uint MaterialSlot;
        public bool Shared;
        public uint InstanceCount;
        public int FirstCommand;
        public required int Pass;
        public required int Object;
        public required ushort Program;

        /// <summary>The program drawing the back faces before Program draws the front ones, -1 when Program draws both, see <see cref="MaterialDraw.Sides"/>.</summary>
        public int BackProgram = -1;
        public required int Page;
        public required PrimitiveTopology Topology;
        public required bool Mirrored;
        public required int Queue;
        public required uint SubMesh;
        public required ChunkRef[] Chunks;
        public int Position;
    }

    internal sealed class Bucket
    {
        public required BucketId Id;
        public required int Queue;
        public required int MeshId;
        public required uint ChunkInMesh;
        public required uint FirstIndex;
        public required uint IndexCount;
        public required uint SubMesh;
        public required bool MultiChunk;
        public ulong Key;
        public int[] Objects = new int[4];
        public int Count;

        /// <summary>Its place in its pass's order, and the objects a sparse build found in it.</summary>
        public int OrderIndex;
        public int BuildStamp;
        public int[] Found = new int[4];
        public int FoundCount;
        public int Pass => Id.Pass;
        public ushort Program => Id.Program;
        public int Page => Id.Page;
        public PrimitiveTopology Topology => Id.Topology;
        public bool Mirrored => Id.Mirrored;
        public uint Chunk => Id.Chunk;
    }

    private readonly RenderWorld.RenderSide _world;
    private readonly DrawPrograms _programs;
    private readonly Dictionary<BucketId, Bucket> _buckets = new();
    private readonly List<List<Bucket>> _ordered = [new(), new(), new()];
    private readonly List<bool> _orderDirty = [true, true, true];
    private List<Membership>[] _members = [];
    private readonly Dictionary<Material, HashSet<int>> _materialUsers = new();
    private List<Material>?[] _objectMaterials = [];
    private readonly Dictionary<int, int> _queueRanks = new();
    private readonly Dictionary<int, int> _queueUse = new();
    private readonly HashSet<int> _unionQueues = new();
    private readonly Dictionary<int, int> _unionRanks = new();
    private bool _ranksDirty;
    private readonly HashSet<int> _rebuild = new();
    private readonly List<(ulong Key, int Bucket, int FirstInstance, int Count)> _prepass = new();
    private readonly List<TransparentItem> _transparent = new();
    private List<TransparentItem>?[] _transparentOf = [];
    private readonly List<(ulong Key, TransparentItem Item)> _sorted = new();
    private bool[] _visible = [];
    private readonly List<int> _visibleList = new();
    private readonly List<int> _candidates = new();
    private readonly List<Bucket> _touched = new();
    private int _buildStamp;
    private readonly RenderableTree _tree;
    private int[] _chunkStamp = [];
    private int[] _chunkStart = [];
    private bool[] _chunkVisible = new bool[256];
    private int _chunkCount;
    private int _cullStamp;

    public BucketSet(RenderWorld.RenderSide world, DrawPrograms programs)
    {
        _world = world;
        _programs = programs;
        _tree = new RenderableTree(world);
    }

    public int BucketCount => _buckets.Count;

    public int TransparentCount => _transparent.Count;

    /// <summary>The index of a pass type's buckets, bucketing every renderable for it the first time it is asked for. Render thread.</summary>
    public int Pass(PassType type)
    {
        int pass = _passes.IndexOf(type);
        if (pass >= 0)
            return pass;

        pass = _passes.Count;
        _passes.Add(type);
        _ordered.Add(new List<Bucket>());
        _orderDirty.Add(true);
        for (int index = 0; index < _world.Capacity; index++)
        {
            if (!_world.Alive[index] || _world.PlacementOf(index) is not { } placement)
                continue;
            AddToPass(index, pass, placement);
            if ((_world.Flags[index] & RenderableFlags.ShadowsOnly) == 0)
                AddTransparent(index, placement, _world.Mirrored[index], pass);
        }
        RefreshRanks();
        return pass;
    }

    public IReadOnlyList<Bucket> Ordered(int pass)
    {
        Order(pass);
        return _ordered[pass];
    }

    /// <summary>
    /// Rebuilds the buckets of renderables whose draws changed, and of every renderable using a material whose draws
    /// changed, and moves changed renderables in the culling trees. Render thread.
    /// </summary>
    public void Update(IReadOnlyList<int> drawChanged, IReadOnlyList<int> boundsChanged, IReadOnlyList<Material> changedMaterials)
    {
        _tree.Update(drawChanged, boundsChanged);
        _rebuild.Clear();
        foreach (int index in drawChanged)
            _rebuild.Add(index);
        foreach (Material material in changedMaterials)
        {
            if (_materialUsers.TryGetValue(material, out HashSet<int>? users))
                _rebuild.UnionWith(users);
        }

        foreach (int index in _rebuild)
        {
            RemoveObject(index);
            if (index < _world.Capacity && _world.Alive[index])
                AddObject(index);
        }

        RefreshRanks();
    }

    private void RefreshRanks()
    {
        if (!_ranksDirty)
            return;
        RankQueues();
        foreach (Bucket bucket in _buckets.Values)
            bucket.Key = Key(bucket);
        for (int i = 0; i < _orderDirty.Count; i++)
            _orderDirty[i] = true;
        _ranksDirty = false;
    }

    /// <summary>
    /// Fills the draw list of a pass from the last <see cref="Cull"/>: visible objects, then their chunk box when the mesh
    /// has several chunks. Empty commands are left out. Prepass commands are sorted near to far by their nearest visible
    /// instance.
    /// </summary>
    public void Build(int pass, in CullFrustum frustum, DrawList list, ProvidedItems? provided = null)
    {
        list.Clear();
        Order(pass);
        bool prepass = pass == 0;
        _prepass.Clear();

        // The visible objects' buckets of this pass, in the pass's order, each with its objects in index order
        _buildStamp++;
        _touched.Clear();
        foreach (int index in _visibleList)
        {
            if (index >= _members.Length || _members[index] is not { } members)
                continue;
            foreach (Membership membership in members)
            {
                Bucket found = membership.Bucket;
                if (found.Pass != pass)
                    continue;
                if (found.BuildStamp != _buildStamp)
                {
                    found.BuildStamp = _buildStamp;
                    found.FoundCount = 0;
                    _touched.Add(found);
                }
                if (found.FoundCount == found.Found.Length)
                    Array.Resize(ref found.Found, found.Found.Length * 2);
                found.Found[found.FoundCount++] = index;
            }
        }
        _touched.Sort((a, c) => a.OrderIndex.CompareTo(c.OrderIndex));

        for (int b = 0; b < _touched.Count; b++)
        {
            Bucket bucket = _touched[b];
            int first = list.InstanceCount;
            float nearest = float.MaxValue;
            Array.Sort(bucket.Found, 0, bucket.FoundCount);
            for (int i = 0; i < bucket.FoundCount; i++)
            {
                int index = bucket.Found[i];
                if (bucket.MultiChunk && !ChunkVisible(frustum, index, (int)bucket.ChunkInMesh))
                    continue;
                list.AddInstance((uint)index);
                if (prepass)
                    nearest = MathF.Min(nearest, Distance(frustum, index));
            }

            int count = list.InstanceCount - first;
            if (count == 0)
                continue;

            if (prepass)
            {
                _prepass.Add((PrepassKey(bucket, nearest), b, first, count));
                continue;
            }
            Emit(list, bucket, first, count);
        }

        if (prepass)
        {
            _prepass.Sort((a, c) => a.Key.CompareTo(c.Key));
            foreach ((_, int b, int first, int count) in _prepass)
                Emit(list, _touched[b], first, count);
        }
        foreach (TransparentItem item in provided?.Opaque ?? [])
        {
            item.FirstCommand = list.CommandCount;
            EmitProvided(list, item);
        }
        if (pass >= BuiltInPasses)
            AppendTransparent(frustum, list, pass, provided?.Sorted);
    }

    /// <summary>
    /// Fills the transparent draw list from the last <see cref="Cull"/>: every visible transparent item, sorted by queue,
    /// sort bias, then back to front. Adjacent items of the same program, page, topology, winding and chunk share a command.
    /// </summary>
    public void BuildTransparent(in CullFrustum frustum, DrawList list, IReadOnlyList<TransparentItem>? provided = null)
    {
        list.Clear();
        AppendTransparent(frustum, list, 1, provided);
    }

    // The visible transparent items of a pass, back to front, after what the list holds, providers' items among them
    private void AppendTransparent(in CullFrustum frustum, DrawList list, int pass, IReadOnlyList<TransparentItem>? provided = null)
    {
        _sorted.Clear();
        Dictionary<int, int> ranks = RanksWith(provided);
        foreach (TransparentItem item in _transparent)
        {
            if (item.Pass == pass && _visible[item.Object])
                _sorted.Add((TransparentKey(item, frustum, ranks), item));
        }
        if (provided != null)
        {
            foreach (TransparentItem item in provided)
                _sorted.Add((ProvidedKey(item, ranks), item));
        }
        _sorted.Sort((a, b) => a.Key.CompareTo(b.Key));

        DrawCommandRef previous = new(0, -1, default, false, 0, -1);
        foreach ((_, TransparentItem item) in _sorted)
        {
            if (item.Provided)
            {
                item.FirstCommand = list.CommandCount;
                EmitProvided(list, item);
                previous = new(0, -1, default, false, 0, -1);
                continue;
            }
            bool multiChunk = item.Chunks.Length > 1;
            for (int side = item.BackProgram >= 0 ? 0 : 1; side < 2; side++)
            {
                ushort program = side == 0 ? (ushort)item.BackProgram : item.Program;
                foreach (ChunkRef chunk in item.Chunks)
                {
                    if (multiChunk && !ChunkVisible(frustum, item.Object, chunk.ChunkInMesh))
                        continue;

                    DrawCommandRef current = new(program, item.Page, item.Topology, item.Mirrored, chunk.Chunk, list.CommandCount);
                    list.AddInstance((uint)item.Object);
                    if (list.CommandCount > 0 && previous == current with { Command = previous.Command } && previous.Command == list.CommandCount - 1)
                    {
                        list.ExtendLast();
                        continue;
                    }

                    list.AddCommand(
                        new IndirectDrawIndexedArguments { IndexCount = chunk.IndexCount, InstanceCount = 1, FirstIndex = chunk.FirstIndex, FirstInstance = (uint)(list.InstanceCount - 1) },
                        new DrawRecord { Chunk = chunk.Chunk, SubMesh = item.SubMesh });
                    list.Merge(program, item.Page, item.Topology, item.Mirrored);
                    previous = current;
                }
            }
        }
    }

    private readonly record struct DrawCommandRef(ushort Program, int Page, PrimitiveTopology Topology, bool Mirrored, uint Chunk, int Command);

    // The world's queue ranks, or ranks over its queues and providers' when a provider draws in a queue the world does not use
    private Dictionary<int, int> RanksWith(IReadOnlyList<TransparentItem>? provided)
    {
        bool missing = false;
        foreach (TransparentItem item in provided ?? [])
            missing |= !_queueRanks.ContainsKey(item.Queue);
        if (!missing)
            return _queueRanks;
        _unionQueues.Clear();
        _unionQueues.UnionWith(_queueRanks.Keys);
        foreach (TransparentItem item in provided!)
            _unionQueues.Add(item.Queue);
        Rank(_unionQueues, _unionRanks);
        return _unionRanks;
    }

    // [queueRank 6][sortBias 8, the middle][depth 32 back to front][program 12][spare 6]
    private static ulong ProvidedKey(TransparentItem item, Dictionary<int, int> ranks)
    {
        ulong rank = (ulong)ranks.GetValueOrDefault(item.Queue);
        ulong depth = ~BitConverter.SingleToUInt32Bits(MathF.Max(item.Depth, 0)) & 0xFFFFFFFFu;
        return (rank << 58) | (128ul << 50) | (depth << 18) | (((ulong)item.Program & 0xFFF) << 6);
    }

    /// <summary>A provider's item into a list: a command per chunk, through its own object or shared by its instances.</summary>
    internal static void EmitProvided(DrawList list, TransparentItem item)
    {
        for (int side = item.BackProgram >= 0 ? 0 : 1; side < 2; side++)
        {
            ushort program = side == 0 ? (ushort)item.BackProgram : item.Program;
            foreach (ChunkRef chunk in item.Chunks)
                EmitProvidedChunk(list, item, chunk, program);
        }
    }

    private static void EmitProvidedChunk(DrawList list, TransparentItem item, ChunkRef chunk, ushort program)
    {
        uint flags = DrawRecord.DirectSlot | (item.Shared ? DrawRecord.SharedObject : 0);
        if (!item.Shared)
            list.AddInstance((uint)item.Object);
        list.AddCommand(
            new IndirectDrawIndexedArguments
            {
                IndexCount = chunk.IndexCount,
                InstanceCount = item.Shared ? item.InstanceCount : 1,
                FirstIndex = chunk.FirstIndex,
                FirstInstance = item.Shared ? 0 : (uint)(list.InstanceCount - 1),
            },
            new DrawRecord { Chunk = chunk.Chunk, Object = (uint)item.Object, Flags = flags, SubMesh = item.MaterialSlot });
        list.Merge(program, item.Page, item.Topology, item.Mirrored);
    }

    /// <summary>The commands an item adds to a list, before culling of its chunks.</summary>
    internal static int CommandsOf(TransparentItem item) => item.Chunks.Length * (item.BackProgram >= 0 ? 2 : 1);

    // [queueRank 6][sortBias 8][depth 32 back to front][program 12][spare 6]
    private ulong TransparentKey(TransparentItem item, in CullFrustum frustum, Dictionary<int, int> ranks)
    {
        ulong rank = (ulong)ranks.GetValueOrDefault(item.Queue);
        ulong bias = (ulong)(_world.SortBias[item.Object] + 128);
        ulong depth = ~BitConverter.SingleToUInt32Bits(Distance(frustum, item.Object)) & 0xFFFFFFFFu;
        return (rank << 58) | (bias << 50) | (depth << 18) | (((ulong)item.Program & 0xFFF) << 6);
    }

    private static void Emit(DrawList list, Bucket bucket, int first, int count)
    {
        list.AddCommand(
            new IndirectDrawIndexedArguments { IndexCount = bucket.IndexCount, InstanceCount = (uint)count, FirstIndex = bucket.FirstIndex, FirstInstance = (uint)first },
            new DrawRecord { Chunk = bucket.Chunk, SubMesh = bucket.SubMesh });
        list.Merge(bucket.Program, bucket.Page, bucket.Topology, bucket.Mirrored);
    }

    /// <summary>
    /// Culls the world for one view, once for all of its passes: objects whose layer the culling mask has, not hidden,
    /// with their sphere in the frustum. Which passes an object draws in is decided by its buckets. Deforming objects it
    /// sees deform this frame.
    /// </summary>
    public void Cull(in CullFrustum frustum, uint cullingMask, CasterFilter filter = CasterFilter.All)
    {
        CullObjects(frustum, cullingMask, filter);
        foreach (int index in _world.Deforming)
        {
            if (index < _visible.Length && _visible[index])
                _world.DeformSeen[index] = _world.DeformStamp;
        }
    }

    private void CullObjects(in CullFrustum frustum, uint cullingMask, CasterFilter filter = CasterFilter.All)
    {
        if (_visible.Length < _world.Capacity)
        {
            _visible = new bool[_world.Capacity];
            Array.Resize(ref _chunkStamp, _world.Capacity);
            Array.Resize(ref _chunkStart, _world.Capacity);
        }
        _cullStamp++;
        _chunkCount = 0;
        foreach (int index in _visibleList)
            _visible[index] = false;
        _visibleList.Clear();

        _tree.Cull(frustum, _candidates);
        foreach (int index in _candidates)
        {
            if (_world.Alive[index]
                && (_world.Flags[index] & RenderableFlags.Hidden) == 0
                && (filter == CasterFilter.All || ((_world.Flags[index] & RenderableFlags.Static) != 0) == (filter == CasterFilter.StaticOnly))
                && (_world.LayerMask[index] & cullingMask) != 0
                && frustum.Sphere(_world.WorldCenter[index], _world.WorldRadius[index]))
            {
                _visible[index] = true;
                _visibleList.Add(index);
            }
        }
    }

    /// <summary>The static and the moving shadow casters a frustum holds, culling the world for it.</summary>
    public (int Static, int Moving) CountCasters(in CullFrustum frustum, uint cullingMask)
    {
        CullObjects(frustum, cullingMask);
        int statics = 0, moving = 0;
        int pass = _passes.IndexOf(PassType.ShadowCaster);
        foreach (int index in _visibleList)
        {
            if (!InPass(index, pass))
                continue;
            if ((_world.Flags[index] & RenderableFlags.Static) != 0)
                statics++;
            else
                moving++;
        }
        return (statics, moving);
    }

    private bool InPass(int index, int pass)
    {
        if (index >= _members.Length || _members[index] is not { } members)
            return false;
        foreach (Membership membership in members)
        {
            if (membership.Bucket.Pass == pass)
                return true;
        }
        return false;
    }

    /// <summary>The renderables drawing with a material.</summary>
    public IReadOnlyCollection<int> UsersOf(Material material) => _materialUsers.TryGetValue(material, out HashSet<int>? users) ? users : [];

    // Every chunk of an object is tested once per view, the first time any pass asks, and the answers are shared. A
    // deformed copy's chunks are wherever its bones put them, and a bounds override says its vertices move in the
    // shader, so either is as visible as its bounds
    private bool ChunkVisible(in CullFrustum frustum, int index, int chunkInMesh)
    {
        if (_world.Deformed[index] != null || _world.BoundsOverride[index] != null)
            return true;
        if (_chunkStamp[index] != _cullStamp)
        {
            MeshChunk[] chunks = _world.Mesh[index]!.Placement!.Chunks;
            if (_chunkCount + chunks.Length > _chunkVisible.Length)
                Array.Resize(ref _chunkVisible, Math.Max(_chunkCount + chunks.Length, _chunkVisible.Length * 2));
            Double4x4 transform = _world.Transform[index];
            for (int c = 0; c < chunks.Length; c++)
            {
                (Double3 center, Float3 extents) = RenderWorld.RenderSide.TransformBox(transform, chunks[c].Bounds);
                _chunkVisible[_chunkCount + c] = frustum.Box(center, extents);
            }
            _chunkStamp[index] = _cullStamp;
            _chunkStart[index] = _chunkCount;
            _chunkCount += chunks.Length;
        }
        return _chunkVisible[_chunkStart[index] + chunkInMesh];
    }

    private float Distance(in CullFrustum frustum, int index)
    {
        Float3 c = frustum.Relative(_world.WorldCenter[index]);
        return MathF.Max(0, MathF.Sqrt(c.X * c.X + c.Y * c.Y + c.Z * c.Z) - _world.WorldRadius[index]);
    }

    private void AddObject(int index)
    {
        if (_world.PlacementOf(index) is not { } placement)
            return;

        for (int pass = 0; pass < _passes.Count; pass++)
            AddToPass(index, pass, placement);
        if ((_world.Flags[index] & RenderableFlags.ShadowsOnly) == 0)
        {
            AddTransparent(index, placement, _world.Mirrored[index], 1);
            for (int pass = BuiltInPasses; pass < _passes.Count; pass++)
                AddTransparent(index, placement, _world.Mirrored[index], pass);
        }
    }

    // Built in passes draw opaque materials, the shadow pass every caster, transparent ones included; a feature's pass draws every material having it
    private void AddToPass(int index, int pass, MeshPlacement placement)
    {
        RenderableFlags flags = _world.Flags[index];
        bool shadows = _passes[pass] == PassType.ShadowCaster;
        if (shadows ? (flags & RenderableFlags.CastShadows) == 0 : (flags & RenderableFlags.ShadowsOnly) != 0)
            return;

        GpuMesh mesh = _world.Mesh[index]!;
        bool mirrored = _world.Mirrored[index];
        for (int c = 0; c < placement.Chunks.Length; c++)
        {
            MeshChunk chunk = placement.Chunks[c];
            Material? material = _world.SliceMaterials[_world.SliceBase[index] + (uint)chunk.SubMesh];
            if (material == null || material.IsDisposed || material.Draw is not { } draw)
                continue;
            if (shadows ? !draw.CastShadows : draw.Transparent)
                continue;
            if (!_programs.TryGet(draw, _passes[pass], out ushort program))
                continue;

            Track(material, index);
            BucketId id = new(pass, program, draw.RenderQueue, placement.Page, placement.SubMeshes[chunk.SubMesh].Topology, mirrored, placement.FirstChunk + (uint)c);
            if (!_buckets.TryGetValue(id, out Bucket? bucket))
            {
                bucket = new Bucket
                {
                    Id = id,
                    Queue = draw.RenderQueue,
                    MeshId = mesh.Id,
                    ChunkInMesh = (uint)c,
                    FirstIndex = placement.IndexBase + chunk.IndexStart,
                    IndexCount = chunk.IndexCount,
                    SubMesh = (uint)chunk.SubMesh,
                    MultiChunk = placement.Chunks.Length > 1,
                };
                _buckets[id] = bucket;
                _programs.Acquire(program);
                UseQueue(bucket.Queue, 1);
                bucket.Key = Key(bucket);
                _orderDirty[pass] = true;
            }
            Join(bucket, index);
        }
    }

    private void AddTransparent(int index, MeshPlacement placement, bool mirrored, int pass)
    {
        for (int s = 0; s < placement.SubMeshes.Length; s++)
        {
            Material? material = _world.SliceMaterials[_world.SliceBase[index] + (uint)s];
            if (material == null || material.IsDisposed || material.Draw is not { } draw || !draw.Transparent)
                continue;
            if (!_programs.TryGet(draw.Sides?.Front ?? draw, _passes[pass], out ushort program))
                continue;
            int back = draw.Sides is { } sides && _programs.TryGet(sides.Back, _passes[pass], out ushort backProgram) ? backProgram : -1;

            List<ChunkRef> chunks = new();
            for (int c = 0; c < placement.Chunks.Length; c++)
            {
                MeshChunk chunk = placement.Chunks[c];
                if (chunk.SubMesh == s)
                    chunks.Add(new ChunkRef(placement.FirstChunk + (uint)c, c, placement.IndexBase + chunk.IndexStart, chunk.IndexCount));
            }
            if (chunks.Count == 0)
                continue;

            Track(material, index);
            TransparentItem item = new()
            {
                Pass = pass,
                Object = index,
                Program = program,
                Page = placement.Page,
                Topology = placement.SubMeshes[s].Topology,
                Mirrored = mirrored,
                Queue = draw.RenderQueue,
                SubMesh = (uint)s,
                Chunks = [.. chunks],
                Position = _transparent.Count,
                BackProgram = back,
            };
            _transparent.Add(item);
            _programs.Acquire(program);
            if (back >= 0)
                _programs.Acquire((ushort)back);
            UseQueue(item.Queue, 1);
            if (index >= _transparentOf.Length)
                Array.Resize(ref _transparentOf, Math.Max(index + 1, _transparentOf.Length * 2));
            (_transparentOf[index] ??= new List<TransparentItem>()).Add(item);
        }
    }

    private void Join(Bucket bucket, int index)
    {
        if (bucket.Count == bucket.Objects.Length)
            Array.Resize(ref bucket.Objects, bucket.Objects.Length * 2);
        bucket.Objects[bucket.Count] = index;
        Members(index).Add(new Membership(bucket, bucket.Count));
        bucket.Count++;
    }

    private void RemoveObject(int index)
    {
        if (index < _transparentOf.Length && _transparentOf[index] is { } items)
        {
            foreach (TransparentItem item in items)
            {
                TransparentItem last = _transparent[^1];
                _transparent[item.Position] = last;
                last.Position = item.Position;
                _transparent.RemoveAt(_transparent.Count - 1);
                UseQueue(item.Queue, -1);
                _programs.Release(item.Program);
                if (item.BackProgram >= 0)
                    _programs.Release((ushort)item.BackProgram);
            }
            items.Clear();
        }

        List<Membership> members = index < _members.Length && _members[index] is { } list ? list : [];
        foreach (Membership membership in members)
        {
            Bucket bucket = membership.Bucket;
            int last = bucket.Count - 1;
            if (membership.Position != last)
            {
                int moved = bucket.Objects[last];
                bucket.Objects[membership.Position] = moved;
                List<Membership> movedMembers = _members[moved];
                for (int m = 0; m < movedMembers.Count; m++)
                {
                    if (movedMembers[m].Bucket == bucket && movedMembers[m].Position == last)
                    {
                        movedMembers[m] = movedMembers[m] with { Position = membership.Position };
                        break;
                    }
                }
            }
            bucket.Count--;

            if (bucket.Count == 0)
            {
                _buckets.Remove(bucket.Id);
                UseQueue(bucket.Queue, -1);
                _programs.Release(bucket.Program);
                _orderDirty[bucket.Pass] = true;
            }
        }
        members.Clear();

        if (index < _objectMaterials.Length && _objectMaterials[index] is { } materials)
        {
            foreach (Material material in materials)
            {
                if (_materialUsers.TryGetValue(material, out HashSet<int>? users) && users.Remove(index) && users.Count == 0)
                    _materialUsers.Remove(material);
            }
            materials.Clear();
        }
    }

    private void Track(Material material, int index)
    {
        if (!_materialUsers.TryGetValue(material, out HashSet<int>? users))
        {
            users = new HashSet<int>();
            _materialUsers[material] = users;
        }
        if (!users.Add(index))
            return;

        if (index >= _objectMaterials.Length)
            Array.Resize(ref _objectMaterials, Math.Max(index + 1, _objectMaterials.Length * 2));
        (_objectMaterials[index] ??= new List<Material>()).Add(material);
    }

    private List<Membership> Members(int index)
    {
        if (index >= _members.Length)
            Array.Resize(ref _members, Math.Max(index + 1, _members.Length * 2));
        return _members[index] ??= new List<Membership>();
    }

    private void Order(int pass)
    {
        if (!_orderDirty[pass])
            return;

        List<Bucket> ordered = _ordered[pass];
        ordered.Clear();
        foreach (Bucket bucket in _buckets.Values)
        {
            if (bucket.Pass == pass)
                ordered.Add(bucket);
        }
        ordered.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Chunk.CompareTo(b.Chunk));
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].OrderIndex = i;
        _orderDirty[pass] = false;
    }

    private void UseQueue(int queue, int delta)
    {
        int count = _queueUse.GetValueOrDefault(queue) + delta;
        if (count == 0)
        {
            _queueUse.Remove(queue);
            _ranksDirty = true;
        }
        else
        {
            if (!_queueUse.ContainsKey(queue))
                _ranksDirty = true;
            _queueUse[queue] = count;
        }
    }

    private void RankQueues() => Rank(_queueUse.Keys, _queueRanks);

    // Keys hold 64 ranks. Queues past the 64th share the last rank, so they keep drawing after the others.
    private static void Rank(IEnumerable<int> queues, Dictionary<int, int> ranks)
    {
        List<int> sorted = [.. queues];
        sorted.Sort();
        ranks.Clear();
        for (int i = 0; i < sorted.Count; i++)
            ranks[sorted[i]] = Math.Min(i, 63);
    }

    // [queueRank 6][program 16][topology 2, winding 1, page 5][mesh 24][spare 10]
    private ulong Key(Bucket bucket)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(bucket.Queue);
        return (rank << 58) | ((ulong)bucket.Program << 42) | (((ulong)bucket.Topology & 3) << 40) | ((bucket.Mirrored ? 1ul : 0) << 39)
            | (((ulong)bucket.Page & 31) << 34) | (((ulong)bucket.MeshId & 0xFFFFFF) << 10) | (bucket.ChunkInMesh & 1023);
    }

    // [queueRank 6][program 16][topology 2, winding 1, page 5][depth 12][mesh 22]
    private ulong PrepassKey(Bucket bucket, float distance)
    {
        ulong rank = (ulong)_queueRanks.GetValueOrDefault(bucket.Queue);
        ulong depth = (ulong)Math.Min(4095, (int)(MathF.Log2(1 + distance) * 256));
        return (rank << 58) | ((ulong)bucket.Program << 42) | (((ulong)bucket.Topology & 3) << 40) | ((bucket.Mirrored ? 1ul : 0) << 39)
            | (((ulong)bucket.Page & 31) << 34) | (depth << 22) | ((ulong)bucket.MeshId & 0x3FFFFF);
    }
}
