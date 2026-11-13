using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A kind of grass the terrain's layers grow, see <see cref="TerrainLayerDesc.Grass"/>: cards of two crossed quads
/// showing a slice of <see cref="TerrainSurface.GrassCards"/>.
/// </summary>
public sealed class TerrainGrassType
{
    /// <summary>The group of layers it grows on: every type of a group grows where a layer names it, so several can share ground.</summary>
    public int Group { get; init; }

    /// <summary>Cards per square metre where its layer has full weight.</summary>
    public float Density { get; init; } = 6f;

    /// <summary>The metres from the camera it reaches, fading out over the last three tenths.</summary>
    public float Range { get; init; } = 90f;

    /// <summary>
    /// The metres within which it grows at full density. Past it fewer cards survive, falling with the square of the
    /// distance so about as many cover each pixel, each wider to keep the ground covered, up to three times.
    /// </summary>
    public float LodDistance { get; init; } = 10f;

    /// <summary>The smallest and largest height in metres, and the width of a card.</summary>
    public float MinHeight { get; init; } = 0.25f;
    public float MaxHeight { get; init; } = 0.45f;
    public float Width { get; init; } = 0.7f;

    /// <summary>
    /// How far its colour leans toward the ground's colour under it, 0 to 1, so grass takes on the floor's variation:
    /// fully at the root, half as much at the tip.
    /// </summary>
    public float GroundTint { get; init; } = 0.5f;

    /// <summary>The colour at the root and at the tip, linear.</summary>
    public Float3 RootColor { get; init; } = new(0.03f, 0.08f, 0.015f);
    public Float3 TipColor { get; init; } = new(0.2f, 0.32f, 0.07f);

    /// <summary>
    /// Smooth noise across the field picks where between <see cref="MinHeight"/> and <see cref="MaxHeight"/> a spot's
    /// grass grows, so it rises and falls in patches, and scales its width: the metres a patch spans, how far each card
    /// strays from its patch's height as a share of the range, and how far the width is scaled either way, 0 to 1.
    /// </summary>
    public float NoiseSize { get; init; } = 6f;
    public float HeightJitter { get; init; } = 0.15f;
    public float WidthNoise { get; init; } = 0.25f;

    /// <summary>How little the wind moves it, 0 for fully.</summary>
    public float Stiffness { get; init; } = 0.3f;

    /// <summary>The slice of <see cref="TerrainSurface.GrassCards"/> it shows.</summary>
    public int Card { get; init; }
}

/// <summary>
/// Grows the terrain's grass each frame for each camera view slot, by compute, see Graphite/Internal/TerrainGrass.glsl:
/// for each grass type a jittered grid anchored in the world around the slot's camera, each point kept by the weight of
/// the layers growing that type under it, thinned with distance and culled to the view. Kept points land in an instance
/// texture by slot, and their counts in a buffer the draws read with <see cref="DrawItemSink.AddIndirect"/>.
/// </summary>
internal sealed class TerrainGrass : IDisposable
{
    public const int Capacity = 1 << 18;
    public const int Rows = Capacity * 2 / Width;
    public const int Width = 1024;
    public const int TypeTexels = 5;
    public static readonly RenderResourceID CountsName = "Graphite.TerrainGrassCounts";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Push
    {
        public Float4 Grid, Place, Terrain, Extra;
    }

    public sealed class Frame
    {
        public readonly List<Push> Fills = new();
        public readonly float[] Views;
        public readonly Double3[] Origins;
        public bool Any;

        public Frame(int slots)
        {
            Views = new float[slots * 8 * 4];
            Origins = new Double3[slots];
        }
    }

    private readonly InternalCompute _program;
    private readonly PropertySet _properties = new();
    private readonly Sampler _point, _linear;
    private BufferHandle _counts;

