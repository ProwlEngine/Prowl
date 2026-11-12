using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A coarser level of a <see cref="LodSet"/>: its mesh and the screen size below which it takes over.</summary>
public readonly struct LodLevel
{
    public LodLevel(GpuMesh mesh, float screenSize, bool castShadows = true)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Mesh = mesh;
        ScreenSize = screenSize;
        CastShadows = castShadows;
    }

    public GpuMesh Mesh { get; }

    /// <summary>The renderable's projected diameter over the view height, 0 to 1, below which this level draws.</summary>
    public float ScreenSize { get; }

    /// <summary>Whether the level casts shadows when the renderable does.</summary>
    public bool CastShadows { get; }
}

/// <summary>
/// The coarser meshes a renderable draws in place of its own as it gets smaller on screen, chosen per view. The
/// renderable's mesh is level 0, these are levels 1 on. Immutable and shared by any number of renderables. Submeshes
/// take the renderable's materials by index, the last repeating, as its own mesh's do.
/// </summary>
public sealed class LodSet
{
    /// <summary>The most levels a set holds, so 8 with the renderable's own mesh.</summary>
    public const int MaxLevels = 7;

    private readonly LodLevel[] _levels;

    /// <param name="levels">From the finest to the coarsest, their screen sizes falling.</param>
    /// <param name="cullScreenSize">The screen size below which nothing draws, under the last level's.</param>
    public LodSet(ReadOnlySpan<LodLevel> levels, float cullScreenSize = 0)
    {
        if (levels.IsEmpty || levels.Length > MaxLevels)
            throw new ArgumentException($"A LOD set holds 1 to {MaxLevels} levels, {levels.Length} were given.", nameof(levels));
        float previous = float.PositiveInfinity;
        foreach (LodLevel level in levels)
        {
            if (level.Mesh == null)
                throw new ArgumentException("Every level of a LOD set needs a mesh.", nameof(levels));
            if (!(level.ScreenSize > 0) || level.ScreenSize >= previous)
                throw new ArgumentException("A LOD set's screen sizes must be above 0 and fall from level to level.", nameof(levels));
            previous = level.ScreenSize;
        }
        if (!(cullScreenSize >= 0) || cullScreenSize >= previous)
            throw new ArgumentException("A LOD set's cull size must be at least 0 and under its last level's.", nameof(cullScreenSize));
        _levels = levels.ToArray();
        CullScreenSize = cullScreenSize;
    }

    public IReadOnlyList<LodLevel> Levels => _levels;

    /// <summary>The screen size below which the renderable draws nothing.</summary>
    public float CullScreenSize { get; }
}

/// <summary>A world's LOD rules. Host thread, read at SubmitFrame.</summary>
public sealed class LodSettings
{
    /// <summary>Multiplies every renderable's screen size, the quality knob. Above 1 keeps detail longer.</summary>
    public float Bias = 1;

    /// <summary>The finest level any view draws, so 1 skips every renderable's own mesh where it has coarser levels.</summary>
    public int FinestLevel;

    /// <summary>The share of a level's screen size a renderable must fall under before a view moves it coarser.</summary>
    public float Hysteresis = 0.1f;

    /// <summary>Levels coarser than their camera's choice that shadow cascades draw.</summary>
    public int ShadowOffset;

    /// <summary>Levels coarser than their size that reflection probe captures draw.</summary>
    public int CaptureOffset = 1;

    /// <summary>Levels coarser than their size that the voxel volume draws.</summary>
    public int VoxelOffset = 1;

    /// <summary>
    /// The view height in pixels that local light shadow tiles and voxel views are judged against: a tile this many
    /// texels high picks the level a camera that many pixels high would, whatever the cameras do.
    /// </summary>
    public float ReferenceHeight = 1080;

    internal LodSettings Copy() => (LodSettings)MemberwiseClone();
}

/// <summary>The levels a view chose last time, so its next choice can hold them under hysteresis. Render thread.</summary>
internal sealed class LodState
{
    private byte[] _level = [];
    private uint[] _seen = [];
    private uint _stamp = 1;

    /// <summary>Starts a cull: choices of the last one are what a renderable seen there holds.</summary>
    public void Begin(int capacity)
    {
        if (_level.Length < capacity)
        {
            Array.Resize(ref _level, capacity);
            Array.Resize(ref _seen, capacity);
        }
        _stamp++;
    }

    /// <summary>The level the last cull chose for a renderable, -1 when it did not choose one.</summary>
    public int Previous(int index) => index < _seen.Length && _seen[index] == _stamp - 1 ? _level[index] : -1;

    public void Keep(int index, int level)
    {
        _level[index] = (byte)level;
        _seen[index] = _stamp;
    }
}

/// <summary>
/// How one view picks levels: a renderable's screen size is its radius times Scale over its distance from Position,
/// or times Scale alone when orthographic, then its own bias. Scale holds the projection's cot(fov / 2), the world and
/// view bias, and for texel views their size over the reference height.
/// </summary>
internal readonly record struct LodView(Double3 Position, float Scale, bool Orthographic)
{
    /// <summary>Levels coarser than the size picks.</summary>
    public int Offset { get; init; }

    /// <summary>The level every renderable draws, -1 to choose by size.</summary>
    public int Forced { get; init; } = -1;

    /// <summary>The finest level drawn.</summary>
    public int Finest { get; init; }

    public float Hysteresis { get; init; }

    /// <summary>The view's choices, read for hysteresis and kept, or null for none.</summary>
    public LodState? State { get; init; }

    /// <summary>Draws every renderable's own mesh.</summary>
    public static LodView OwnMesh { get; } = new(Double3.Zero, 0, false) { Forced = 0 };

    /// <summary>A view's choice through its first layer: cameras hold their levels under hysteresis, captures draw coarser.</summary>
    public static LodView Of(in ViewDesc view, LodSettings settings, LodState? state)
    {
        ViewLayer layer = view.Layers[0];
        bool camera = ((ViewKinds)(1 << (int)view.Kind) & ViewKinds.Cameras) != 0;
        bool capture = ((ViewKinds)(1 << (int)view.Kind) & ViewKinds.Captures) != 0;
        return new LodView(layer.Position, MathF.Abs(layer.Projection.c1.Y) * settings.Bias * view.LodBias, layer.Projection.c2.W == 0)
        {
            Offset = capture ? settings.CaptureOffset : 0,
            Forced = view.ForcedLod,
            Finest = settings.FinestLevel,
            Hysteresis = camera ? settings.Hysteresis : 0,
            State = camera ? state : null,
        };
    }

    /// <summary>A view into a texture, judged by its texels across the layer's height as a camera that many pixels high would be.</summary>
    public static LodView Texels(in ViewLayer layer, float texels, LodSettings settings, int offset)
        => new(layer.Position, MathF.Abs(layer.Projection.c1.Y) * texels / settings.ReferenceHeight * settings.Bias, layer.Projection.c2.W == 0)
        {
            Offset = offset,
            Finest = settings.FinestLevel,
        };
}
