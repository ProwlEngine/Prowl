using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public enum ShadowFilter : byte
{
    /// <summary>One hardware comparison, a hard edge.</summary>
    Hard,

    /// <summary>A 5x5 tent from 9 hardware comparisons.</summary>
    Tent,
}

/// <summary>How a world's local light shadows are stored and sized. Host thread, read at SubmitFrame.</summary>
public sealed class ShadowSettings
{
    /// <summary>The size of each square page local light tiles live in.</summary>
    public int PageSize = 4096;

    /// <summary>Bytes of pages the world may hold.</summary>
    public long Budget = 512L << 20;

    /// <summary>Shadow map texels per screen pixel a light covers, before rounding to a power of two.</summary>
    public float TexelsPerPixel = 1;

    public int MinTileSize = 64;

    /// <summary>Local lights farther than this from every view cast no shadow, fading out over the last tenth.</summary>
    public float LocalDistance = 100;

    internal ShadowSettings Copy() => (ShadowSettings)MemberwiseClone();
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
        while (s < size)
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

/// <summary>One face of a local light's shadow as shaders read it, 96 bytes, matching Graphite_ShadowFace. Six per light.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShadowFace
{
    /// <summary>From a position relative to the light to the face's clip space, reversed Z.</summary>
    public Float4x4 LightToClip;

    /// <summary>The tile's origin and size in page uv, then the world size of a texel one unit from the light.</summary>
    public Float4 Rect;

    /// <summary>The page's 2D texture slot as uint bits, the light's fade, one over the page size, unused.</summary>
    public Float4 Params;
}

/// <summary>One cascade of a directional light in a view as shaders read it, 112 bytes, matching Graphite_Cascade. Four per light.</summary>
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
}

/// <summary>A depth page of a world's local light tiles, in the bindless table.</summary>
internal sealed class ShadowPage(RenderTexture texture, uint slot) : IDisposable
{
    public RenderTexture Texture { get; } = texture;
    public ShadowTarget Target { get; } = new(texture, "Graphite.ShadowPage");
    public uint Slot { get; } = slot;
    public ShadowTileAllocator Tiles { get; } = new((int)texture.Desc.Width);
    public int LastUsed;

    public void Dispose() => Texture.Dispose();
}

/// <summary>A shadow view drawn this frame: its camera, where in its target and the list it draws.</summary>
internal readonly record struct ShadowDraw(CameraFrame Camera, ShadowTile Tile);

/// <summary>A depth target the frame's shadow pipeline draws into: a world's page or one cascade of a view.</summary>
internal sealed class ShadowTarget(RenderTexture texture, string name) : IPass
{
    private static int s_next;
    private readonly RenderResourceID _id = RenderResourceID.Intern($"Graphite.Shadow.{System.Threading.Interlocked.Increment(ref s_next)}");

    public RenderTexture Texture { get; } = texture;
    public readonly List<ShadowDraw> Draws = new();
    public string Name { get; } = name;

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedTexture(_id, Texture);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        if (Draws.Count == 0)
            return;
        cmd.SetFramebuffer(Texture.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Clear(0f)));
        foreach ((CameraFrame camera, ShadowTile tile) in Draws)
        {
            cmd.SetViewport(new Viewport(tile.X, tile.Y, tile.Size, tile.Size, 0, 1));
            cmd.SetScissor((uint)tile.X, (uint)tile.Y, (uint)tile.Size, (uint)tile.Size);
            camera.Drawer.Prepare(context.Task.RingSlot);
            if (camera.FindList(PassType.ShadowCaster) is { } list)
                camera.Drawer.DrawList(cmd, list);
        }
    }
}

/// <summary>A world's local light shadows on the render thread: its pages, every light's faces, the views drawing them.</summary>
internal sealed class WorldShadows(GraphicsDevice device) : IDisposable
{
    public const int FacesPerLight = 6;

    public GpuTable Faces { get; } = GpuTable.Of<ShadowFace>(device, "Shadow faces", 64 * FacesPerLight);
    public readonly List<ShadowPage> Pages = new();
    public readonly List<CameraFrame> Cameras = new();
    public int CamerasUsed;

    /// <summary>The light indices whose faces hold a tile this frame, so next frame clears the ones that lost theirs.</summary>
    public readonly List<int> Shadowed = new();

