using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public enum ShadowFilter : byte
{
    /// <summary>One hardware comparison, a hard edge.</summary>
    Hard,

    /// <summary>A 5x5 tent from 9 hardware comparisons.</summary>
    Tent,

    /// <summary>
    /// Soft by how far a shadow falls from what casts it, for lights with a size: a spot or point light's SourceRadius, a
    /// directional light's AngularRadius. Lights of no size use the tent.
    /// </summary>
    ContactHardening,
}

/// <summary>How a world's local light shadows are stored and sized. Host thread, read at SubmitFrame.</summary>
public sealed class ShadowSettings
{
    /// <summary>The size of the two square pages local light tiles live in, one for live tiles and one for static copies.</summary>
    public int PageSize = 8192;

    /// <summary>Shadow map texels per screen pixel a light covers, before rounding to a power of two.</summary>
    public float TexelsPerPixel = 1;

    public int MinTileSize = 64;

    /// <summary>Faces that may change size a frame only to sharpen or soften, so a camera move does not redraw every light at once.</summary>
    public int MaxResizesPerFrame = 32;

    /// <summary>Local lights farther than this from every view cast no shadow, fading out over the last tenth.</summary>
    public float LocalDistance = 100;

    /// <summary>Local lights smaller on screen than this radius in pixels cast no shadow, fading in up to twice it.</summary>
    public float MinScreenRadius = 16;

    /// <summary>Milliseconds a frame may spend preparing shadow draws, the most important lights first and always at least one.
    /// Lights left waiting keep their last shadow, or none yet.</summary>
    public float DrawMilliseconds = 2.5f;

    internal ShadowSettings Copy() => (ShadowSettings)MemberwiseClone();
}

/// <summary>What a light's shadow is drawn from besides its casters.</summary>
internal readonly record struct ShadowInputs(LightType Type, Double3 Position, Float3 Direction, Float3 Up, float Range, float OuterAngle, uint Resolution)
{
    public static ShadowInputs Of(in LightDesc desc) => desc.Type == LightType.Point
        ? new(desc.Type, desc.Position, default, default, desc.Range, 0, desc.Shadow.Resolution)
        : new(desc.Type, desc.Position, desc.Direction, desc.Up, desc.Range, desc.OuterAngle, desc.Shadow.Resolution);
}

internal readonly record struct FaceStamp(ShadowInputs Key, int Size, int PageSize, float Fade, float Strength, float DepthBias, float NormalBias, ShadowFilter Filter);

/// <summary>Shadowed lights by light index, iterated densely.</summary>
internal sealed class ShadowLights
{
    private LightShadow?[] _byIndex = [];
    private readonly List<(int Index, LightShadow Light)> _list = new();

    public LightShadow this[int index] => _byIndex[index]!;

    public bool ContainsKey(int index) => index < _byIndex.Length && _byIndex[index] != null;

    public bool TryGetValue(int index, out LightShadow light)
    {
        light = index < _byIndex.Length ? _byIndex[index]! : null!;
        return light != null;
    }

    public void Add(int index, LightShadow light)
    {
        if (index >= _byIndex.Length)
            Array.Resize(ref _byIndex, Math.Max(index + 1, _byIndex.Length * 2));
        _byIndex[index] = light;
        light.ListAt = _list.Count;
        _list.Add((index, light));
    }

    public void Remove(int index)
    {
        LightShadow light = _byIndex[index]!;
        _byIndex[index] = null;
        (int, LightShadow) last = _list[^1];
        _list[light.ListAt] = last;
        last.Item2.ListAt = light.ListAt;
        _list.RemoveAt(_list.Count - 1);
    }

    public List<(int Index, LightShadow Light)>.Enumerator GetEnumerator() => _list.GetEnumerator();

    public IEnumerable<LightShadow> Values
    {
        get
        {
            foreach ((_, LightShadow light) in _list)
                yield return light;
        }
    }
}

/// <summary>A square of a page, in texels.</summary>
internal readonly record struct ShadowTile(int X, int Y, int Size)
{
    public bool IsValid => Size > 0;
}

/// <summary>Power of two squares packed into a square page by halving, merged back as all four quarters free up.</summary>
internal sealed class ShadowTileAllocator
{
    public const int SmallestTile = 16;

    // Free squares of each size, the lowest row then leftmost first so tiles pack toward one corner
    private readonly Dictionary<int, SortedSet<long>> _free = new();

    public ShadowTileAllocator(int size) => Reset(size);

    public int Size { get; private set; }
    public long UsedTexels { get; private set; }

    public void Reset(int size)
    {
        Size = size;
        UsedTexels = 0;
        foreach (SortedSet<long> set in _free.Values)
            set.Clear();
        FreeSet(size).Add(Pack(0, 0));
    }

    /// <summary>The power of two a request rounds up to.</summary>
    public static int RoundUp(int size)
    {
        int s = SmallestTile;
        while (s < size && s < 1 << 30)
            s <<= 1;
        return s;
    }

    public bool TryAllocate(int size, out ShadowTile tile)
    {
        size = RoundUp(size);
        int s = size;
        while (s <= Size && FreeSet(s).Count == 0)
            s <<= 1;
        if (s > Size)
        {
            tile = default;
            return false;
        }

        SortedSet<long> set = FreeSet(s);
        long key = set.Min;
        set.Remove(key);
        (int x, int y) = Unpack(key);
        while (s > size)
        {
            s >>= 1;
            SortedSet<long> half = FreeSet(s);
            half.Add(Pack(x + s, y));
            half.Add(Pack(x, y + s));
            half.Add(Pack(x + s, y + s));
        }
        UsedTexels += (long)size * size;
        tile = new ShadowTile(x, y, size);
        return true;
    }

    /// <summary>Takes a given square if it is free, so tiles freed to try something can go back where they were.</summary>
    public bool TryClaim(ShadowTile tile)
    {
        for (int s = tile.Size; s <= Size; s <<= 1)
        {
            int x = tile.X - tile.X % s, y = tile.Y - tile.Y % s;
            if (!FreeSet(s).Remove(Pack(x, y)))
                continue;
            while (s > tile.Size)
            {
                s >>= 1;
                int cx = tile.X - tile.X % s, cy = tile.Y - tile.Y % s;
                for (int i = 0; i < 4; i++)
                {
                    int qx = x + (i & 1) * s, qy = y + (i >> 1) * s;
                    if (qx != cx || qy != cy)
                        FreeSet(s).Add(Pack(qx, qy));
                }
                (x, y) = (cx, cy);
            }
            UsedTexels += (long)tile.Size * tile.Size;
            return true;
        }
        return false;
    }

    public void Free(ShadowTile tile)
    {
        if (!tile.IsValid)
            return;
        int x = tile.X, y = tile.Y, s = tile.Size;
        UsedTexels -= (long)s * s;
        while (s < Size)
        {
            int parent = s << 1;
            int px = x - x % parent, py = y - y % parent;
            SortedSet<long> set = FreeSet(s);
            bool siblingsFree = true;
            for (int i = 0; i < 4 && siblingsFree; i++)
            {
                int cx = px + (i & 1) * s, cy = py + (i >> 1) * s;
                if (cx != x || cy != y)
                    siblingsFree = set.Contains(Pack(cx, cy));
            }
            if (!siblingsFree)
                break;
            for (int i = 0; i < 4; i++)
            {
                int cx = px + (i & 1) * s, cy = py + (i >> 1) * s;
                if (cx != x || cy != y)
                    set.Remove(Pack(cx, cy));
            }
            (x, y, s) = (px, py, parent);
        }
        FreeSet(s).Add(Pack(x, y));
    }

    private SortedSet<long> FreeSet(int size)
    {
        if (!_free.TryGetValue(size, out SortedSet<long>? set))
            _free[size] = set = new SortedSet<long>();
        return set;
    }

    private static long Pack(int x, int y) => ((long)y << 32) | (uint)x;

    private static (int X, int Y) Unpack(long key) => ((int)(key & 0xFFFFFFFF), (int)(key >> 32));
}

/// <summary>
/// One face of a local light's shadow as shaders read it, 48 bytes, the three vec4s Graphite_ShadowFaces holds per face.
/// Six per light. A cube face's clip space is worked out in the shader from its near and far; a spot light uses its first
/// face, and its second and third hold its light to clip matrix.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShadowFace
{
    /// <summary>The tile's origin and size in page uv, then the world size of a texel one unit from the light.</summary>
    public Float4 Rect;

    /// <summary>The page's 2D texture slot as uint bits, the light's fade, one over the page size, unused.</summary>
    public Float4 Params;

    /// <summary>The near and far distance along the face's axis, unused.</summary>
    public Float4 Depth;

    public static bool Same(in ShadowFace a, in ShadowFace b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<ShadowFace>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<ShadowFace>(in b)));
}

/// <summary>One cascade of a directional light in a view as shaders read it, 128 bytes, matching Graphite_Cascade. Four per light.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CascadeRecord
{
    /// <summary>From a camera relative position to the cascade's clip space, reversed Z.</summary>
    public Float4x4 ViewToClip;

    /// <summary>The camera relative centre and radius of the sphere it covers.</summary>
    public Float4 Sphere;

    /// <summary>The texture's 2D slot as uint bits, one over its size, the light's cascade count, the blend fraction.</summary>
    public Float4 Params;

    /// <summary>The view's forward and the shadow distance along it.</summary>
    public Float4 Fade;

    /// <summary>The world distance along the light from depth 1 to depth 0, unused.</summary>
    public Float4 Depth;
}

/// <summary>A depth page of a world's local light tiles, live ones or the static only copies, in the bindless table.</summary>
internal sealed class ShadowPage(PersistentTexture persistent, bool isStatic, int size)
{
    public PersistentTexture Persistent { get; } = persistent;
    public RenderTexture Texture => Persistent.Texture!;
    public RenderResourceID Id => Persistent.Id;
    public uint Slot => Persistent.Slot;
    public bool IsStatic { get; } = isStatic;
    public ShadowTileAllocator Tiles { get; } = new(size);
    public readonly List<ShadowDraw> Draws = new();
    public int LastUsed;
}

/// <summary>A shadow view drawn into a tile this frame, the tile cleared first unless a static copy already fills it.</summary>
internal readonly record struct ShadowDraw(CameraFrame Camera, ShadowTile Tile, bool Clear, int Light = -1, int Face = 0, bool Cascade = false);

/// <summary>A tile read down through the scratch page into its smaller place.</summary>
internal readonly record struct ShadowShrink(ShadowPage From, ShadowTile FromTile, ShadowTile Scratch, ShadowPage To, ShadowTile ToTile);

/// <summary>A static only tile copied into its light's live tile.</summary>
internal readonly record struct ShadowCopy(ShadowPage From, ShadowTile FromTile, ShadowPage To, ShadowTile ToTile);

internal static class ShadowRecorder
{
    public static readonly TargetLoadStoreOps Loaded = new(AttachmentOps.Loaded, AttachmentOps.Loaded);

    public static GraphTextureDesc Desc(int size, PixelFormat format) => new()
    {
        Width = size,
        Height = size,
        ColorFormats = [],
        EnableDepth = true,
        DepthFormat = format,
    };

