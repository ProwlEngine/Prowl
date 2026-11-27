using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>One layer of a view: a camera relative view matrix (rotation only), a reversed Z projection and the camera's world position.</summary>
public struct ViewLayer
{
    public Float4x4 View;
    public Float4x4 Projection;
    public Double3 Position;
}

[InlineArray(6)]
public struct ViewLayers
{
    private ViewLayer _first;
}

/// <summary>
/// Where a view sees from: one layer for mono, two for stereo or six for a cube, with the near plane and the logical far
/// distance effects read.
/// </summary>
public struct ViewCamera
{
    public ViewLayers Layers;

    /// <summary>1 for mono. Stereo and cube layers draw through multiview.</summary>
    public byte LayerCount;

    /// <summary>The projection's near plane, for linear depth.</summary>
    public float Near;

    /// <summary>The logical far distance, for effects that need one with an infinite projection.</summary>
    public float FarDistance;

    // The projections' X scale is for an aspect of 1, divided by the viewport's at SubmitFrame
    internal bool FitsViewport;

    /// <summary>A mono camera of one layer.</summary>
    public ViewCamera(in ViewLayer layer, float near = 0.1f, float farDistance = 1000)
        : this([layer], near, farDistance)
    {
    }

    /// <summary>A camera of one to six layers.</summary>
    public ViewCamera(ReadOnlySpan<ViewLayer> layers, float near = 0.1f, float farDistance = 1000)
    {
        if (layers.Length is < 1 or > 6)
            throw new ArgumentException($"A camera has 1 to 6 layers, {layers.Length} were given.", nameof(layers));
        for (int i = 0; i < layers.Length; i++)
            Layers[i] = layers[i];
        LayerCount = (byte)layers.Length;
        Near = near;
        FarDistance = farDistance;
    }

    /// <summary>
    /// A perspective camera looking along a unit forward, reversed Z with an infinite far plane, up on screen the way up
    /// leans. The field of view is vertical in radians, the aspect width over height, 0 for the viewport's.
    /// </summary>
    public static ViewCamera Perspective(Double3 position, Float3 forward, Float3 up, float fieldOfView, float near = 0.1f, float aspect = 0, float farDistance = 1000)
    {
        float focal = 1f / MathF.Tan(fieldOfView * 0.5f);
        Float4x4 projection = new(new Float4(aspect > 0 ? focal / aspect : focal, 0, 0, 0), new Float4(0, -focal, 0, 0), new Float4(0, 0, 0, 1), new Float4(0, 0, near, 0));
        return new ViewCamera(new ViewLayer { View = LookAlong(forward, up), Projection = projection, Position = position }, near, farDistance) { FitsViewport = aspect <= 0 };
    }

    /// <summary>
    /// An orthographic camera looking along a unit forward, seeing height units top to bottom and reversed Z from near to
    /// far, up on screen the way up leans. The aspect is width over height, 0 for the viewport's.
    /// </summary>
    public static ViewCamera Orthographic(Double3 position, Float3 forward, Float3 up, float height, float near, float far, float aspect = 0)
    {
        float scale = 2f / height, range = far - near;
        Float4x4 projection = new(new Float4(aspect > 0 ? scale / aspect : scale, 0, 0, 0), new Float4(0, -scale, 0, 0), new Float4(0, 0, -1f / range, 0), new Float4(0, 0, far / range, 1));
        return new ViewCamera(new ViewLayer { View = LookAlong(forward, up), Projection = projection, Position = position }, near, far) { FitsViewport = aspect <= 0 };
    }

    private static Float4x4 LookAlong(Float3 forward, Float3 up)
    {
        Float3 right = Float3.Normalize(Float3.Cross(up, forward));
        Float3 u = Float3.Cross(forward, right);
        return new Float4x4(new Float4(right.X, u.X, forward.X, 0), new Float4(right.Y, u.Y, forward.Y, 0), new Float4(right.Z, u.Z, forward.Z, 0), new Float4(0, 0, 0, 1));
    }
}