    public void Dispose()
    {
        Faces.Dispose();
        foreach (ShadowPage page in Pages)
            page.Dispose();
        foreach (CameraFrame camera in Cameras)
            camera.Dispose();
    }
}

/// <summary>A view's directional cascades: a depth texture each, refit on their stagger, and the records shaders read.</summary>
internal sealed class ViewShadows : IDisposable
{
    public const int MaxCascades = 4;

    public sealed class Cascade
    {
        public RenderTexture? Texture;
        public uint Slot;
        public ShadowTarget? Target;
        public CameraFrame? Camera;

        /// <summary>The world position of the centre it was last drawn around, and its radius, so a lagging cascade keeps its fit.</summary>
        public Double3 Center;
        public float Radius;
        public bool Valid;
    }

    /// <summary>Per directional light in shader order, its cascades and the settings they were made for.</summary>
    public readonly List<(int Light, uint Resolution, Cascade[] Cascades)> Lights = new();
    public readonly List<CascadeRecord> Records = new();

    public void Dispose()
    {
        foreach ((_, _, Cascade[] cascades) in Lights)
        {
            foreach (Cascade cascade in cascades)
            {
                cascade.Texture?.Dispose();
                cascade.Camera?.Dispose();
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

    /// <summary>A reversed Z box of half size radius around the origin down +Z: depth 1 at -radius, 0 at +radius.</summary>
    public static Float4x4 Orthographic(float radius)
        => new(new Float4(1f / radius, 0, 0, 0), new Float4(0, 1f / radius, 0, 0), new Float4(0, 0, -0.5f / radius, 0), new Float4(0, 0, 0.5f, 1));

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
    private readonly RenderPipeline _shadowPipeline = new() { Name = "Graphite.Shadows" };
    private readonly List<ShadowTarget> _shadowTargets = new();
    private ShadowTarget[] _builtShadowTargets = [];
    private readonly List<(int Light, int Size, float Fade)> _shadowRequests = new();
    private readonly List<IDisposable> _shadowRetired = new();
    private Sampler? _shadowSampler;

    /// <summary>The comparison sampler every shadow read goes through: linear, clamped, lit where the receiver is nearer.</summary>
    public Sampler ShadowSampler => _shadowSampler ??= _device.ResourceFactory.CreateSampler(SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
        MipFilter = FilterMode.Point,
        ComparisonKind = ComparisonKind.GreaterEqual,
    });

    // Plans and prepares the frame's shadow views, after every host view is prepared. Returns whether any draw.
    private bool PrepareShadows()
    {
        _shadowTargets.Clear();
        foreach (WorldFrame world in _frameWorlds)
            PrepareLocalShadows(world);
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame == _frame)
                PrepareCascades(camera, Casts(camera.Desc));
        }
        foreach (WorldFrame world in _frameWorlds)
        {
            foreach (CameraFrame camera in world.Captures())
            {
                if (camera.LastFrame == _frame)
                    PrepareCascades(camera, false);
            }
        }

        if (!CollectionsMarshal.AsSpan(_shadowTargets).SequenceEqual(_builtShadowTargets))
        {
            _builtShadowTargets = [.. _shadowTargets];
            if (_builtShadowTargets.Length > 0)
                _shadowPipeline.SetPasses(_builtShadowTargets);
        }
        return _builtShadowTargets.Length > 0;
    }

    // Views people look at receive directional shadows, captures do not
    private static bool Casts(in ViewDesc view) => ((ViewKinds)(1 << (int)view.Kind) & ViewKinds.Cameras) != 0;