    public TerrainGrass(Renderer renderer, int slots)
    {
        Slots = slots;
        ResourceFactory factory = renderer.Device.ResourceFactory;
        Instances = factory.CreateTexture(TextureDescription.Texture2D(Width, (uint)(Rows * slots), 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Storage | TextureUsage.Sampled));
        Instances.Name = "Graphite Terrain Grass";
        Views = factory.CreateTexture(TextureDescription.Texture2D(8, (uint)slots, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        Types = factory.CreateTexture(TextureDescription.Texture2D(TypeTexels, 64, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        _program = renderer.Effects.Compute("Internal/TerrainGrass.glsl");
        _point = factory.CreateSampler(SamplerDescription.Point);
        _linear = factory.CreateSampler(SamplerDescription.Linear);
        Current = new Frame(slots);
    }

    public int Slots { get; }

    /// <summary>Per slot, Rows rows of two texels an instance: its spot from the slot's origin and facing, then its height, width, ground colour and type.</summary>
    public Texture Instances { get; }

    /// <summary>Per slot a row: the camera's frustum planes from the slot's origin, then the camera from the origin and the plane count.</summary>
    public Texture Views { get; }

    /// <summary>Per type a row, see <see cref="WriteTypes"/>.</summary>
    public Texture Types { get; }

    public Frame Current { get; private set; }

    private float[]? _types;

    /// <summary>Packs the types for the GPU, written with the next frame.</summary>
    public void SetTypes(IReadOnlyList<TerrainGrassType> types)
    {
        float[] data = new float[64 * TypeTexels * 4];
        for (int t = 0; t < Math.Min(types.Count, 64); t++)
        {
            TerrainGrassType type = types[t];
            int at = t * TypeTexels * 4;
            (data[at], data[at + 1], data[at + 2], data[at + 3]) = (0f, type.MinHeight, MathF.Max(type.MaxHeight, type.MinHeight), type.Width);
            (data[at + 4], data[at + 5], data[at + 6], data[at + 7]) = (type.RootColor.X, type.RootColor.Y, type.RootColor.Z, Math.Clamp(type.GroundTint, 0f, 1f));
            (data[at + 8], data[at + 9], data[at + 10], data[at + 11]) = (type.TipColor.X, type.TipColor.Y, type.TipColor.Z, type.Stiffness);
            (data[at + 12], data[at + 13], data[at + 14], data[at + 15]) = (type.Density, type.Range, type.Card, MathF.Max(type.LodDistance, 0.1f));
            (data[at + 16], data[at + 17], data[at + 18], data[at + 19]) = (type.Group, MathF.Max(type.NoiseSize, 0.01f), Math.Clamp(type.HeightJitter, 0f, 1f), Math.Clamp(type.WidthNoise, 0f, 1f));
        }
        _types = data;
    }

    /// <summary>
    /// Picks the frame's fills for each slot with a camera this frame: its frustum and where its origin sits, snapped so
    /// instance positions stay small. frustums holds the frustum each slot's view culled with last, null for slots not
    /// seen. Main thread.
    /// </summary>
    public void Update(IReadOnlyList<CullFrustum?> frustums, IReadOnlyList<TerrainGrassType> types, TerrainData data, Double3 position)
    {
        Frame frame = new(Slots);
        TerrainHeader header = data.Header;
        Float4 terrain = new((float)header.Bottom, (float)header.Range, (float)header.Spacing, header.Levels);
        for (int slot = 0; slot < Slots; slot++)
        {
            if (frustums[slot] is not { } frustum)
                continue;
            Double3 eye = frustum.Camera;
            Double3 origin = new(Math.Floor(eye.X / 64) * 64, Math.Floor(eye.Y / 64) * 64, Math.Floor(eye.Z / 64) * 64);
            frame.Origins[slot] = origin;
            Float3 shift = new((float)(eye.X - origin.X), (float)(eye.Y - origin.Y), (float)(eye.Z - origin.Z));
            int row = slot * 8 * 4;
            int planes = 0;
            if (frustum.Layers == 1)
            {
                planes = frustum.PlaneCount(0);
                for (int p = 0; p < planes; p++)
                {
                    // The plane moved from the camera to the origin
                    Float4 plane = frustum.Plane(0, p);
                    float w = plane.W - (plane.X * shift.X + plane.Y * shift.Y + plane.Z * shift.Z);
                    (frame.Views[row + p * 4], frame.Views[row + p * 4 + 1], frame.Views[row + p * 4 + 2], frame.Views[row + p * 4 + 3]) = (plane.X, plane.Y, plane.Z, w);
                }
            }
            (frame.Views[row + 24], frame.Views[row + 25], frame.Views[row + 26], frame.Views[row + 27]) = (shift.X, shift.Y, shift.Z, planes);
            Double2 local = new(eye.X - position.X, eye.Z - position.Z);
            (frame.Views[row + 28], frame.Views[row + 29], frame.Views[row + 30], frame.Views[row + 31]) = ((float)(origin.X - position.X), (float)(origin.Z - position.Z), (float)(origin.Y - position.Y), 0f);

            for (int t = 0; t < Math.Min(types.Count, 64); t++)
            {
                TerrainGrassType type = types[t];
                if (type.Density <= 0 || type.Range <= 0)
                    continue;
                double spacing = 1.0 / Math.Sqrt(type.Density);
                long x0 = (long)Math.Floor((local.X - type.Range) / spacing), z0 = (long)Math.Floor((local.Y - type.Range) / spacing);
                int cells = (int)Math.Ceiling(2 * type.Range / spacing) + 1;
                frame.Fills.Add(new Push
                {
                    Grid = new Float4(x0, z0, (float)spacing, cells),
                    Place = new Float4(slot, t, 0f, 0f),
                    Terrain = terrain,
                    Extra = new Float4((float)local.X, (float)local.Y, type.Range, 0f),
                });
                frame.Any = true;
            }
        }
        Current = frame;
    }

    public void Setup(FrameBuilder builder)
    {
        _counts = builder.Buffer(CountsName, GraphBufferDesc.Structured((uint)(Slots * 4), 4));
        builder.Export(_counts);
        builder.ExportToDraws(builder.Write(Instances, TextureState.Storage));
        builder.ExportToDraws(builder.Write(Views, TextureState.TransferDst | TextureState.Sampled));
        builder.ExportToDraws(builder.Write(Types, TextureState.TransferDst | TextureState.Sampled));
    }

    /// <summary>Clears the counts and runs the frame's fills. Render thread.</summary>
    public void Render(FrameContext context, Frame frame, TerrainPool pool, TerrainLayers layers)
    {
        CommandBuffer cmd = context.Command;
        DeviceBuffer counts = context.Get(_counts);
        cmd.UpdateBuffer(counts, 0, new uint[Slots]);
        cmd.UpdateTexture<float>(Views, frame.Views, TextureRegion.Whole(Views, 0, 0));
        if (_types != null)
        {
            cmd.UpdateTexture<float>(Types, _types, TextureRegion.Whole(Types, 0, 0));
            _types = null;
        }
        if (frame.Fills.Count == 0)
            return;
        cmd.ComputeBarrier();
        _properties.SetTexture("Terrain_GrassInstances", Instances);
        _properties.SetTexture("Terrain_GrassViews", Views, _point);
        _properties.SetTexture("Terrain_GrassTypes", Types, _point);
        _properties.SetBuffer("Terrain_GrassCounts", counts);
        _properties.SetTexture("Terrain_PageTable", pool.PageTable, _point);
        _properties.SetTexture("_TerrainHeights", pool.Heights, _linear);
        _properties.SetTexture("_TerrainControl", pool.Control, _point);
        _properties.SetTexture("_TerrainLayerData", layers.Data, _point);
        _properties.SetTexture("_TerrainAlbedo", layers.Albedo, _linear);
        _properties.SetTexture("_TerrainSurface", layers.Surface, _linear);
        _properties.SetFloat4("_TerrainSpacing", new Float4(1f, layers.Count, 0f, 0f));
        cmd.SetComputeShader(_program.Program);
        cmd.SetProperties(_properties);
        foreach (Push push in frame.Fills)
        {
            cmd.SetPushConstants(push);
            uint groups = (uint)(push.Grid.W + 7) / 8;
            cmd.Dispatch(groups, groups, 1);
        }
    }

    public void Dispose()
    {
        _program.Dispose();
        _point.Dispose();
        _linear.Dispose();
        Instances.Dispose();
        Views.Dispose();
        Types.Dispose();
    }
}