/// <summary>
/// A camera into a world, drawn each frame it is submitted in. It keeps what carries over between its frames, such as
/// temporal history, exposure and shadow cascades, until it is disposed. Made by <see cref="Renderer.CreateView"/>,
/// host thread.
/// </summary>
/// <summary>How a view lights its opaque surfaces. A host's views are deferred by default, the renderer's own captures forward.</summary>
public enum RenderPath : byte
{
    /// <summary>Every surface is lit as it is drawn.</summary>
    Forward,

    /// <summary>
    /// Opaque surfaces whose shader has a GBuffer pass are drawn into the view's GBuffer and lit from it, see
    /// <see cref="GBufferFeature"/> and <see cref="DeferredLightingFeature"/>. The others are lit as they are drawn.
    /// Screen space lighting and reflections work in this path only.
    /// </summary>
    Deferred,
}

/// <summary>
/// Which of a deferred view's GBuffer surfaces its depth prepass draws first, so the GBuffer pass shades only the pixels
/// they own. Surfaces lit as they are drawn are always in the prepass.
/// </summary>
public enum EarlyDepth : byte
{
    /// <summary>Every surface that has a Prepass pass.</summary>
    All,

    /// <summary>The alpha cut surfaces alone, whose hidden layers would each run the GBuffer pass to be cut out.</summary>
    AlphaCut,

    /// <summary>None: the GBuffer pass draws them alone.</summary>
    None,
}

/// <summary>What transparent items sort back to front by.</summary>
public enum TransparencySortMode : byte
{
    /// <summary>Orthographic for an orthographic view, perspective otherwise.</summary>
    Default,

    /// <summary>The distance from the camera to the item's centre.</summary>
    Perspective,

    /// <summary>The depth of the item's centre along the view direction.</summary>
    Orthographic,

    /// <summary>How far the item's centre is along <see cref="TransparencySort.Axis"/>, furthest drawn first.</summary>
    CustomAxis,
}

/// <summary>A view's transparency sort, its axis read by <see cref="TransparencySortMode.CustomAxis"/> alone.</summary>
public readonly record struct TransparencySort(TransparencySortMode Mode = TransparencySortMode.Default, Float3 Axis = default);

/// <summary>The paths a feature works in.</summary>
[Flags]
public enum RenderPaths : byte
{
    Forward = 1 << RenderPath.Forward,
    Deferred = 1 << RenderPath.Deferred,
    All = Forward | Deferred,
}

public sealed class RenderView : IDisposable
{
    /// <summary>How the view lights its opaque surfaces, deferred unless set.</summary>
    public RenderPath Path { get; set; } = RenderPath.Deferred;

    /// <summary>Which GBuffer surfaces a deferred view's prepass draws first.</summary>
    public EarlyDepth EarlyDepth { get; set; }

    /// <summary>What the view sorts transparent items back to front by.</summary>
    public TransparencySort TransparencySort { get; set; }

    /// <summary>
    /// The pixels a cell of the view's screen space displacement spans, 2 by default, 0 for none: surfaces that keep a
    /// height are pushed out by a grid with a corner every this many pixels, so a smaller cell follows finer relief
    /// and costs more. Deferred views only. See <see cref="DisplacementFeature"/>.
    /// </summary>
    public int DisplacementCell { get; set; } = 2;

    /// <summary>
    /// The pixels over which the way the view's displacement lifts a surface blends across the hard creases in it, 3
    /// by default, so the faces at a crease lift along the same line where they meet and stay joined. 0 lifts each
    /// straight along its own normal, which opens a gap at every crease.
    /// </summary>
    public int DisplacementCreaseBlend { get; set; } = 3;

    private readonly Renderer _renderer;
    private bool _cut;

    internal RenderView(Renderer renderer, int id, RenderWorld world, string? name)
    {
        _renderer = renderer;
        Id = id;
        World = world;
        Name = name;
    }

    /// <summary>Keys the view's state in the frame pipeline.</summary>
    internal int Id { get; }

    public RenderWorld World { get; set; }

    /// <summary>Name debug tools show for the view, or null.</summary>
    public string? Name { get; set; }

    public ViewCamera Camera { get; set; }