    private void PrepareLocalShadows(WorldFrame world)
    {
        WorldShadows shadows = world.Shadows ??= new WorldShadows(_device);
        ShadowSettings settings = world.World.ShadowSnapshot;
        WorldLighting lighting = world.World.Lighting;
        shadows.CamerasUsed = 0;
        foreach (int light in shadows.Shadowed)
            shadows.Faces.Set((uint)(light * WorldShadows.FacesPerLight), default(ShadowFace));
        shadows.Shadowed.Clear();

        int pageSize = (int)Math.Clamp(BitOperations.RoundUpToPowerOf2((uint)Math.Max(settings.PageSize, 256)), 256u, 16384u);
        for (int i = shadows.Pages.Count - 1; i >= 0; i--)
        {
            ShadowPage page = shadows.Pages[i];
            page.Target.Draws.Clear();
            if (page.Tiles.Size != pageSize || _frame - page.LastUsed > StaleViewFrames)
            {
                _shadowRetired.Add(page);
                shadows.Pages.RemoveAt(i);
                continue;
            }
            page.Tiles.Reset(pageSize);
        }

        _shadowRequests.Clear();
        for (int i = 0; i < lighting.LightCapacity; i++)
        {
            if (!lighting.IsAlive(i))
                continue;
            ref readonly LightDesc light = ref lighting.Light(i);
            if (light.Type != LightType.Spot || !light.Shadow.Enabled)
                continue;
            (int size, float fade) = WantedTile(world, light, settings, pageSize);
            if (size > 0)
                _shadowRequests.Add((i, size, fade));
            else
                shadows.Faces.Set((uint)(i * WorldShadows.FacesPerLight), default(ShadowFace));
        }

        // Largest first, each halving until it fits a page or the budget's pages are full
        _shadowRequests.Sort((a, b) => b.Size != a.Size ? b.Size.CompareTo(a.Size) : a.Light.CompareTo(b.Light));
        long pageBytes = (long)pageSize * pageSize * 4;
        int maxPages = (int)Math.Max(1, settings.Budget / pageBytes);
        int minTile = Math.Max(ShadowTileAllocator.SmallestTile, settings.MinTileSize);
        foreach ((int index, int wanted, float fade) in _shadowRequests)
        {
            ShadowPage? page = null;
            ShadowTile tile = default;
            for (int size = wanted; page == null && size >= Math.Min(minTile, wanted); size >>= 1)
                page = AllocateTile(shadows, pageSize, maxPages, size, out tile);
            if (page == null)
            {
                shadows.Faces.Set((uint)(index * WorldShadows.FacesPerLight), default(ShadowFace));
                continue;
            }
            page.LastUsed = _frame;
            DrawSpotShadow(world, shadows, lighting.Light(index), index, page, tile, fade, pageSize);
        }
        foreach (ShadowPage page in shadows.Pages)
            _shadowTargets.Add(page.Target);
    }

    // The tile a light wants: twice its radius on screen in texels in the view that sees it biggest, and its fade by distance
    private (int Size, float Fade) WantedTile(WorldFrame world, in LightDesc light, ShadowSettings settings, int pageSize)
    {
        float wanted = 0, fade = 0;
        float range = Math.Max(light.Range, 1e-3f);
        float distanceLimit = Math.Max(settings.LocalDistance, 1e-3f);
        foreach (CameraFrame camera in _cameras.Values)
        {
            if (camera.LastFrame != _frame || camera.World != world || !Casts(camera.Desc))
                continue;
            ViewDesc view = camera.Desc;
            if (!new CullFrustum(view.Layers, view.LayerCount).Sphere(light.Position, range))
                continue;
            ViewLayer layer = view.Layers[0];
            Double3 d = light.Position - layer.Position;
            float distance = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            float beyond = Math.Max(distance - range, 0);
            if (beyond > distanceLimit)
                continue;
            fade = Math.Max(fade, Math.Clamp((distanceLimit - beyond) / (0.1f * distanceLimit), 0, 1));
            Float4x4 p = layer.Projection;
            float scale = MathF.Abs(p.c1.Y) * camera.View.PixelHeight * 0.5f;
            float pixels = p.c3.W > 0.5f ? range * scale : scale * range / Math.Max(distance, range);
            wanted = Math.Max(wanted, 2 * pixels * settings.TexelsPerPixel);
        }
        if (fade <= 0)
            return (0, 0);
        int largest = (int)Math.Min(BitOperations.RoundUpToPowerOf2(Math.Max(light.Shadow.Resolution, 16u)), (uint)pageSize);
        int smallest = Math.Min(Math.Max(settings.MinTileSize, ShadowTileAllocator.SmallestTile), largest);
        return (Math.Clamp(ShadowTileAllocator.RoundUp((int)MathF.Ceiling(wanted)), smallest, largest), fade);
    }