    public static void Record(CommandBuffer cmd, RenderTexture texture, List<ShadowDraw> draws, GraphicsProgram clear, uint ring)
    {
        if (draws.Count == 0)
            return;
        cmd.SetFramebuffer(texture.Framebuffer, Loaded);
        foreach ((CameraFrame camera, ShadowTile tile, bool clearFirst, int light, int face, bool cascade) in draws)
        {
            bool group = light >= 0 && cmd.WantsDebugMeta;
            if (group)
                cmd.PushDebugGroup(cascade ? $"Cascade {face}" : $"Shadow face {face}", DebugGroups.ShadowFace(camera.World.World, light, face, cascade, tile));
            cmd.SetViewport(new Viewport(tile.X, tile.Y, tile.Size, tile.Size, 0, 1));
            cmd.SetScissor((uint)tile.X, (uint)tile.Y, (uint)tile.Size, (uint)tile.Size);
            if (clearFirst)
            {
                cmd.SetShader(clear);
                cmd.SetVertexSource(VertexSource.None);
                cmd.Draw(3);
            }
            camera.Drawer.Prepare(ring);
            if (camera.FindList(PassType.ShadowCaster) is { } list)
                camera.Drawer.DrawList(cmd, list);
            if (group)
                cmd.PopDebugGroup();
        }
    }

    internal static ShaderPass TileClearPass()
    {
        const string source = """
            Vertex
            {
            #include "Graphite/Fullscreen.glsl"
            }
            Fragment
            {
            #define GRAPHITE_NO_SOURCE 1
            #include "Graphite/Fullscreen.glsl"
            void main() { }
            }
            """;
        return new ShaderPass { State = new PassState(), Source = source, SourceName = "Graphite/ShadowTileClear" };
    }

    /// <summary>Draws the far plane over a tile, depth only, so one tile clears without touching its neighbours.</summary>
    public static GraphicsProgram TileClear(GraphicsDevice device, IShaderCompiler? compiler)
    {
        ShaderDescription description = TileClearPass().Variants(compiler, device.BackendType).Require();
        description.BlendState = BlendStateDescription.Empty;
        description.DepthStencilState = new DepthStencilStateDescription(true, true, ComparisonKind.Always);
        description.RasterizerState = RasterizerStateDescription.CullNone;
        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
        program.Name = "Graphite/ShadowTileClear";
        return program;
    }
}

/// <summary>The passes that draw a world's shadow pages, in the order they run.</summary>
internal static class ShadowPasses
{
    private static readonly List<TextureBlit> s_blits = new();

    public static void Add(FramePipeline frame, FrameGraph graph, WorldFrame world)
    {
        WorldShadows shadows = world.Shadows!;
        if (shadows.Pages.Count == 0 && shadows.StaticPages.Count == 0)
            return;
        if (shadows.Shrinks.Count > 0)
            Shrink(frame, graph, world);
        foreach (ShadowPage page in shadows.StaticPages)
            Draw(frame, graph, world, page);
        if (shadows.Copies.Count > 0)
            Copies(graph, world);
        foreach (ShadowPage page in shadows.Pages)
            Draw(frame, graph, world, page);
    }

    // Each page's shrinking tiles blitted at their new size into a scratch page in one command, each texel the nearest old one
    // so depths never blend, which is what drawing at the smaller size gives. Then the scratch tiles land in their new places,
    // before any page draws.
    private static void Shrink(FramePipeline frame, FrameGraph graph, WorldFrame world)
    {
        WorldShadows shadows = world.Shadows!;
        TextureHandle scratch = default;
        graph.AddPass("Graphite.ShadowShrink", builder =>
        {
            List<(ShadowPage Page, TextureHandle Handle)> pages = new();
            foreach (ShadowPage page in shadows.AllPages())
                pages.Add((page, builder.Read(graph.Import(page.Texture), TextureState.TransferSrc, TextureState.TransferSrc)));
            scratch = builder.Write(graph.Create(shadows.ScratchId, ShadowRecorder.Desc(shadows.PageSize / 2, frame.ShadowFormat)), TextureState.TransferDst, TextureState.TransferDst);
            return (pages, scratch);
        }, (context, data) =>
        {
            Texture target = context.Get(data.scratch).DepthTexture!;
            foreach ((ShadowPage page, TextureHandle handle) in data.pages)
            {
                s_blits.Clear();
                foreach (ShadowShrink shrink in shadows.Shrinks)
                {
                    if (shrink.From == page)
                        s_blits.Add(new TextureBlit((uint)shrink.FromTile.X, (uint)shrink.FromTile.Y, (uint)shrink.FromTile.Size, (uint)shrink.FromTile.Size, (uint)shrink.Scratch.X, (uint)shrink.Scratch.Y, (uint)shrink.Scratch.Size, (uint)shrink.Scratch.Size));
                }
                if (s_blits.Count > 0)
                    context.Command.BlitTexture(context.Get(handle).DepthTexture!, target, CollectionsMarshal.AsSpan(s_blits));
            }
        });
        graph.AddPass("Graphite.ShadowShrinkCopies", builder =>
        {
            TextureHandle source = builder.Read(scratch, TextureState.TransferSrc, TextureState.TransferSrc);
            List<(ShadowPage Page, TextureHandle Handle)> pages = new();
            foreach (ShadowPage page in shadows.AllPages())
                pages.Add((page, builder.Write(graph.Import(page.Texture), TextureState.TransferDst, TextureState.TransferDst)));
            return (pages, source);
        }, (context, data) =>
        {
            Texture source = context.Get(data.source).DepthTexture!;
            foreach ((ShadowPage page, TextureHandle handle) in data.pages)
            {
                s_blits.Clear();
                foreach (ShadowShrink shrink in shadows.Shrinks)
                {
                    if (shrink.To == page)
                        s_blits.Add(new TextureBlit((uint)shrink.Scratch.X, (uint)shrink.Scratch.Y, (uint)shrink.Scratch.Size, (uint)shrink.Scratch.Size, (uint)shrink.ToTile.X, (uint)shrink.ToTile.Y, (uint)shrink.ToTile.Size, (uint)shrink.ToTile.Size));
                }
                if (s_blits.Count > 0)
                    context.Command.BlitTexture(source, context.Get(handle).DepthTexture!, CollectionsMarshal.AsSpan(s_blits));
            }
        });
    }

    // Draws the casters of the tiles queued on one page
    private static void Draw(FramePipeline frame, FrameGraph graph, WorldFrame world, ShadowPage page)
    {
        if (page.Draws.Count == 0)
            return;
        graph.AddPass(page.IsStatic ? "Graphite.ShadowStaticPage" : "Graphite.ShadowPage", builder =>
        {
            frame.Buffers.ReadWorld(graph, builder, world);
            frame.DeclareShadowReads(graph, builder, page.Texture);
            foreach (ShadowDraw draw in page.Draws)
                draw.Camera.Buffers.Read(graph, builder, BufferAccess.AllReads);
            return builder.Write(graph.Import(page.Texture), TextureState.Attachment);
        }, (context, handle) => ShadowRecorder.Record(context.Command, context.Get(handle), page.Draws, frame.TileClearProgram, context.Task.RingSlot));
    }

    // Copies static only tiles into their lights' live tiles, after the static pages draw and before the live ones
    private static void Copies(FrameGraph graph, WorldFrame world)
    {
        WorldShadows shadows = world.Shadows!;
        graph.AddPass("Graphite.ShadowCopies", builder =>
        {
            Dictionary<ShadowPage, TextureHandle> pages = new();
            foreach (ShadowPage page in shadows.AllPages())
            {
                TextureHandle texture = graph.Import(page.Texture);
                pages[page] = page.IsStatic
                    ? builder.Read(texture, TextureState.TransferSrc, TextureState.TransferSrc)
                    : builder.Write(texture, TextureState.TransferDst, TextureState.TransferDst);
            }
            return pages;
        }, (context, pages) =>
        {
            foreach (ShadowCopy copy in shadows.Copies)
            {
                Texture source = context.Get(pages[copy.From]).DepthTexture!;
                Texture target = context.Get(pages[copy.To]).DepthTexture!;
                context.Command.CopyTexture(source, (uint)copy.FromTile.X, (uint)copy.FromTile.Y, 0, 0, 0, target, (uint)copy.ToTile.X, (uint)copy.ToTile.Y, 0, 0, 0, (uint)copy.ToTile.Size, (uint)copy.ToTile.Size, 1, 1);
            }
        });
    }
}

/// <summary>
/// Draws a view's refit cascades into their depth textures and publishes them as <see cref="RendererOutputs.Cascades"/>.
/// Exclusive group "Cascades".
/// </summary>
public sealed class CascadeFeature : RenderFeature
{
    internal CascadeFeature() { }

    private sealed class State
    {
        public readonly List<(ViewShadows.Cascade Cascade, TextureHandle Handle)> Targets = new();
    }

    public override RenderSlot Slot => RenderSlot.Shadows;
    public override string? ExclusiveGroup => "Cascades";
    public override ViewKinds RunsOn => ViewKinds.All;

    private static ViewShadows.Cascade? First(CameraFrame camera)
    {
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                if (light.Cascades[c].Texture?.Texture != null)
                    return light.Cascades[c];
            }
        }
        return null;
    }

    internal override bool Skips(ViewGraph view) => view.Probing || First(view.Camera) == null;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        ViewGraph graph = builder.ViewGraph;
        CameraFrame camera = graph.Camera;
        State state = builder.GetViewState<State>();
        state.Targets.Clear();
        graph.Frame.Buffers.ReadWorld(graph.Graph, builder.Graph, camera.World);
        List<RenderTexture> written = new();
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                if (light.Cascades[c].Draws.Count > 0)
                    written.Add(light.Cascades[c].Texture!.Texture!);
            }
        }
        graph.Frame.DeclareShadowReads(graph.Graph, builder.Graph, alsoWritten: written);
        foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
        {
            for (int c = 0; c < light.Count; c++)
            {
                ViewShadows.Cascade cascade = light.Cascades[c];
                if (cascade.Draws.Count == 0)
                    continue;
                foreach (ShadowDraw draw in cascade.Draws)
                    draw.Camera.Buffers.Read(graph.Graph, builder.Graph, BufferAccess.AllReads);
                state.Targets.Add((cascade, builder.Graph.Write(graph.Graph.Import(cascade.Texture!.Texture!), TextureState.Attachment)));
            }
        }
        builder.Publish(RendererOutputs.Cascades, state.Targets.Count > 0 ? state.Targets[0].Handle : graph.Graph.Import(First(camera)!.Texture!.Texture!));
    }

    public override void Render(FeatureContext context)
    {
        foreach ((ViewShadows.Cascade cascade, TextureHandle handle) in context.GetViewState<State>().Targets)
            ShadowRecorder.Record(context.Command, context.Get(handle), cascade.Draws, context.Frame.TileClearProgram, context.Graph.Task.RingSlot);
    }
}

/// <summary>
/// A local light's cached shadow: a tile per face that persists and redraws only when the light, its tile or a caster
/// near it changed. A light with both static and moving casters keeps static only copies, so moving changes copy them
/// back and draw the moving casters alone.
/// </summary>
internal sealed class LightShadow
{
    public int Faces;

    /// <summary>The size of its tiles.</summary>
    public int Size;
    public float Fade;
    public ShadowInputs Key;
    public int LastSeen;
    public int ListAt;

    // What its faces were last written from
    public FaceStamp Stamp;
    public bool Stamped;
    public readonly ShadowTile[] StampTiles = new ShadowTile[6];
    public readonly uint[] StampSlots = new uint[6];

    /// <summary>Static and moving casters its views held at the last count, for deciding again whether to split.</summary>
    public int Statics, Movings;

    /// <summary>Whether it would have split but the frame had no room, so a later frame decides again.</summary>
    public bool SplitDenied;
    public bool Dirty = true, StaticDirty = true, Split, EveryFrame, StaticEveryFrame, ProviderCasts, Seen;

    /// <summary>Whether a moving caster or provider reached it since it last drew, which decides again whether it splits.</summary>
    public bool MovingChanged;