    /// <summary>What the view draws into. A swapchain framebuffer presents after the frame.</summary>
    public Framebuffer? Target { get; set; }

    public uint CullingMask { get; set; } = ~0u;

    /// <summary>Multiplies the screen size every renderable's level of detail is chosen by in this view. Above 1 keeps detail longer.</summary>
    public float LodBias { get; set; } = 1;

    /// <summary>The level of detail every renderable draws at in this view, 0 their own mesh, or -1 to choose by size.</summary>
    public int ForcedLod { get; set; } = -1;

    /// <summary>How this view switches levels of detail over the world's <see cref="LodSettings.Transition"/>, or null for the world's.</summary>
    public LodTransition? LodTransition { get; set; }

    /// <summary>Its alpha is ignored, views clear opaque.</summary>
    public Float4 ClearColor { get; set; }

    /// <summary>Whether the world's sky draws behind what the view sees.</summary>
    public bool Sky { get; set; } = true;

    /// <summary>
    /// The camera's exposure value at ISO 100, which every light the view draws is pre-exposed by through
    /// <see cref="ExposureOf"/>. An <see cref="AutoExposure"/> on the view replaces it with its own from its second frame.
    /// </summary>
    public float EV100 { get; set; }

    /// <summary>What the view is for, which decides the features that run on it.</summary>
    public ViewKind Kind { get; set; }

    /// <summary>The part of the target the view draws into, as x, y, width and height from 0 to 1, y down from the top.</summary>
    public Float4 Viewport { get; set; } = new(0, 0, 1, 1);

    /// <summary>Clears the whole target to the clear colour before drawing into the viewport, for a view letterboxed into its target.</summary>
    public bool ClearTarget { get; set; }

    /// <summary>
    /// Draws over what its target already holds, such as views of a lower <see cref="Order"/>: the view starts transparent
    /// without sky or clear colour, and its colour, premultiplied by its alpha as blending over transparent leaves it, is
    /// blended over the target: transparent and additive surfaces and glow reach the views under it. For stacked cameras
    /// such as a first person weapon or world space UI.
    /// </summary>
    public bool Overlay { get; set; }

    /// <summary>Render resolution against the viewport's size in the target, presented scaled to fit.</summary>
    public float RenderScale { get; set; } = 1;

    /// <summary>
    /// Renders the scene into 8 bit sRGB colour instead of 16 bit float: half the bandwidth, and light above 1 clips. For
    /// views with no tonemapper.
    /// </summary>
    public bool Ldr { get; set; }

    /// <summary>Features of this view alone, over the world's.</summary>
    public ExtensionSet<RenderFeature>? Features { get; set; }

    /// <summary>Values of global axes for this view alone, over the world's, such as GlobalAxis.PbrTier["LITE"]. Copied at SubmitFrame.</summary>
    public GlobalKeyword[]? Keywords { get; set; }

    /// <summary>Views draw lowest Order first, each after the views it depends on.</summary>
    public int Order { get; set; }

    /// <summary>Views that draw before this one when submitted in the same frame, such as ones whose targets it samples.</summary>
    public List<RenderView> DependsOn { get; } = new();

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// The enabled features of the view and its world that do not work in the view's <see cref="Path"/> and so are
    /// left out of it, for a tool to tell its user. Host thread.
    /// </summary>
    public List<RenderFeature> SkippedFeatures()
    {
        List<RenderFeature> skipped = new();
        RenderPaths path = (RenderPaths)(1 << (int)Path);
        ViewKinds kind = (ViewKinds)(1 << (int)Kind);
        foreach (RenderFeature feature in World.Features)
        {
            if (feature.Enabled && (feature.RunsOn & kind) != 0 && (feature.Paths & path) == 0)
                skipped.Add(feature);
        }
        foreach (RenderFeature feature in Features ?? [])
        {
            if (feature.Enabled && (feature.RunsOn & kind) != 0 && (feature.Paths & path) == 0 && !skipped.Contains(feature))
                skipped.Add(feature);
        }
        return skipped;
    }