    private ShadowPage? AllocateTile(WorldShadows shadows, int pageSize, int maxPages, int size, out ShadowTile tile)
    {
        foreach (ShadowPage page in shadows.Pages)
        {
            if (page.Tiles.TryAllocate(size, out tile))
                return page;
        }
        tile = default;
        if (shadows.Pages.Count >= maxPages)
            return null;
        RenderTexture texture = _device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription((uint)pageSize, (uint)pageSize, [], PixelFormat.D32_Float));
        texture.Name = "Graphite shadow page";
        ShadowPage created = new(texture, ((IMaterialResources)_textures).TextureSlot(texture.DepthTexture, MaterialPropertyType.Texture2D, "black"));
        shadows.Pages.Add(created);
        return created.Tiles.TryAllocate(size, out tile) ? created : null;
    }

    private void DrawSpotShadow(WorldFrame world, WorldShadows shadows, in LightDesc light, int index, ShadowPage page, ShadowTile tile, float fade, int pageSize)
    {
        float half = Math.Clamp(light.OuterAngle, 1f, 85f) * MathF.PI / 180f;
        float far = Math.Max(light.Range, 0.02f);
        float near = Math.Clamp(far * 0.005f, 0.01f, 0.5f);
        (Float3 right, Float3 up, Float3 forward) = ShadowMath.Basis(light.Direction, light.Up);
        Float4x4 view = ShadowMath.Rows(right, up, forward);
        Float4x4 projection = ShadowMath.Perspective(half, near, far);

        float inverse = 1f / pageSize;
        shadows.Faces.Set((uint)(index * WorldShadows.FacesPerLight), new ShadowFace
        {
            LightToClip = projection * view,
            Rect = new Float4(tile.X * inverse, tile.Y * inverse, tile.Size * inverse, 2 * MathF.Tan(half) / tile.Size),
            Params = new Float4(BitConverter.UInt32BitsToSingle(page.Slot), fade, inverse, 0),
        });
        shadows.Shadowed.Add(index);

        if (shadows.CamerasUsed == shadows.Cameras.Count)
            shadows.Cameras.Add(ShadowCamera());
        CameraFrame camera = shadows.Cameras[shadows.CamerasUsed++];
        ViewLayer layer = new() { View = view, Projection = projection, Position = light.Position };
        DrawShadowView(camera, world, page.Texture, tile, layer, new CullFrustum(projection * view, light.Position, nearPlane: true), near, far);
        page.Target.Draws.Add(new ShadowDraw(camera, tile));
    }

    private CameraFrame ShadowCamera()
    {
        CameraFrame camera = new(_device, this, WorldFrame.NextCaptureViewId());
        _textures.TrackView(camera.ViewTable);
        return camera;
    }

    // A shadow view's casters into a tile of a depth target, culled against the given frustum
    private void DrawShadowView(CameraFrame camera, WorldFrame world, RenderTexture target, ShadowTile tile, in ViewLayer layer, in CullFrustum cull, float near, float far)
    {
        float size = target.Desc.Width;
        ViewDesc view = new()
        {
            World = world.World,
            Kind = ViewKind.Shadow,
            ViewId = camera.View.ViewId,
            External = true,
            Passes = [PassType.ShadowCaster],
            Target = target.Framebuffer,
            Viewport = new Float4(tile.X / size, tile.Y / size, tile.Size / size, tile.Size / size),
            Sky = false,
            Near = near,
            FarDistance = far,
        };
        view.Layers[0] = layer;
        camera.ShadowCull = cull;
        Draw(camera, view, [], world, default);
    }

    // Each casting directional light's cascades for the view, refit on their stagger, and the records its shaders read
    private void PrepareCascades(CameraFrame camera, bool casts)
    {
        ViewShadows state = camera.Shadows;
        WorldLighting lighting = camera.World.World.Lighting;
        IReadOnlyList<int> order = lighting.DirectionalOrder;
        state.Records.Clear();
        for (int d = 0; d < order.Count; d++)
        {
            ref readonly LightDesc light = ref lighting.Light(order[d]);
            if (casts && light.Shadow.Enabled)
            {
                PrepareLightCascades(camera, state, light, order[d]);
                continue;
            }
            for (int c = 0; c < ViewShadows.MaxCascades; c++)
                state.Records.Add(default);
        }
        for (int i = state.Lights.Count - 1; i >= 0; i--)
        {
            int light = state.Lights[i].Light;
            if (!casts || !System.Linq.Enumerable.Contains(order, light) || !lighting.Light(light).Shadow.Enabled)
                ReleaseCascades(state, i);
        }
        if (state.Records.Count == 0)
            state.Records.Add(default);
        camera.Cascades.Write<CascadeRecord>(_uploader, CollectionsMarshal.AsSpan(state.Records));
    }

    private void PrepareLightCascades(CameraFrame camera, ViewShadows state, in LightDesc light, int index)
    {
        int count = Math.Clamp((int)light.Shadow.Cascades, 1, ViewShadows.MaxCascades);
        uint resolution = Math.Clamp(BitOperations.RoundUpToPowerOf2(Math.Max(light.Shadow.Resolution, 1u)), 256u, 8192u);
        int at = state.Lights.FindIndex(l => l.Light == index);
        if (at >= 0 && state.Lights[at].Resolution != resolution)
        {
            ReleaseCascades(state, at);
            at = -1;
        }
        if (at < 0)
        {
            ViewShadows.Cascade[] made = new ViewShadows.Cascade[ViewShadows.MaxCascades];
            for (int c = 0; c < made.Length; c++)
                made[c] = new ViewShadows.Cascade();
            state.Lights.Add((index, resolution, made));
            at = state.Lights.Count - 1;
        }
        ViewShadows.Cascade[] cascades = state.Lights[at].Cascades;

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
            if (c >= count)
            {
                state.Records.Add(default);
                continue;
            }
            ViewShadows.Cascade cascade = cascades[c];
            if (cascade.Texture == null)
            {
                cascade.Texture = _device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(resolution, resolution, [], PixelFormat.D32_Float));
                cascade.Texture.Name = $"Graphite cascade {c}";
                cascade.Slot = ((IMaterialResources)_textures).TextureSlot(cascade.Texture.DepthTexture, MaterialPropertyType.Texture2D, "black");
                cascade.Target = new ShadowTarget(cascade.Texture, "Graphite.Cascade");
                cascade.Camera = ShadowCamera();
                cascade.Valid = false;
            }
            cascade.Target!.Draws.Clear();
            _shadowTargets.Add(cascade.Target);

            // Cascade c refits every c + 1 frames, staggered, keeping its last fit in between
            if (!cascade.Valid || (_frame + c) % (c + 1) == 0)
            {
                float sliceNear = c == 0 ? viewNear : ShadowMath.Split(c, count, splitNear, distance);
                float sliceFar = ShadowMath.Split(c + 1, count, splitNear, distance);
                float depth = ShadowMath.SliceSphere(sliceNear, sliceFar, extentX, extentY, orthographic, out float radius);
                radius += padding;
                Double3 center = first.Position + new Double3(viewForward.X * depth, viewForward.Y * depth, viewForward.Z * depth);
                cascade.Center = ShadowMath.Snap(center, right, up, forward, 2.0 * radius / resolution);
                cascade.Radius = radius;
                cascade.Valid = true;

                Float4x4 projection = ShadowMath.Orthographic(radius);
                ShadowTile whole = new(0, 0, (int)resolution);
                ViewLayer layer = new() { View = rotation, Projection = projection, Position = cascade.Center };
                DrawShadowView(cascade.Camera!, camera.World, cascade.Texture, whole, layer, new CullFrustum(projection * rotation, cascade.Center, nearPlane: false), -radius, radius);
                cascade.Target.Draws.Add(new ShadowDraw(cascade.Camera!, whole));
            }

            Double3 r = cascade.Center - first.Position;
            Float3 relative = new((float)r.X, (float)r.Y, (float)r.Z);
            state.Records.Add(new CascadeRecord
            {
                ViewToClip = ShadowMath.Orthographic(cascade.Radius) * rotation * Float4x4.CreateTranslation(-relative),
                Sphere = new Float4(relative.X, relative.Y, relative.Z, cascade.Radius),
                Params = new Float4(BitConverter.UInt32BitsToSingle(cascade.Slot), 1f / resolution, count, blend),
                Fade = new Float4(viewForward.X, viewForward.Y, viewForward.Z, distance),
            });
        }
    }

    private void ReleaseCascades(ViewShadows state, int at)
    {
        foreach (ViewShadows.Cascade cascade in state.Lights[at].Cascades)
        {
            if (cascade.Texture != null)
                _shadowRetired.Add(cascade.Texture);
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
            for (int i = 0; i < world.Shadows.CamerasUsed; i++)
                yield return world.Shadows.Cameras[i];
        }
        foreach (CameraFrame camera in AllViews())
        {
            foreach ((_, _, ViewShadows.Cascade[] cascades) in camera.Shadows.Lights)
            {
                foreach (ViewShadows.Cascade cascade in cascades)
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