    /// <summary>Whether this frame's plan leaves room for a static copy.</summary>
    public bool SplitAllowed = true;
    public readonly ShadowPage?[] Pages = new ShadowPage?[6];
    public readonly ShadowTile[] Tiles = new ShadowTile[6];
    public readonly ShadowPage?[] StaticPages = new ShadowPage?[6];
    public readonly ShadowTile[] StaticTiles = new ShadowTile[6];
    /// <summary>The view and frustum each face last drew with, for asking providers whether they cast into it.</summary>
    public readonly ViewDesc[] Views = new ViewDesc[6];
    public readonly CullFrustum[] Culls = new CullFrustum[6];

    /// <summary>The shaders its casters last drew with, and its static copy's, so a global keyword they read redraws them.</summary>
    public readonly List<Shader> Shaders = new(), StaticShaders = new();
    public readonly ShadowFace[] Written = new ShadowFace[6];

    /// <summary>Its priority this frame, frames it waited on the draw budget, and whether its tiles hold a drawing.</summary>
    public float Priority;
    public int Waited;
    public bool Drawn;

    public bool HasTiles => Pages[0] != null;
}

/// <summary>
/// Shadowed lights in grids of cells doubling in size, each light in the level whose cells are as wide as it, so a change
/// finds the lights its sphere touches by looking only at the cells around it.
/// </summary>
internal sealed class LightGrid
{
    private const float BaseCell = 16;
    private readonly Dictionary<(int Level, long X, long Y, long Z), List<int>> _cells = new();
    private readonly List<List<int>> _pool = new();
    private readonly List<int>[] _levels = new List<int>[48];
    private readonly HashSet<int> _found = new();

    // Lights too big for any level's cells, which every change looks at
    private readonly List<int> _everywhere = new();

    public LightGrid()
    {
        for (int l = 0; l < _levels.Length; l++)
            _levels[l] = new List<int>();
    }

    public void Clear()
    {
        foreach (List<int> cell in _cells.Values)
        {
            cell.Clear();
            _pool.Add(cell);
        }
        _cells.Clear();
        foreach (List<int> level in _levels)
            level.Clear();
        _everywhere.Clear();
    }

    public void Add(int light, Double3 center, float radius)
    {
        int level = Level(radius);
        if (!Bounded(level, center, radius))
        {
            _everywhere.Add(light);
            return;
        }
        _levels[level].Add(light);
        (long x0, long y0, long z0, long x1, long y1, long z1) = Cells(level, center, radius);
        for (long x = x0; x <= x1; x++)
        {
            for (long y = y0; y <= y1; y++)
            {
                for (long z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue((level, x, y, z), out List<int>? list))
                    {
                        list = _pool.Count > 0 ? _pool[^1] : new List<int>();
                        if (_pool.Count > 0)
                            _pool.RemoveAt(_pool.Count - 1);
                        _cells[(level, x, y, z)] = list;
                    }
                    list.Add(light);
                }
            }
        }
    }

    /// <summary>The lights in cells a sphere touches, each once, to test exactly.</summary>
    public HashSet<int> Near(Double3 center, float radius)
    {
        _found.Clear();
        foreach (int light in _everywhere)
            _found.Add(light);
        for (int level = 0; level < _levels.Length; level++)
        {
            List<int> lights = _levels[level];
            if (lights.Count == 0)
                continue;

            // A sphere covering more cells than the level has lights, or no finite number, looks at its lights directly
            if (!Bounded(level, center, radius) || CellCount(level, center, radius) >= lights.Count)
            {
                foreach (int light in lights)
                    _found.Add(light);
                continue;
            }
            (long x0, long y0, long z0, long x1, long y1, long z1) = Cells(level, center, radius);
            for (long x = x0; x <= x1; x++)
            {
                for (long y = y0; y <= y1; y++)
                {
                    for (long z = z0; z <= z1; z++)
                    {
                        if (!_cells.TryGetValue((level, x, y, z), out List<int>? list))
                            continue;
                        foreach (int light in list)
                            _found.Add(light);
                    }
                }
            }
        }
        return _found;
    }

    private int Level(float radius)
    {
        int level = 0;
        while (level < _levels.Length - 1 && CellSize(level) < 2 * radius)
            level++;
        return level;
    }

    private static double CellSize(int level) => BaseCell * Math.Pow(2, level);

    // Cells a sphere spans at a level, worked out in double so huge spheres cannot wrap
    private static double CellCount(int level, Double3 center, float radius)
    {
        double cell = CellSize(level);
        double Span(double c) => Math.Floor((c + radius) / cell) - Math.Floor((c - radius) / cell) + 1;
        return Span(center.X) * Span(center.Y) * Span(center.Z);
    }

    // Whether a sphere's cells at a level are finite and few enough to walk
    private static bool Bounded(int level, Double3 center, float radius)
        => float.IsFinite(radius) && double.IsFinite(center.X) && double.IsFinite(center.Y) && double.IsFinite(center.Z) && CellCount(level, center, radius) <= 1 << 20;

    private static (long, long, long, long, long, long) Cells(int level, Double3 center, float radius)
    {
        double cell = CellSize(level);
        return ((long)Math.Floor((center.X - radius) / cell), (long)Math.Floor((center.Y - radius) / cell), (long)Math.Floor((center.Z - radius) / cell),
            (long)Math.Floor((center.X + radius) / cell), (long)Math.Floor((center.Y + radius) / cell), (long)Math.Floor((center.Z + radius) / cell));
    }
}

/// <summary>One shadowed light as a frame plans its tile: how much it wants, what it holds and the size it gets.</summary>
internal sealed class ShadowPlan
{
    public int Light;
    public int Faces;

    /// <summary>Its radius on screen in pixels in the view that sees it biggest, which orders lights when room runs out.</summary>
    public float Priority;

    /// <summary>The size it wants at full quality, that size under the world's scale, and the largest it may have.</summary>
    public float Wanted, Desired;
    public int MaxTile;

    /// <summary>The size it holds, 0 for none, and whether it keeps a static copy as well.</summary>
    public int Held;
    public bool Split;

    /// <summary>The size it gets this frame, 0 for no shadow.</summary>
    public int Target;
}

/// <summary>
/// How a world's local light tiles are sized: a tile per light from its size on screen, kept until the
/// wish moves three quarters of an octave from it, every light scaling down together on a square root of two ladder
/// when they do not fit in four fifths of the budget, and back up one step after fitting a step higher for 30 frames.
/// </summary>
internal sealed class ShadowPlanner
{
    public const float ScaleStep = 1.41421356f;
    public const int FramesBeforeScaleRises = 30;
    public const float Hysteresis = 0.75f;
    public const float FitHeadroom = 0.8f;

    private int _level, _framesAbove;

    /// <summary>The scale every light's wanted size is multiplied by.</summary>
    public float Scale => MathF.Pow(ScaleStep, -_level);

    public static int RoundPow2(float size, int minTile, int maxTile)
    {
        int level = (int)MathF.Round(MathF.Log2(Math.Max(size, 1f)));
        return Math.Clamp(1 << Math.Clamp(level, 0, 30), minTile, maxTile);
    }

    /// <summary>The size a light gets: what it holds while its desired size stays near it, else the nearest power of two.</summary>
    public static int Keep(float desired, int held, int minTile, int maxTile)
    {
        int rounded = RoundPow2(desired, minTile, maxTile);
        if (held <= 0 || held > maxTile || held < minTile)
            return rounded;
        float apart = MathF.Abs(MathF.Log2(desired) - MathF.Log2(held));
        return apart < Hysteresis ? held : rounded;
    }

    /// <summary>Wanted and desired sizes and targets for a frame, the plans sorted most important first, fitted to a capacity in texels.</summary>
    public void Plan(List<ShadowPlan> plans, long capacity, int minTile)
    {
        foreach (ShadowPlan plan in plans)
            plan.Wanted = Math.Clamp(plan.Wanted, minTile, plan.MaxTile);

        // The scale first, so lights coming back into view after a while land on the sizes they hold rather than dropping
        // to fit a scale that rose while they were away. With nothing to plan it stays where it is.
        if (plans.Count > 0)
            UpdateScale(plans, capacity, minTile);
        float scale = Scale;
        foreach (ShadowPlan plan in plans)
        {
            plan.Desired = Math.Clamp(plan.Wanted * scale, minTile, plan.MaxTile);
            plan.Target = Keep(plan.Desired, plan.Held, minTile, plan.MaxTile);
        }
        plans.Sort(static (a, b) => b.Priority != a.Priority ? b.Priority.CompareTo(a.Priority) : a.Light.CompareTo(b.Light));
        Fit(plans, capacity, minTile);
    }

    // Live tiles only, static copies have pages of their own
    private static long Texels(ShadowPlan plan) => (long)plan.Faces * plan.Target * plan.Target;

    // Sizes held above what is wanted give way first, then the least important lights halve, then they drop
    private static void Fit(List<ShadowPlan> plans, long capacity, int minTile)
    {
        long sum = 0;
        foreach (ShadowPlan plan in plans)
            sum += Texels(plan);
        if (sum <= capacity)
            return;

        for (int i = plans.Count - 1; i >= 0 && sum > capacity; i--)
        {
            ShadowPlan plan = plans[i];
            int rounded = RoundPow2(plan.Desired, minTile, int.MaxValue);
            if (plan.Target <= rounded)
                continue;
            sum -= Texels(plan);
            plan.Target = rounded;
            sum += Texels(plan);
        }

        bool halved = true;
        while (sum > capacity && halved)
        {
            halved = false;
            for (int i = plans.Count - 1; i >= 0 && sum > capacity; i--)
            {
                ShadowPlan plan = plans[i];
                if (plan.Target <= minTile)
                    continue;
                sum -= Texels(plan);
                plan.Target /= 2;
                sum += Texels(plan);
                halved = true;
            }
        }

        for (int i = plans.Count - 1; i >= 0 && sum > capacity; i--)
        {
            sum -= Texels(plans[i]);
            plans[i].Target = 0;
        }
    }

    // The scale drops as soon as the wanted sizes stop fitting, and climbs a step only after they would have fit a step higher for a while
    private void UpdateScale(List<ShadowPlan> plans, long capacity, int minTile)
    {
        long TexelsAt(float scale)
        {
            long sum = 0;
            foreach (ShadowPlan plan in plans)
            {
                int t = RoundPow2(Math.Clamp(plan.Wanted * scale, minTile, plan.MaxTile), minTile, plan.MaxTile);
                sum += (long)plan.Faces * t * t;
            }
            return sum;
        }

        float current = Scale;
        long now = TexelsAt(current);
        if (now > capacity)
        {
            // Down until it fits, but no further than every light at the smallest tile, so a load too big even then cannot sink it without end
            long floor = TexelsAt(0);
            int steps = 0;
            for (float scale = current; now > capacity && now > floor && steps < 24; steps++)
            {
                scale /= ScaleStep;
                now = TexelsAt(scale);
            }
            _level += steps;
            _framesAbove = 0;
        }
        else if (_level > 0 && TexelsAt(current * ScaleStep) <= capacity)
        {
            if (++_framesAbove >= FramesBeforeScaleRises)
            {
                _level--;
                _framesAbove = 0;
            }
        }
        else
        {
            _framesAbove = 0;
        }
    }
}

/// <summary>A world's local light shadows on the render thread: its pages, every shadowed light's cache, the grid finding them.</summary>
internal sealed class WorldShadows(TableHost tables, int worldId)
{
    public int WorldId { get; } = worldId;

    public const int FacesPerLight = 6;

    public GpuTable Faces { get; } = tables.Of<ShadowFace>("Faces", 64 * FacesPerLight);
    public readonly List<ShadowPage> Pages = new();
    public readonly List<ShadowPage> StaticPages = new();
    public readonly ShadowLights Lights = new();
    public readonly LightGrid Grid = new();
    public readonly ShadowPlanner Planner = new();
    public int PageSize;

    /// <summary>The shrinks and static copies this frame's plan queued, which the world's passes record.</summary>
    public readonly List<ShadowShrink> Shrinks = new();
    public readonly List<ShadowCopy> Copies = new();