    /// <summary>
    /// Marks the camera as jumped, like a teleport or a scene switch: the next frame has no previous one, so motion is
    /// none and every feature's history starts over.
    /// </summary>
    public void Cut() => _cut = true;

    /// <summary>Frees the view's state once the frames drawing it are done. Host thread.</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        _renderer.ReleaseView(this);
    }

    private static Keyword ToKeyword(GlobalKeyword keyword)
        => keyword.Axis is { } axis ? new Keyword(axis.Name, keyword.Value) : throw new ArgumentException("A view keyword has no axis, make it with an axis's indexer.");

    /// <summary>The scale an exposure value at ISO 100 gives light, 1 / (1.2 * 2^EV100), as a camera of ISO 100 and K 12.5 sees it.</summary>
    public static float ExposureOf(float ev100) => 1f / (1.2f * MathF.Pow(2f, ev100));

    // What the frame draws the view with, its cut taken. Host thread, at SubmitFrame.
    internal ViewDesc Snapshot()
    {
        ViewCamera camera = Camera;
        if (camera.FitsViewport && Target is { } target)
        {
            float aspect = target.Width * Viewport.Z / Math.Max(target.Height * Viewport.W, 1);
            for (int l = 0; l < camera.LayerCount; l++)
                camera.Layers[l].Projection.c0.X /= aspect;
        }
        ViewDesc view = new()
        {
            World = World,
            Owner = this,
            ViewId = Id,
            Name = Name,
            Layers = camera.Layers,
            LayerCount = camera.LayerCount,
            Target = Target!,
            CullingMask = CullingMask,
            LodBias = LodBias,
            ForcedLod = ForcedLod,
            LodTransition = LodTransition,
            Path = Path,
            EarlyDepth = EarlyDepth,
            TransparencySort = TransparencySort,
            DisplacementCell = DisplacementCell,
            DisplacementCreaseBlend = DisplacementCreaseBlend,
            ClearColor = ClearColor,
            Sky = Sky,
            Near = camera.Near,
            FarDistance = camera.FarDistance,
            EV100 = EV100,
            Kind = Kind,
            Viewport = Viewport,
            ClearTarget = ClearTarget,
            Overlay = Overlay,
            RenderScale = RenderScale,
            Ldr = Ldr,
            Cut = _cut,
            Features = Features,
            Keywords = Keywords is { Length: > 0 } keywords ? Array.ConvertAll(keywords, ToKeyword) : null,
            Order = Order,
            DependsOn = DependsOn.Count > 0 ? DependsOn.ToArray() : null,
        };
        _cut = false;
        return view;
    }
}

/// <summary>A view as one frame draws it: a host's view snapshot at SubmitFrame, or one of the renderer's own captures.</summary>
internal struct ViewDesc
{
    public RenderWorld World;

    /// <summary>The host's view, null for the renderer's own.</summary>
    public RenderView? Owner;

    /// <summary>Unique per renderer, keys per view state.</summary>
    public int ViewId;
    public string? Name;
    public ViewLayers Layers;
    public byte LayerCount;
    public Framebuffer Target;
    public uint CullingMask;
    public float LodBias;
    public int ForcedLod;
    public LodTransition? LodTransition;
    public RenderPath Path;
    public EarlyDepth EarlyDepth;
    public TransparencySort TransparencySort;
    public int DisplacementCell;
    public int DisplacementCreaseBlend;
    public Float4 ClearColor;
    public bool Sky;
    public float Near;
    public float FarDistance;
    public float EV100;
    public ViewKind Kind;
    public Float4 Viewport;
    public bool ClearTarget;
    public bool Overlay;
    public float RenderScale;
    public bool Ldr;
    public bool Cut;
    public ExtensionSet<RenderFeature>? Features;
    public Keyword[]? Keywords;
    public int Order;
    public RenderView[]? DependsOn;

    public ViewDesc()
    {
        World = null!;
        Target = null!;
        LayerCount = 1;
        CullingMask = ~0u;
        LodBias = 1;
        ForcedLod = -1;
        Sky = true;
        Near = 0.1f;
        FarDistance = 1000;
        Viewport = new Float4(0, 0, 1, 1);
        RenderScale = 1;
    }
}