    /// <summary>The scratch page shrinks read down into, its tiles packed again each frame, and the tiles a shrinking light holds in it, live then static.</summary>
    public ShadowTileAllocator? ScratchTiles;
    public readonly ShadowTile[] ScratchLive = new ShadowTile[6], ScratchStatic = new ShadowTile[6];

    public RenderResourceID ScratchId { get; } = RenderResourceID.Intern($"Graphite.World{worldId}/ShadowScratch");

    /// <summary>Lights whose faces hold nothing, written once.</summary>
    public bool[] Zeroed = [];

    /// <summary>The world's keywords the cached shadows were drawn with.</summary>
    public IReadOnlyDictionary<string, string>? Keywords;

    public IEnumerable<ShadowPage> AllPages()
    {
        foreach (ShadowPage page in Pages)
            yield return page;
        foreach (ShadowPage page in StaticPages)
            yield return page;
    }

    public RenderResourceID NewPageId(bool isStatic)
    {
        List<ShadowPage> pages = isStatic ? StaticPages : Pages;
        string kind = isStatic ? "ShadowStatic" : "ShadowLive";
        for (int n = 0; ; n++)
        {
            RenderResourceID id = RenderResourceID.Intern($"Graphite.World{worldId}/{kind}{n}");
            if (!pages.Exists(page => page.Id == id))
                return id;
        }
    }
}

/// <summary>The slots of a directional light's cascades in a camera's shape: the light's place in shader order, how many cascades and how big.</summary>
internal readonly record struct CascadeSlot(int Slot, int Count, uint Resolution);

/// <summary>A view's directional cascades: a persistent depth texture each, refit on their stagger, and the records shaders read.</summary>
internal sealed class ViewShadows : IDisposable
{
    public const int MaxCascades = 4;

    public sealed class Cascade
    {
        public PersistentTexture? Texture;
        public CameraFrame? Camera;
        public readonly List<ShadowDraw> Draws = new();

        /// <summary>
        /// What it was last drawn with: the world position of its centre, its radius and its projection times the light's
        /// rotation, so a lagging cascade is read the way it was drawn.
        /// </summary>
        public Double3 Center;
        public float Radius;
        public float DepthRange;
        public Float4x4 Shape;
        public RenderWorld? World;
        public bool Valid;
    }

    /// <summary>A directional light's cascades at its place in shader order, with the light index and settings they were made for.</summary>
    public sealed class CascadeLight(int light, int slot, uint resolution, int count)
    {
        public int Light { get; } = light;
        public int Slot { get; } = slot;
        public uint Resolution { get; } = resolution;
        public int Count { get; } = count;
        public Cascade[] Cascades { get; } = new Cascade[MaxCascades];
    }

    public readonly List<CascadeLight> Lights = new();
    public readonly List<CascadeRecord> Records = new();

    public void Dispose()
    {
        foreach (CascadeLight light in Lights)
        {
            foreach (Cascade cascade in light.Cascades)
            {
                cascade?.Camera?.Dispose();
                cascade?.Texture?.Texture?.Dispose();
            }
        }
        Lights.Clear();
    }
}

/// <summary>The math shadow views are built with.</summary>
internal static class ShadowMath
{
    public const float SplitLogBlend = 0.75f;
    public const float MinSplitNear = 0.1f;

    /// <summary>Where cascade split index of count falls between near and the shadow distance: 0.75 log and 0.25 even.</summary>
    public static float Split(int index, int count, float near, float distance)
    {
        if (index >= count)
            return distance;
        float t = index / (float)count;
        float log = near * MathF.Pow(distance / near, t);
        float even = near + (distance - near) * t;
        return even + (log - even) * SplitLogBlend;
    }

    /// <summary>
    /// The smallest sphere around a slice of a symmetric view between two depths, extents being the tangents of its half
    /// angles (half sizes when orthographic). Its radius depends only on the depths and extents, so it holds still as the
    /// view turns. Returns the centre's depth along the view.
    /// </summary>
    public static float SliceSphere(float near, float far, float extentX, float extentY, bool orthographic, out float radius)
    {
        if (orthographic)
        {
            float halfDepth = (far - near) * 0.5f;
            radius = MathF.Sqrt(halfDepth * halfDepth + extentX * extentX + extentY * extentY);
            return (near + far) * 0.5f;
        }
        float k = extentX * extentX + extentY * extentY;
        float centerDepth = (near + far) * (1f + k) * 0.5f;
        if (centerDepth >= far)
        {
            radius = far * MathF.Sqrt(k);
            return far;
        }
        float toFar = far - centerDepth;
        radius = MathF.Sqrt(toFar * toFar + far * far * k);
        return centerDepth;
    }

    /// <summary>A view matrix whose rows are the directions the image's x, y and depth run along.</summary>
    public static Float4x4 Rows(Float3 x, Float3 y, Float3 z)
        => new(new Float4(x.X, y.X, z.X, 0), new Float4(x.Y, y.Y, z.Y, 0), new Float4(x.Z, y.Z, z.Z, 0), new Float4(0, 0, 0, 1));

    /// <summary>Right, up and forward of a light shining along forward, up made square to it.</summary>
    public static (Float3 Right, Float3 Up, Float3 Forward) Basis(Float3 forward, Float3 up)
    {
        Float3 right = Normalize(Cross(up, forward));
        return (right, Cross(forward, right), forward);
    }

    /// <summary>A reversed Z perspective down +Z with a half angle, between two clip planes.</summary>
    public static Float4x4 Perspective(float halfAngle, float near, float far)
    {
        float s = 1f / MathF.Tan(halfAngle);
        return new Float4x4(new Float4(s, 0, 0, 0), new Float4(0, s, 0, 0), new Float4(0, 0, -near / (far - near), 1), new Float4(0, 0, near * far / (far - near), 0));
    }

    /// <summary>A reversed Z box of half size radius around the origin down +Z: depth 1 at -reach, 0 at +radius.</summary>
    public static Float4x4 Orthographic(float radius, float reach)
        => new(new Float4(1f / radius, 0, 0, 0), new Float4(0, 1f / radius, 0, 0), new Float4(0, 0, -1f / (radius + reach), 0), new Float4(0, 0, radius / (radius + reach), 1));

    public static Float4x4 Orthographic(float radius) => Orthographic(radius, radius);

    /// <summary>
    /// The cascade's centre snapped to its texel grid in the light's space in double, so the map holds still while the
    /// view moves, along the light kept as it is.
    /// </summary>
    public static Double3 Snap(Double3 center, Float3 right, Float3 up, Float3 forward, double texel)
    {
        double x = Dot(center, right), y = Dot(center, up), z = Dot(center, forward);
        x = Math.Round(x / texel) * texel;
        y = Math.Round(y / texel) * texel;
        return new Double3(right.X * x + up.X * y + forward.X * z, right.Y * x + up.Y * y + forward.Y * z, right.Z * x + up.Z * y + forward.Z * z);
    }

    private static double Dot(Double3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Float3 Cross(Float3 a, Float3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static Float3 Normalize(Float3 v)
    {
        float length = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length > 1e-12f ? v / length : new Float3(1, 0, 0);
    }
}

/// <summary>
/// The frame's shadows: every world's local light tiles, sized for the view that wants each biggest, and every view's
/// directional cascades, all drawn by one pipeline after FrameStart and before any view.
/// </summary>
internal sealed partial class FramePipeline
{
    /// <summary>Shadow views drawn and static copies made in the last frame.</summary>
    internal int ShadowViewsDrawn { get; private set; }

    /// <summary>Lights given tiles this frame that held none, and lights whose tiles grew or shrank.</summary>
    internal int ShadowsAdded { get; private set; }
    internal int ShadowsGrown { get; private set; }
    internal int ShadowsShrunk { get; private set; }
    internal int ShadowCopiesMade { get; private set; }
    private GraphicsProgram? _tileClear;
    private readonly List<ShadowPlan> _shadowPlans = new(), _shadowPlanPool = new(), _shadowQueue = new();
    private readonly List<(int Index, LightShadow Light, float Key)> _shadowDraws = new();
    private readonly List<CascadeSlot> _cascadeSlots = new();

    // Cameras faces draw with, rented for a frame each, and one that asks providers about cached faces
    private readonly Stack<CameraFrame> _shadowCameraPool = new();
    private readonly List<CameraFrame> _shadowCamerasDrawn = new();
    private CameraFrame? _providerProbe;

    // The views lights cast for this frame, each with its frustum, first layer and height in pixels
    private readonly List<(CullFrustum Frustum, Double3 Position, float Pixels, bool Orthographic)> _shadowViewers = new();
    private readonly ShadowPage?[] _resizePages = new ShadowPage?[6], _probePages = new ShadowPage?[6];
    private readonly ShadowTile[] _resizeTiles = new ShadowTile[6], _probeTiles = new ShadowTile[6];
    private readonly ShadowPage?[] _oldPages = new ShadowPage?[6];
    private readonly ShadowTile[] _oldTiles = new ShadowTile[6];
    private bool? _blitShadows;

    // Whether the device blits the shadow format, else shrinking lights draw again
    private bool BlitShadows => _blitShadows ??= _device.SupportsBlit(ShadowFormat, depthFormat: true);

    // The largest power of two shadow map up to a cap that the device can make, at least the smallest one asked for
    private uint LargestShadowMap(uint cap) => Math.Max(256u, Math.Min(cap, 1u << BitOperations.Log2(Math.Max(_device.Limits.MaxTextureDimension2D, 1u))));
    private readonly List<IDisposable> _shadowRetired = new();
    private Sampler? _shadowSampler;

    /// <summary>Clears one tile of a depth target to the far plane.</summary>
    public GraphicsProgram TileClearProgram => _tileClear ??= ShadowRecorder.TileClear(_device, _compiler);

    /// <summary>The materials whose draws or records changed this frame, whose casters' lights draw again.</summary>
    public List<Material> ChangedMaterials { get; set; } = new();

    /// <summary>The depth format of shadow pages and cascades.</summary>
    public PixelFormat ShadowFormat { get; init; } = PixelFormat.D16_UNorm;

    /// <summary>The comparison sampler every shadow read goes through: linear, clamped, lit where the receiver is nearer.</summary>
    public Sampler ShadowSampler => _shadowSampler ??= _device.ResourceFactory.CreateSampler(SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
        MipFilter = FilterMode.Point,
        ComparisonKind = ComparisonKind.GreaterEqual,
    });

    // Plans and prepares the frame's local light shadows, after every host view is prepared, and the cascades of every view
    private void PrepareShadows()
    {
        ShadowViewsDrawn = 0;
        ShadowsAdded = ShadowsGrown = ShadowsShrunk = 0;
        ShadowCopiesMade = 0;
        foreach (WorldFrame world in _frameWorlds)
        {
            PrepareLocalShadows(world);
            ShadowCopiesMade += world.Shadows!.Copies.Count;
        }
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                PrepareCascades(camera, FitOf(camera.Desc));
        }
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
            {
                if (camera.LastFrame == _frame)
                    PrepareCascades(camera, FitOf(camera.Desc));
            }
        }
    }

    private enum CascadeFit
    {
        None,

        /// <summary>Fitted to slices of the view, refit on their stagger.</summary>
        View,

        /// <summary>Spheres around the view's position, for captures looking every way, refit each time.</summary>
        Around,
    }

    // Views people look at fit cascades to what they see
    private static bool Casts(in ViewDesc view) => ((ViewKinds)(1 << (int)view.Kind) & ViewKinds.Cameras) != 0;

    private void PrepareLocalShadows(WorldFrame world)
    {
        WorldShadows shadows = world.Shadows ??= new WorldShadows(new TableHost(_device, _store, $"Graphite.World{world.World.Id}.Shadows"), world.World.Id);
        ShadowSettings settings = world.World.ShadowSnapshot;
        WorldLighting lighting = world.World.Lighting;
        int pageSize = (int)Math.Clamp(BitOperations.RoundUpToPowerOf2((uint)Math.Max(settings.PageSize, 256)), 256u, LargestShadowMap(16384u));
        shadows.Shrinks.Clear();
        shadows.Copies.Clear();
        shadows.ScratchTiles?.Reset(shadows.ScratchTiles.Size);
        if (shadows.PageSize != pageSize)
        {
            foreach ((int index, LightShadow light) in shadows.Lights)
                ReleaseShadow(shadows, index, light, keepState: true);
            RetirePages(shadows, shadows.Pages, all: true);
            RetirePages(shadows, shadows.StaticPages, all: true);
            shadows.PageSize = pageSize;
        }
        foreach (ShadowPage page in shadows.AllPages())
        {
            page.Draws.Clear();
            ResolvePage(page);
        }

        InvalidateShadows(world, shadows, lighting);
        IReadOnlyDictionary<string, string> keywords = world.World.Globals.KeywordSnapshot;
        if (!ReferenceEquals(shadows.Keywords, keywords))
        {
            IReadOnlyDictionary<string, string>? before = shadows.Keywords;
            shadows.Keywords = keywords;
            foreach ((_, LightShadow light) in shadows.Lights)
            {
                light.Dirty |= ReadsChangedKeyword(light.Shaders, before, keywords);
                light.StaticDirty |= ReadsChangedKeyword(light.StaticShaders, before, keywords);
            }
        }

        // Which lights cast this frame, how important each is and the size it wants
        foreach ((_, LightShadow light) in shadows.Lights)
            light.Seen = false;
        _shadowViewers.Clear();
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame != _frame || camera.World != world || !Casts(camera.Desc))
                continue;
            ViewLayer layer = camera.Desc.Layers[0];
            float pixels = camera.View.PixelHeight * 0.5f * MathF.Abs(layer.Projection.c1.Y);
            _shadowViewers.Add((new CullFrustum(camera.Desc.Layers, camera.Desc.LayerCount), layer.Position, pixels, layer.Projection.c3.W > 0.5f));
        }
        _shadowPlans.Clear();
        int minTile = ShadowTileAllocator.RoundUp(Math.Clamp(settings.MinTileSize, ShadowTileAllocator.SmallestTile, pageSize / 2));
        for (int i = 0; i < lighting.LightCapacity; i++)
        {
            if (!lighting.IsAlive(i))
                continue;
            ref readonly LightDesc desc = ref lighting.Light(i);
            if (!desc.Shadow.Enabled || desc.Type is not (LightType.Spot or LightType.Point))
                continue;
            (float priority, float fade) = ShadowVisibility(desc, settings);
            if (fade <= 0)
            {
                if (!shadows.Lights.ContainsKey(i))
                    ZeroFaces(shadows, i);
                continue;
            }

            if (!shadows.Lights.TryGetValue(i, out LightShadow light))
                shadows.Lights.Add(i, light = new LightShadow());
            light.Seen = true;
            light.LastSeen = _frame;
            light.Fade = fade;
            light.Priority = priority;
            int faces = desc.Type == LightType.Point ? 6 : 1;
            ShadowInputs key = ShadowInputs.Of(desc);
            if (key != light.Key || faces != light.Faces)
            {
                if (faces != light.Faces)
                    ReleaseShadow(shadows, i, light, keepState: true);
                light.Key = key;
                light.Faces = faces;
                light.Dirty = light.StaticDirty = true;
            }

            if (_shadowPlans.Count == _shadowPlanPool.Count)
                _shadowPlanPool.Add(new ShadowPlan());
            ShadowPlan plan = _shadowPlanPool[_shadowPlans.Count];
            plan.Light = i;
            plan.Faces = faces;
            plan.Priority = priority;
            plan.Wanted = Math.Min(priority, 1e6f) * settings.TexelsPerPixel;
            plan.MaxTile = Math.Max(Math.Min(ShadowTileAllocator.RoundUp((int)Math.Min(desc.Shadow.Resolution, 1u << 20)), pageSize), minTile);
            plan.Held = light.HasTiles ? light.Size : 0;
            plan.Split = SplitCounted(light);
            _shadowPlans.Add(plan);
        }
        List<int>? gone = null;
        foreach ((int index, LightShadow light) in shadows.Lights)
        {
            // A light no view sees keeps its tiles until the page needs the room, so turning back to it draws nothing
            if (light.Seen)
                continue;
            if (!lighting.IsAlive(index))
            {
                (gone ??= new()).Add(index);
                continue;
            }
            ref readonly LightDesc desc = ref lighting.Light(index);
            if (!desc.Shadow.Enabled || desc.Type is not (LightType.Spot or LightType.Point))
                (gone ??= new()).Add(index);
        }
        foreach (int index in gone ?? [])
        {
            ReleaseShadow(shadows, index, shadows.Lights[index], keepState: false);
            shadows.Lights.Remove(index);
        }

        shadows.Planner.Plan(_shadowPlans, (long)((double)pageSize * pageSize * ShadowPlanner.FitHeadroom), minTile);

        // Tiles in order of importance. A light that draws anyway changes size freely, one that would only sharpen or soften waits for the budget.
        int resizes = Math.Max(settings.MaxResizesPerFrame, 0);
        _shadowQueue.Clear();
        _shadowQueue.AddRange(_shadowPlans);
        for (int q = 0; q < _shadowQueue.Count; q++)
        {
            ShadowPlan plan = _shadowQueue[q];
            LightShadow light = shadows.Lights[plan.Light];
            // A static copy is ruled out only when the plan dropped one it had counted
            light.SplitAllowed = plan.Split || !SplitCounted(light);
            if (!plan.Split && light.StaticPages[0] != null)
            {
                ReleaseStatic(light);
                light.Split = false;
                light.Dirty = true;
            }
            if (plan.Target == 0)
            {
                if (light.HasTiles)
                    ReleaseShadow(shadows, plan.Light, light, keepState: true);
                continue;
            }
            if (light.HasTiles && light.Size == plan.Target)
                continue;
            bool mandatory = !light.HasTiles || light.Dirty || light.StaticDirty;
            if (!mandatory)
            {
                // Only sharpening or softening. Sharpening draws again so it is paced by the budget, which a light may
                // finish even when it has fewer faces left than it needs; softening reads the old tiles down and is free.
                // The new size goes where it fits as things are or in the light's own old space, and the old tiles stay
                // just where they were when it does not fit either way.
                bool shrink = plan.Target < light.Size && BlitShadows;
                bool redrawShrink = plan.Target < light.Size && !shrink;
                if (!shrink && !redrawShrink && resizes <= 0)
                    continue;
                if (shrink && !ReserveScratch(shadows, light, plan.Target, pageSize))
                    continue;
                Array.Copy(light.Pages, _oldPages, light.Faces);
                Array.Copy(light.Tiles, _oldTiles, light.Faces);
                bool oldFreed = false;
                bool fits = AllocateFaces(shadows.Pages, shadows, light.Faces, plan.Target, pageSize, _resizePages, _resizeTiles, isStatic: false);
                while (!fits && EvictUnseen(shadows))
                    fits = AllocateFaces(shadows.Pages, shadows, light.Faces, plan.Target, pageSize, _resizePages, _resizeTiles, isStatic: false);
                if (!fits)
                {
                    for (int f = 0; f < light.Faces; f++)
                        light.Pages[f]!.Tiles.Free(light.Tiles[f]);
                    bool placed = AllocateFaces(shadows.Pages, shadows, light.Faces, plan.Target, pageSize, _resizePages, _resizeTiles, isStatic: false);
                    for (int f = 0; f < light.Faces; f++)
                    {
                        if (!placed)
                            light.Pages[f]!.Tiles.TryClaim(light.Tiles[f]);
                        else
                            light.Pages[f] = null;
                    }
                    if (!placed)
                    {
                        if (shrink)
                            ReleaseScratch(shadows, light);
                        continue;
                    }
                    oldFreed = true;
                }
                if (shrink)
                {
                    Shrink(shadows, light, plan.Target, pageSize, oldFreed);
                    ShadowsShrunk++;
                    continue;
                }
                if (redrawShrink)
                {
                    ShadowsShrunk++;
                }
                else
                {
                    resizes -= light.Faces;
                    ShadowsGrown++;
                }
                ReleaseShadow(shadows, plan.Light, light, keepState: true);
                Array.Copy(_resizePages, light.Pages, light.Faces);
                Array.Copy(_resizeTiles, light.Tiles, light.Faces);
                light.Size = plan.Target;
                continue;
            }
            int had = light.HasTiles ? light.Size : 0;
            if (light.HasTiles)
                ReleaseShadow(shadows, plan.Light, light, keepState: true);
            if (!PlaceLight(shadows, light, plan.Target, pageSize))
                continue;
            light.Size = plan.Target;
            if (had == 0)
                ShadowsAdded++;
            else if (plan.Target > had)
                ShadowsGrown++;
            else
                ShadowsShrunk++;
        }

        bool providers = CastingProviders(world);
        _shadowDraws.Clear();
        foreach ((int index, LightShadow light) in shadows.Lights)
        {
            if (!light.HasTiles || !light.Seen)
                continue;
            if (!light.Dirty && light.EveryFrame)
                light.Dirty = true;
            if (providers && !light.ProviderCasts && ProvidersCastInto(world, light))
                light.Dirty = light.MovingChanged = true;
            else if (!light.Dirty && light.ProviderCasts && ProvidersCastInto(world, light))
                light.Dirty = true;
            if (light.StaticEveryFrame)
                light.StaticDirty = true;

            // Moving casters reaching a light that held only static ones decide again whether it splits
            bool retry = light.SplitDenied && StaticRoom(shadows, light);
            if ((light.MovingChanged || retry) && !light.Split && light.SplitAllowed && light.Statics > 0)
                light.StaticDirty = true;
            if (light.Dirty || light.StaticDirty)
                _shadowDraws.Add((index, light, light.Priority * (1 + light.Waited)));
        }

        // Lights with nothing drawn yet go first, then by priority grown with the frames each has waited
        _shadowDraws.Sort((a, b) => a.Light.Drawn != b.Light.Drawn ? a.Light.Drawn.CompareTo(b.Light.Drawn) : b.Key.CompareTo(a.Key));
        long start = Stopwatch.GetTimestamp();
        double budget = Math.Max(settings.DrawMilliseconds, 0);
        for (int d = 0; d < _shadowDraws.Count; d++)
        {
            (int index, LightShadow light, _) = _shadowDraws[d];
            if (d > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budget)
            {
                light.Waited++;
                continue;
            }
            light.Waited = 0;
            light.Drawn = true;
            RedrawShadow(world, shadows, index, lighting.Light(index), light, pageSize);
        }

        foreach ((int index, LightShadow light) in shadows.Lights)
        {
            if (!light.HasTiles || !light.Seen || !light.Drawn)
            {
                light.Stamped = false;
                for (int f = 0; f < WorldShadows.FacesPerLight; f++)
                    WriteFace(shadows, index, f, default, light);
                continue;
            }
            ref readonly LightDesc desc = ref lighting.Light(index);
            for (int f = 0; f < light.Faces; f++)
            {
                light.Pages[f]!.LastUsed = _frame;
                if (light.StaticPages[f] is { } staticPage)
                    staticPage.LastUsed = _frame;
            }
            FaceStamp stamp = new(light.Key, light.Size, pageSize, light.Fade, desc.Shadow.Strength, desc.Shadow.DepthBias, desc.Shadow.NormalBias, desc.Shadow.Filter);
            if (SameStamp(light, stamp))
                continue;
            light.Stamp = stamp;
            light.Stamped = true;
            for (int f = 0; f < light.Faces; f++)
            {
                light.StampTiles[f] = light.Tiles[f];
                light.StampSlots[f] = light.Pages[f]!.Slot;
            }
            for (int f = 0; f < light.Faces; f++)
            {
                (Float4x4 view, Float4x4 projection, float half, float near, float far) = FaceView(desc, f);
                float inverse = 1f / pageSize;
                ShadowTile tile = light.Tiles[f];
                WriteFace(shadows, index, f, new ShadowFace
                {
                    Rect = new Float4(tile.X * inverse, tile.Y * inverse, tile.Size * inverse, 2 * MathF.Tan(half) / tile.Size),
                    Params = new Float4(BitConverter.UInt32BitsToSingle(light.Pages[f]!.Slot), light.Fade * desc.Shadow.Strength, inverse, (float)desc.Shadow.Filter),
                    Depth = new Float4(near, far, desc.Shadow.DepthBias, desc.Shadow.NormalBias),
                }, light);
                if (desc.Type != LightType.Spot)
                    continue;
                Float4x4 toClip = projection * view;
                WriteFace(shadows, index, 1, new ShadowFace { Rect = toClip.c0, Params = toClip.c1, Depth = toClip.c2 }, light);
                WriteFace(shadows, index, 2, new ShadowFace { Rect = toClip.c3 }, light);
            }
        }

        RetirePages(shadows, shadows.Pages, all: false);
        RetirePages(shadows, shadows.StaticPages, all: false);
        world.World.LiveShadowAtlas = shadows.Pages.Count > 0 ? shadows.Pages[0].Texture.DepthTexture : null;
        world.World.StaticShadowAtlas = shadows.StaticPages.Count > 0 ? shadows.StaticPages[0].Texture.DepthTexture : null;
    }

    private void ResolvePage(ShadowPage page) => page.Persistent.Resolve(_device, texture => _textures.ShadowSlot(texture.DepthTexture!), out _);

    // Lights whose shadows a caster's change touched: its old and new place, every user of a changed material
    private void InvalidateShadows(WorldFrame world, WorldShadows shadows, WorldLighting lighting)
    {
        RenderWorld.RenderSide render = world.World.Render;
        if (render.ShadowChanges.Count == 0 && ChangedMaterials.Count == 0)
            return;
        shadows.Grid.Clear();
        int tiled = 0;
        foreach ((int index, LightShadow light) in shadows.Lights)
        {
            if (light.HasTiles && lighting.IsAlive(index))
            {
                shadows.Grid.Add(index, lighting.Light(index).Position, Math.Max(lighting.Light(index).Range, 1e-3f));
                tiled++;
            }
        }
        if (tiled == 0)
        {
            render.ShadowChanges.Clear();
            return;
        }

        void Touch(Double3 center, float radius, bool isStatic)
        {
            foreach (int index in shadows.Grid.Near(center, radius))
            {
                ref readonly LightDesc desc = ref lighting.Light(index);
                Double3 d = desc.Position - center;
                double reach = desc.Range + radius;
                if (d.X * d.X + d.Y * d.Y + d.Z * d.Z > reach * reach)
                    continue;
                LightShadow light = shadows.Lights[index];
                if (isStatic)
                {
                    light.StaticDirty = true;
                }
                else
                {
                    light.Dirty = true;
                    light.MovingChanged = true;
                }
            }
        }

        foreach ((Double3 center, float radius, bool isStatic) in render.ShadowChanges)
            Touch(center, radius, isStatic);
        render.ShadowChanges.Clear();
        foreach (Material material in ChangedMaterials)
        {
            foreach (int index in world.Buckets.UsersOf(material))
            {
                if (index < render.Capacity && render.Alive[index] && (render.Flags[index] & RenderableFlags.CastShadows) != 0)
                    Touch(render.WorldCenter[index], render.WorldRadius[index], (render.Flags[index] & RenderableFlags.Static) != 0);
            }
        }
    }

    // A light's faces into the page. When they do not fit, lights no view sees give theirs back oldest first until it fits.
    private bool PlaceLight(WorldShadows shadows, LightShadow light, int size, int pageSize)
    {
        bool Fits() => AllocateFaces(shadows.Pages, shadows, light.Faces, size, pageSize, light.Pages, light.Tiles, isStatic: false);

        if (Fits())
            return true;
        while (EvictUnseen(shadows))
        {
            if (Fits())
                return true;
        }
        return false;
    }

    // The tiles of the light seen longest ago that no view sees now. Returns whether one gave its tiles back.
    private bool EvictUnseen(WorldShadows shadows)
    {
        int oldest = -1;
        foreach ((int index, LightShadow other) in shadows.Lights)
        {
            if (!other.Seen && other.HasTiles && (oldest < 0 || other.LastSeen < shadows.Lights[oldest].LastSeen))
                oldest = index;
        }
        if (oldest < 0)
            return false;
        ReleaseShadow(shadows, oldest, shadows.Lights[oldest], keepState: true);
        return true;
    }

    private void RedrawShadow(WorldFrame world, WorldShadows shadows, int index, in LightDesc desc, LightShadow light, int pageSize)
    {
        if (light.StaticDirty)
        {
            int statics = 0, moving = 0;
            for (int f = 0; f < light.Faces; f++)
            {
                (int s, int m) = world.Buckets.CountCasters(FaceCull(desc, f), ~0u);
                statics += s;
                moving += m;
            }
            light.Statics = statics;
            light.Movings = moving;
            bool wants = statics > 0 && (moving > 0 || light.ProviderCasts);
            bool split = wants && light.SplitAllowed;
            if (split && light.StaticPages[0] == null && !AllocateFaces(shadows.StaticPages, shadows, light.Faces, light.Size, pageSize, light.StaticPages, light.StaticTiles, isStatic: true))
                split = false;
            light.SplitDenied = wants && !split;
            if (!split)
                ReleaseStatic(light);
            light.Split = split;
        }

        bool everyFrame = false, staticEveryFrame = false, providerCasts = false, complete = true, staticComplete = true;
        light.Shaders.Clear();
        if (light.StaticDirty)
            light.StaticShaders.Clear();
        for (int f = 0; f < light.Faces; f++)
        {
            (Float4x4 view, Float4x4 projection, _, float near, float far) = FaceView(desc, f);
            ViewLayer layer = new() { View = view, Projection = projection, Position = desc.Position };
            CullFrustum cull = FaceCull(desc, f);
            CameraFrame camera = RentShadowCamera();
            if (light.Split)
            {
                if (light.StaticDirty)
                {
                    CameraFrame statics = RentShadowCamera();
                    DrawShadowView(statics, world, light.StaticTiles[f].Size, layer, cull, near, far, CasterFilter.StaticOnly);
                    light.StaticPages[f]!.Draws.Add(new ShadowDraw(statics, light.StaticTiles[f], Clear: true, index, f));
                    staticEveryFrame |= EveryFrameCasters(statics);
                    staticComplete &= ShadowReady(statics);
                    CollectShaders(statics, light.StaticShaders);
                }
                shadows.Copies.Add(new ShadowCopy(light.StaticPages[f]!, light.StaticTiles[f], light.Pages[f]!, light.Tiles[f]));
                DrawShadowView(camera, world, light.Tiles[f].Size, layer, cull, near, far, CasterFilter.MovingOnly);
                light.Pages[f]!.Draws.Add(new ShadowDraw(camera, light.Tiles[f], Clear: false, index, f));
            }
            else
            {
                DrawShadowView(camera, world, light.Tiles[f].Size, layer, cull, near, far, CasterFilter.All);
                light.Pages[f]!.Draws.Add(new ShadowDraw(camera, light.Tiles[f], Clear: true, index, f));
            }
            everyFrame |= EveryFrameCasters(camera);
            providerCasts |= camera.FindList(PassType.ShadowCaster) is { } list && camera.Sink.For(list) != null;
            complete &= ShadowReady(camera);
            CollectShaders(camera, light.Shaders);
            light.Views[f] = camera.Desc;
            light.Culls[f] = cull;
        }
        light.EveryFrame = everyFrame;
        light.ProviderCasts = providerCasts;
        light.MovingChanged = false;
        if (light.StaticDirty)
            light.StaticEveryFrame = light.Split && staticEveryFrame;

        // Casters whose programs are still compiling drew nothing, so the light draws again once they are ready
        light.Dirty = !complete;
        light.StaticDirty = !staticComplete;
    }

    // Whether a caster shader has a global keyword that changed
    private static bool ReadsChangedKeyword(List<Shader> shaders, IReadOnlyDictionary<string, string>? before, IReadOnlyDictionary<string, string> after)
    {
        foreach (Shader shader in shaders)
        {
            foreach (VariantSpace axis in shader.GlobalAxes)
            {
                if (before == null || before.GetValueOrDefault(axis.Name) != after.GetValueOrDefault(axis.Name))
                    return true;
            }
        }
        return false;
    }

    // The shaders a shadow view drew with, added to a light's
    private void CollectShaders(CameraFrame camera, List<Shader> into)
    {
        if (camera.FindList(PassType.ShadowCaster) is not { } list)
            return;
        foreach (DrawCall call in list.Calls)
        {
            Shader shader = DrawPrograms[call.Program].Shader;
            if (!into.Contains(shader))
                into.Add(shader);
        }
    }

    // Whether a light holds or would hold a static copy, by its last count of casters
    private static bool SplitCounted(LightShadow light) => light.Split || (light.Statics > 0 && (light.Movings > 0 || light.ProviderCasts));

    // Whether a static copy of a light's size could be made now: a static page to spare, or one with room
    private bool StaticRoom(WorldShadows shadows, LightShadow light)
    {
        if (shadows.StaticPages.Count == 0)
            return true;

        // Tried in the static pages and given straight back, so the answer is what allocating would really do
        int placed = 0;
        for (; placed < light.Faces; placed++)
        {
            _probePages[placed] = null;
            foreach (ShadowPage page in shadows.StaticPages)
            {
                if (page.Tiles.TryAllocate(light.Size, out _probeTiles[placed]))
                {
                    _probePages[placed] = page;
                    break;
                }
            }
            if (_probePages[placed] == null)
                break;
        }
        for (int f = 0; f < placed; f++)
            _probePages[f]!.Tiles.Free(_probeTiles[f]);
        return placed == light.Faces;
    }

    // Whether every caster of a shadow view drew with its own program, not a stand in or nothing
    private bool ShadowReady(CameraFrame camera)
    {
        if (camera.FindList(PassType.ShadowCaster) is not { } list)
            return true;
        KeywordSet keywords = camera.KeywordSet;
        foreach (DrawCall call in list.Calls)
        {
            bool mirrored = call.Mirrored != camera.Mirrored;
            if (Programs.Failed(call.Program, mirrored, keywords))
                continue;
            if (!Programs.Ready(call.Program, mirrored, keywords))
                return false;
            if (!Materials.Tables.ContainsKey(DrawPrograms[call.Program].Shader) || Programs.Get(call.Program, mirrored, keywords) is not { } program)
                return false;
            IReadOnlyDictionary<PropertyID, DeviceBuffer> buffers = camera.World.World.Globals.BufferSnapshot;
            foreach (PropertyID name in Programs.UserBuffers(program))
            {
                if (!buffers.TryGetValue(name, out DeviceBuffer? buffer) || buffer.IsDisposed)
                    return false;
            }
        }
        return true;
    }

    // A face's view and projection: a spot's cone along its direction, or one of a point light's cube faces
    private static (Float4x4 View, Float4x4 Projection, float HalfAngle, float Near, float Far) FaceView(in LightDesc desc, int face)
    {
        float far = Math.Max(desc.Range, 0.02f);

        // A fiftieth of the range keeps 16 bit steps fine toward its far end; casters nearer the light are kept by the cull and clamp onto the near plane
        float near = Math.Clamp(far * 0.02f, 0.01f, 2f);
        if (desc.Type == LightType.Point)
            return (WorldReflections.FaceView(face), ShadowMath.Perspective(MathF.PI / 4, near, far), MathF.PI / 4, near, far);
        float half = Math.Clamp(desc.OuterAngle, 1f, 85f) * MathF.PI / 180f;
        (Float3 right, Float3 up, Float3 forward) = ShadowMath.Basis(desc.Direction, desc.Up);
        return (ShadowMath.Rows(right, up, forward), ShadowMath.Perspective(half, near, far), half, near, far);
    }

    private static CullFrustum FaceCull(in LightDesc desc, int face)
    {
        (Float4x4 view, Float4x4 projection, _, _, _) = FaceView(desc, face);
        return new CullFrustum(projection * view, desc.Position, nearPlane: false);
    }

    // Every face a tile of one size, all or none. Live and static pages may each number as many pages as the budget holds
    private bool AllocateFaces(List<ShadowPage> pages, WorldShadows shadows, int faces, int size, int pageSize, ShadowPage?[] pageOf, ShadowTile[] tileOf, bool isStatic)
    {
        for (int f = 0; f < faces; f++)
        {
            ShadowPage? found = null;
            ShadowTile tile = default;
            foreach (ShadowPage page in pages)
            {
                if (page.Tiles.TryAllocate(size, out tile))
                {
                    found = page;
                    break;
                }
            }
            if (found == null && pages.Count == 0)
            {
                PersistentTexture texture = new(shadows.NewPageId(isStatic), ShadowRecorder.Desc(pageSize, ShadowFormat), MaterialPropertyType.Texture2D);
                found = new ShadowPage(texture, isStatic, pageSize) { LastUsed = _frame };
                ResolvePage(found);
                pages.Add(found);
                if (!found.Tiles.TryAllocate(size, out tile))
                    found = null;
            }
            if (found == null)
            {
                for (int g = 0; g < f; g++)
                {
                    pageOf[g]!.Tiles.Free(tileOf[g]);
                    pageOf[g] = null;
                }
                return false;
            }
            pageOf[f] = found;
            tileOf[f] = tile;
        }
        return true;
    }

    // Room in the scratch page for a light's faces at their new size, the static copy's as well when it keeps one
    private bool ReserveScratch(WorldShadows shadows, LightShadow light, int size, int pageSize)
    {
        if (shadows.ScratchTiles == null || shadows.ScratchTiles.Size != pageSize / 2)
            shadows.ScratchTiles = new ShadowTileAllocator(pageSize / 2);
        ShadowTileAllocator scratch = shadows.ScratchTiles;
        int faces = light.Faces;
        int reserved = 0;
        for (; reserved < faces * 2; reserved++)
        {
            bool live = reserved < faces;
            if (!live && light.StaticPages[0] == null)
                break;
            ref ShadowTile tile = ref live ? ref shadows.ScratchLive[reserved] : ref shadows.ScratchStatic[reserved - faces];
            if (!scratch.TryAllocate(size, out tile))
            {
                for (int r = 0; r < reserved; r++)
                    scratch.Free(r < faces ? shadows.ScratchLive[r] : shadows.ScratchStatic[r - faces]);
                return false;
            }
        }
        return true;
    }

    private static void ReleaseScratch(WorldShadows shadows, LightShadow light)
    {
        for (int f = 0; f < light.Faces; f++)
        {
            shadows.ScratchTiles!.Free(shadows.ScratchLive[f]);
            if (light.StaticPages[0] != null)
                shadows.ScratchTiles.Free(shadows.ScratchStatic[f]);
        }
    }

    // Moves a light into the smaller tiles placed for it, its old tiles read down through the scratch page rather than drawn
    // again, the static copy too when it keeps one. A static copy with no room left goes, the live tiles holding all its
    // casters already.
    private void Shrink(WorldShadows shadows, LightShadow light, int size, int pageSize, bool oldFreed)
    {
        for (int f = 0; f < light.Faces; f++)
        {
            if (!oldFreed)
                _oldPages[f]!.Tiles.Free(_oldTiles[f]);
            shadows.Shrinks.Add(new ShadowShrink(_oldPages[f]!, _oldTiles[f], shadows.ScratchLive[f], _resizePages[f]!, _resizeTiles[f]));
            light.Pages[f] = _resizePages[f];
            light.Tiles[f] = _resizeTiles[f];
        }
        light.Size = size;
        if (light.StaticPages[0] == null)
            return;

        Array.Copy(light.StaticPages, _oldPages, light.Faces);
        Array.Copy(light.StaticTiles, _oldTiles, light.Faces);
        for (int f = 0; f < light.Faces; f++)
            _oldPages[f]!.Tiles.Free(_oldTiles[f]);
        if (!AllocateFaces(shadows.StaticPages, shadows, light.Faces, size, pageSize, light.StaticPages, light.StaticTiles, isStatic: true))
        {
            Array.Clear(light.StaticPages);
            light.Split = false;
            return;
        }
        for (int f = 0; f < light.Faces; f++)
            shadows.Shrinks.Add(new ShadowShrink(_oldPages[f]!, _oldTiles[f], shadows.ScratchStatic[f], light.StaticPages[f]!, light.StaticTiles[f]));
    }

    // A light's tiles go back to their pages and its faces read as unshadowed; its cameras go too unless it keeps its state
    private void ReleaseShadow(WorldShadows shadows, int index, LightShadow light, bool keepState)
    {
        for (int f = 0; f < 6; f++)
        {
            if (light.Pages[f] is { } page)
                page.Tiles.Free(light.Tiles[f]);
            light.Pages[f] = null;
            WriteFace(shadows, index, f, default, light);
        }
        light.Stamped = false;
        ReleaseStatic(light);
        light.Split = false;
        light.Drawn = false;
        light.Dirty = light.StaticDirty = true;
        if (keepState)
            return;
        light.Shaders.Clear();
        light.StaticShaders.Clear();
        Array.Clear(light.Views);
    }

    private static void ReleaseStatic(LightShadow light)
    {
        for (int f = 0; f < 6; f++)
        {
            if (light.StaticPages[f] is { } page)
                page.Tiles.Free(light.StaticTiles[f]);
            light.StaticPages[f] = null;
        }
    }

    // Pages holding no tile for a few frames go, or every page when the page size changed
    private bool RetirePages(WorldShadows shadows, List<ShadowPage> pages, bool all)
    {
        bool retired = false;
        for (int i = pages.Count - 1; i >= 0; i--)
        {
            ShadowPage page = pages[i];
            if (!all && (page.Tiles.UsedTexels > 0 || _frame - page.LastUsed <= StaleViewFrames))
                continue;
            pages.RemoveAt(i);
            page.Persistent.Release(_store);
            retired = true;
        }
        return retired;
    }

    private static bool SameStamp(LightShadow light, in FaceStamp stamp)
    {
        if (!light.Stamped || light.Stamp != stamp)
            return false;
        for (int f = 0; f < light.Faces; f++)
        {
            if (light.StampTiles[f] != light.Tiles[f] || light.StampSlots[f] != light.Pages[f]!.Slot)
                return false;
        }
        return true;
    }

    private static void WriteFace(WorldShadows shadows, int index, int face, in ShadowFace record, LightShadow light)
    {
        uint at = (uint)(index * WorldShadows.FacesPerLight + face);
        if (ShadowFace.Same(light.Written[face], record) && at < shadows.Faces.Count)
            return;
        light.Written[face] = record;
        shadows.Faces.Set(at, record);
        if (index < shadows.Zeroed.Length)
            shadows.Zeroed[index] = false;
    }

    // A shadowed light casting nothing still has faces for shaders to read, all unshadowed
    private static void ZeroFaces(WorldShadows shadows, int index)
    {
        if (index >= shadows.Zeroed.Length)
            Array.Resize(ref shadows.Zeroed, Math.Max(index + 1, shadows.Zeroed.Length * 2));
        if (shadows.Zeroed[index])
            return;
        shadows.Zeroed[index] = true;
        for (int f = 0; f < WorldShadows.FacesPerLight; f++)
            shadows.Faces.Set((uint)(index * WorldShadows.FacesPerLight + f), default(ShadowFace));
    }

    // Whether a shadow view drew casters that change every frame without the world knowing
    private bool EveryFrameCasters(CameraFrame camera)
    {
        if (camera.FindList(PassType.ShadowCaster) is not { } list)
            return false;
        if (camera.Sink.For(list) != null)
            return true;
        foreach (DrawCall call in list.Calls)
        {
            DrawProgram program = DrawPrograms[call.Program];
            if (ShaderDefinition.PassHasTag(program.Shader.Definition.Passes![program.PassIndex], "ShadowUpdate", "EveryFrame"))
                return true;
        }
        return false;
    }

    private static bool CastingProviders(WorldFrame world)
    {
        foreach (RenderProvider provider in world.World.ProviderSnapshot)
        {
            if (provider.EnabledSnapshot && (provider.RunsOnSnapshot & ViewKinds.Shadow) != 0)
                return true;
        }
        return false;
    }

    // Whether providers put anything into a cached light's views, which then draw again
    private bool ProvidersCastInto(WorldFrame world, LightShadow light)
    {
        _providerProbe ??= ShadowCamera();
        DrawList list = _providerProbe.ListFor(PassType.ShadowCaster);
        for (int f = 0; f < light.Faces; f++)
        {
            if (light.Views[f].World == null)
                continue;
            CullProviders(_providerProbe, light.Views[f], world, light.Culls[f]);
            if (_providerProbe.Sink.For(list) != null)
                return true;
        }
        return false;
    }

    // A light's radius on screen in pixels in the view that sees it biggest, and its fade over the last tenth of the local distance and as it shrinks toward the smallest radius that casts
    private (float Priority, float Fade) ShadowVisibility(in LightDesc light, ShadowSettings settings)
    {
        float priority = 0, fade = 0;
        float range = Math.Max(light.Range, 0.02f);
        float distanceLimit = Math.Max(settings.LocalDistance, 0.01f);
        float sizeLimit = Math.Max(settings.MinScreenRadius, 0);
        float reach = distanceLimit + range;
        foreach ((CullFrustum frustum, Double3 position, float pixels, bool orthographic) in _shadowViewers)
        {
            Double3 d = light.Position - position;
            float distanceSquared = (float)(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (distanceSquared > reach * reach || !frustum.Sphere(light.Position, range))
                continue;
            float edge = MathF.Sqrt(distanceSquared) - range;
            float t = Math.Clamp((Math.Max(edge, 0) - distanceLimit * 0.9f) / (distanceLimit * 0.1f), 0, 1);
            float radius = ScreenRadius(pixels, orthographic, distanceSquared, range);
            float s = sizeLimit > 0 ? Math.Clamp((radius - sizeLimit) / sizeLimit, 0, 1) : 1;
            fade = Math.Max(fade, (1 - t * t * (3 - 2 * t)) * s * s * (3 - 2 * s));
            priority = Math.Max(priority, radius);
        }
        return (priority, fade);
    }

    // A sphere's radius on screen in pixels, unbounded once the camera is about inside it
    private static float ScreenRadius(float pixels, bool orthographic, float distanceSquared, float radius)
    {
        if (orthographic)
            return radius * pixels;
        float inner = distanceSquared - radius * radius;
        if (inner <= radius * radius * 0.1f)
            return float.MaxValue;
        return radius / MathF.Sqrt(inner) * pixels;
    }

    // A camera for one face this frame, back in the pool once the frame dispatched
    private CameraFrame RentShadowCamera()
    {
        CameraFrame camera = _shadowCameraPool.Count > 0 ? _shadowCameraPool.Pop() : ShadowCamera();
        _shadowCamerasDrawn.Add(camera);
        return camera;
    }

    private void ReturnShadowCameras()
    {
        foreach (CameraFrame camera in _shadowCamerasDrawn)
            _shadowCameraPool.Push(camera);
        _shadowCamerasDrawn.Clear();
    }

    private CameraFrame ShadowCamera()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId()) { IsShadowView = true };
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    // A shadow view's casters culled against the given frustum for a tile of the given size, its draw lists ready to record
    private void DrawShadowView(CameraFrame camera, WorldFrame world, int tile, in ViewLayer layer, in CullFrustum cull, float near, float far, CasterFilter filter, Keyword[]? keywords = null)
    {
        ViewDesc view = new()
        {
            World = world.World,
            Kind = ViewKind.Shadow,
            ViewId = camera.View.ViewId,
            Keywords = keywords,
            Sky = false,
            Near = near,
            FarDistance = far,
        };
        view.Layers[0] = layer;
        camera.ShadowCull = cull;
        camera.ShadowFilter = filter;
        ShadowViewsDrawn++;
        PrepareShadowView(camera, view, world, (uint)tile);
    }

    private void PrepareShadowView(CameraFrame camera, in ViewDesc view, WorldFrame world, uint size)
    {
        camera.LastFrame = _frame;
        camera.Desc = view;
        camera.World = world;
        camera.ResolveKeywords();
        camera.Info = new FeatureView(view, size, size);
        camera.ResetPassLists();
        camera.ListFor(PassType.ShadowCaster);
        Prepare(camera, view, world, default, size, size);
        camera.View.PixelWidth = size;
        camera.View.PixelHeight = size;
    }

    private static int CascadeCount(in LightDesc light) => Math.Clamp((int)light.Shadow.Cascades, 1, ViewShadows.MaxCascades);

    private uint CascadeResolution(in LightDesc light) => Math.Clamp(BitOperations.RoundUpToPowerOf2(Math.Max(light.Shadow.Resolution, 1u)), 256u, LargestShadowMap(8192u));

    private static CascadeFit FitOf(in ViewDesc view) => Casts(view) ? CascadeFit.View : view.Kind == ViewKind.ReflectionProbe ? CascadeFit.Around : CascadeFit.None;

    private (CascadeSlot[] Slots, string Key) CascadesOf(CameraFrame camera, in ViewDesc view, WorldFrame world)
    {
        _cascadeSlots.Clear();
        if (FitOf(view) != CascadeFit.None)
        {
            WorldLighting lighting = world.World.Lighting;
            IReadOnlyList<int> order = lighting.DirectionalOrder;
            for (int d = 0; d < order.Count; d++)
            {
                ref readonly LightDesc light = ref lighting.Light(order[d]);
                if (light.Shadow.Enabled)
                    _cascadeSlots.Add(new CascadeSlot(d, CascadeCount(light), CascadeResolution(light)));
            }
        }
        if (!CollectionsMarshal.AsSpan(_cascadeSlots).SequenceEqual(camera.CascadeSlots))
        {
            camera.CascadeSlots = [.. _cascadeSlots];
            camera.CascadeKey = string.Join(',', camera.CascadeSlots.Select(slot => $"{slot.Slot}:{slot.Count}x{slot.Resolution}"));
        }
        return (camera.CascadeSlots, camera.CascadeKey);
    }

    // Each casting directional light's cascades for the view, refit on their stagger, and the records its shaders read
    private void PrepareCascades(CameraFrame camera, CascadeFit fit)
    {
        ViewShadows state = camera.Shadows;
        WorldLighting lighting = camera.World.World.Lighting;
        IReadOnlyList<int> order = lighting.DirectionalOrder;
        state.Records.Clear();
        for (int d = 0; d < order.Count; d++)
        {
            ref readonly LightDesc light = ref lighting.Light(order[d]);
            if (fit != CascadeFit.None && light.Shadow.Enabled)
            {
                PrepareLightCascades(camera, state, light, order[d], d, fit == CascadeFit.Around);
                continue;
            }
            for (int c = 0; c < ViewShadows.MaxCascades; c++)
                state.Records.Add(default);
        }
        for (int i = state.Lights.Count - 1; i >= 0; i--)
        {
            int slot = state.Lights[i].Slot;
            if (fit == CascadeFit.None || slot >= order.Count || order[slot] != state.Lights[i].Light || !lighting.Light(order[slot]).Shadow.Enabled)
                ReleaseCascades(state, i);
        }
        if (state.Records.Count == 0)
            state.Records.Add(default);
        camera.Cascades.Write<CascadeRecord>(_uploader, CollectionsMarshal.AsSpan(state.Records));
    }

    private void PrepareLightCascades(CameraFrame camera, ViewShadows state, in LightDesc light, int index, int slot, bool around)
    {
        int count = CascadeCount(light);
        uint resolution = CascadeResolution(light);
        int at = state.Lights.FindIndex(l => l.Slot == slot);
        if (at >= 0 && (state.Lights[at].Light != index || state.Lights[at].Resolution != resolution || state.Lights[at].Count != count))
        {
            ReleaseCascades(state, at);
            at = -1;
        }
        if (at < 0)
        {
            ViewShadows.CascadeLight made = new(index, slot, resolution, count);
            for (int c = 0; c < made.Cascades.Length; c++)
                made.Cascades[c] = new ViewShadows.Cascade();
            state.Lights.Add(made);
            at = state.Lights.Count - 1;
        }
        ViewShadows.CascadeLight cascadeLight = state.Lights[at];
        ViewShadows.Cascade[] cascades = cascadeLight.Cascades;

        ViewDesc view = camera.Desc;
        ViewLayer first = view.Layers[0];
        Float4x4 p = first.Projection;
        bool orthographic = p.c3.W > 0.5f;
        float offsetX = orthographic ? p.c3.X : p.c2.X, offsetY = orthographic ? p.c3.Y : p.c2.Y;
        float extentX = (1f + MathF.Abs(offsetX)) / MathF.Abs(p.c0.X), extentY = (1f + MathF.Abs(offsetY)) / MathF.Abs(p.c1.Y);
        Float3 viewForward = new(first.View.c0.Z, first.View.c1.Z, first.View.c2.Z);
        float padding = 0;
        for (int l = 1; l < view.LayerCount; l++)
        {
            Double3 e = view.Layers[l].Position - first.Position;
            padding = Math.Max(padding, (float)Math.Sqrt(e.X * e.X + e.Y * e.Y + e.Z * e.Z));
        }

        float distance = Math.Max(light.Shadow.Distance, 0.01f);
        float viewNear = Math.Max(view.Near, 0);
        float splitNear = Math.Min(Math.Max(viewNear, ShadowMath.MinSplitNear), distance * 0.5f);
        (Float3 right, Float3 up, Float3 forward) = ShadowMath.Basis(light.Direction, light.Up);
        Float4x4 rotation = ShadowMath.Rows(right, up, forward);
        float blend = Math.Clamp(light.Shadow.CascadeBlend, 0, 0.5f);

        for (int c = 0; c < ViewShadows.MaxCascades; c++)
        {
            ViewShadows.Cascade cascade = cascades[c];
            cascade.Draws.Clear();
            if (c >= count)
            {
                cascade.Valid = false;
                state.Records.Add(default);
                continue;
            }
            if (cascade.World != camera.World.World)
                cascade.Valid = false;
            if (cascade.Texture == null)
            {
                cascade.Texture = new PersistentTexture(RenderResourceID.Intern($"Graphite.Camera{camera.View.ViewId}/Cascade{slot}.{c}"), ShadowRecorder.Desc((int)resolution, ShadowFormat), MaterialPropertyType.Texture2D);
                cascade.Camera = ShadowCamera();
                cascade.Valid = false;
            }
            cascade.Texture.Resolve(_device, texture => _textures.ShadowSlot(texture.DepthTexture!), out bool replaced);
            if (replaced)
                cascade.Valid = false;

            // Cascade c refits every c + 1 frames, staggered, keeping its last fit in between
            if (around || !cascade.Valid || (_frame + c) % (c + 1) == 0)
            {
                float sliceNear = c == 0 ? viewNear : ShadowMath.Split(c, count, splitNear, distance);
                float sliceFar = ShadowMath.Split(c + 1, count, splitNear, distance);
                float depth = ShadowMath.SliceSphere(sliceNear, sliceFar, extentX, extentY, orthographic, out float radius);
                radius += padding;
                if (around)
                    (depth, radius) = (0, sliceFar + padding);
                Double3 center = first.Position + new Double3(viewForward.X * depth, viewForward.Y * depth, viewForward.Z * depth);
                cascade.Center = ShadowMath.Snap(center, right, up, forward, 2.0 * radius / resolution);
                cascade.Radius = radius;
                cascade.World = camera.World.World;
                cascade.Valid = true;

                // Without depth clamp the box reaches toward the light instead, so casters there still land
                float reach = _device.Features.DepthClipDisable ? radius : radius + 2 * distance;
                Float4x4 projection = ShadowMath.Orthographic(radius, reach);
                cascade.Shape = projection * rotation;
                cascade.DepthRange = radius + reach;
                ShadowTile whole = new(0, 0, (int)resolution);
                ViewLayer layer = new() { View = rotation, Projection = projection, Position = cascade.Center };
                DrawShadowView(cascade.Camera!, camera.World, (int)resolution, layer, new CullFrustum(cascade.Shape, cascade.Center, nearPlane: false), -reach, radius, CasterFilter.All, camera.Desc.Keywords);
                cascade.Draws.Add(new ShadowDraw(cascade.Camera!, whole, Clear: true, cascadeLight.Light, c, Cascade: true));
            }

            Double3 r = cascade.Center - first.Position;
            Float3 relative = new((float)r.X, (float)r.Y, (float)r.Z);
            state.Records.Add(new CascadeRecord
            {
                ViewToClip = cascade.Shape * Float4x4.CreateTranslation(-relative),
                Sphere = new Float4(relative.X, relative.Y, relative.Z, cascade.Radius),
                Params = new Float4(BitConverter.UInt32BitsToSingle(cascade.Texture.Slot), 1f / resolution, count, blend),
                Fade = around ? new Float4(0, 0, 0, distance) : new Float4(viewForward.X, viewForward.Y, viewForward.Z, distance),
                Depth = new Float4(cascade.DepthRange, 0, 0, 0),
            });
        }
    }

    private void ReleaseCascades(ViewShadows state, int at)
    {
        foreach (ViewShadows.Cascade cascade in state.Lights[at].Cascades)
        {
            cascade.Texture?.Release(_store);
            if (cascade.Camera != null)
            {
                _textures.Untrack(cascade.Camera.ViewTable);
                _shadowRetired.Add(cascade.Camera);
            }
        }
        state.Lights.RemoveAt(at);
    }

    // Every camera the shadows drew this frame
    private IEnumerable<CameraFrame> ShadowCameras()
    {
        foreach (WorldFrame world in _frameWorlds)
        {
            if (world.Shadows == null)
                continue;
        }
        foreach (CameraFrame camera in _shadowCamerasDrawn)
            yield return camera;
        foreach (CameraFrame camera in AllViews())
        {
            foreach (ViewShadows.CascadeLight light in camera.Shadows.Lights)
            {
                foreach (ViewShadows.Cascade cascade in light.Cascades)
                {
                    if (cascade.Camera is { } shadow && shadow.LastFrame == _frame)
                        yield return shadow;
                }
            }
        }
    }

    // The hosts' views and the worlds' captures
    private IEnumerable<CameraFrame> AllViews()
    {
        foreach (CameraFrame camera in _cameras.Values)
            yield return camera;
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
                yield return camera;
        }
    }
}
