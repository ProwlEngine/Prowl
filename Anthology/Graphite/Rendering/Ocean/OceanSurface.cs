using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The ocean's surface: rings of tiles around the viewer, the innermost a 4 by 4 grid of tiles and each ring after it
/// the 12 tiles around the one inside at twice the size, the last with a skirt out toward the horizon. The rings grow
/// with the viewer's height above the water in steps of two, the innermost fading into the next as it nears a step.
/// Drawn with <see cref="Material"/>, which samples <see cref="Ocean"/>'s waves, through the <see cref="Pass"/> pass by
/// <see cref="DrawFeature"/>, which the host adds to the world's features beside the surface.
/// </summary>
public sealed class OceanSurface : RenderProvider, IDisposable
{
    private readonly GpuMesh _centre, _ring, _outer;
    private int _rings = 7, _quads;
    private float _minScale = 8f, _maxScale = 256f, _maxVertical = 10f;
    private double _seaLevel;
    private Double3? _viewer;
    private Double3 _depthViewer;
    private readonly float[] _sizes = new float[OceanFeature.Cascades];
    private readonly Texture _foamTexture, _caustics, _normals;
    private Texture? _foamOverride, _causticsOverride, _normalsOverride;

    /// <summary>The caustics texture and wave normal map, for the underwater pass to light the floor with too.</summary>
    internal Texture Caustics => _causticsOverride ?? _caustics;
    internal Texture Normals => _normalsOverride ?? _normals;

    /// <summary>Draws foam with a texture's red channel in place of the library's, the library's again with null.</summary>
    public void SetFoamTexture(Texture? texture, SamplerDescription? sampler = null)
    {
        if (texture == _foamOverride)
            return;
        _foamOverride = texture;
        Material.SetTexture("_OceanFoamTexture", texture ?? _foamTexture, texture == null ? null : sampler);
    }

    /// <summary>Lights the floor with a caustics texture in place of the library's, the library's again with null.</summary>
    public void SetCausticsTexture(Texture? texture, SamplerDescription? sampler = null)
    {
        if (texture == _causticsOverride)
            return;
        _causticsOverride = texture;
        Material.SetTexture("_OceanCaustics", texture ?? _caustics, texture == null ? null : sampler);
    }

    /// <summary>Details the waves with a normal map in place of the library's, the library's again with null.</summary>
    public void SetNormalsTexture(Texture? texture, SamplerDescription? sampler = null)
    {
        if (texture == _normalsOverride)
            return;
        _normalsOverride = texture;
        Material.SetTexture("_OceanNormals", texture ?? _normals, texture == null ? null : sampler);
    }

    /// <summary>Sets the caustics' shader values through a setter, the surface's material's or the underwater pass's.</summary>
    internal void SetCaustics(Action<PropertyID, float> set)
    {
        set("_CausticsTextureScale", MathF.Max(CausticsScale, 1e-3f));
        set("_CausticsTextureAverage", CausticsGreyPoint);
        set("_CausticsStrength", MathF.Max(CausticsStrength, 0f));
        set("_CausticsFocalDepth", CausticsFocalDepth);
        set("_CausticsDepthOfField", MathF.Max(CausticsDepthOfField, 0.01f));
        set("_CausticsDistortionStrength", CausticsDistortion);
        set("_CausticsDistortionScale", MathF.Max(CausticsDistortionScale, 1e-3f));
    }

    /// <param name="ocean">The waves the surface takes.</param>
    /// <param name="quadsPerTile">Quads along each side of a tile, even.</param>
    public OceanSurface(Renderer renderer, OceanFeature ocean, int quadsPerTile = 32) : base(ViewKinds.Cameras | ViewKinds.Captures)
    {
        if (quadsPerTile < 4 || quadsPerTile % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(quadsPerTile), "Tiles have an even number of quads, at least 4.");
        Ocean = ocean;
        _quads = quadsPerTile;
        Shader shader = renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Ocean/Water.shader")!));
        Material = renderer.CreateMaterial(shader);
        Material.SetTexture("_OceanDisplacement", ocean.Displacement);
        Material.SetTexture("_OceanSlopes", ocean.Slopes);
        Material.SetTexture("_OceanFoam", ocean.Foam);
        Material.SetTexture("_OceanDepth", ocean.Depth);
        Material.SetTexture("_OceanLevels", ocean.Levels);
        Material.SetTexture("_OceanFlow", ocean.Flow);
        Material.SetTexture("_OceanClip", ocean.Clip);
        Material.SetTexture("_OceanWhitecaps", ocean.Whitecaps);
        Material.SetTexture("_OceanRipples", ocean.Ripples);
        Material.SetTexture("_OceanState", ocean.State);
        _foamTexture = RendererResources.LoadData(renderer.Device, "OceanFoam.bin", 512, 512, mipmapped: true, edit: pixels => FlattenFoam(pixels, 512));
        _caustics = RendererResources.LoadData(renderer.Device, "OceanCaustics.bin", 630, 630, mipmapped: true, srgb: true);
        Material.SetTexture("_OceanFoamTexture", _foamTexture);
        _normals = RendererResources.LoadData(renderer.Device, "OceanNormals.bin", 1024, 1024, mipmapped: true);
        Material.SetTexture("_OceanCaustics", _caustics);
        Material.SetTexture("_OceanNormals", _normals);

        // Chunks keep their place across but take any height, as waves and the planet's curve move them up and down
        Float3 margin = new(2f, 1e5f, 2f);
        _centre = renderer.CreateMesh(Grid(quadsPerTile, hole: false, skirt: 0f, margin));
        _ring = renderer.CreateMesh(Grid(quadsPerTile, hole: true, skirt: 0f, margin));
        _outer = renderer.CreateMesh(Grid(quadsPerTile, hole: true, skirt: SkirtReach, margin));
    }

    /// <summary>How far past the last ring the skirt reaches, in widths of that ring.</summary>
    public const float SkirtReach = 8f;

    public OceanFeature Ocean { get; }

    /// <summary>The pass type the water draws through.</summary>
    public static readonly PassType Pass = new("Ocean");

    /// <summary>Draws the water after the opaques and the scene copies, before the atmosphere, so clouds and fog see it.</summary>
    public RenderFeature DrawFeature { get; } = new DrawPassFeature(Pass, RenderSlot.AfterOpaques, runsOn: ViewKinds.Cameras | ViewKinds.Captures);

    /// <summary>The material the surface is drawn with, Graphite/Ocean. Its colours and roughness are the water's look.</summary>
    public Material Material { get; }

    /// <summary>How many rings of tiles, 2 to 12.</summary>
    public int Rings { get; set; } = 7;

    /// <summary>The smallest a tile of the innermost ring gets in metres, near the water. A power of two.</summary>
    public float MinScale { get; set; } = 8f;

    /// <summary>The largest a tile of the innermost ring gets in metres, high above the water. A power of two.</summary>
    public float MaxScale { get; set; } = 256f;

    /// <summary>The highest the waves reach above sea level, in metres, which makes the rings grow sooner.</summary>
    public float MaxVerticalDisplacement { get; set; } = 10f;

    /// <summary>Where the rings centre, null for each view's camera. The water's depth stays centred on <see cref="OceanFeature.Viewer"/>.</summary>
    public Double3? Viewer { get; set; }



    /// <summary>The water depth in metres shoreline foam reaches out to.</summary>
    public float ShorelineFoamDepth { get; set; } = 0.65f;

    /// <summary>How thick shoreline foam gets.</summary>
    public float ShorelineFoamStrength { get; set; } = 2f;

    /// <summary>How thick the water behind a pixel must be for its foam to show in full, so foam thins out where the water meets geometry.</summary>
    public float ShorelineFoamMinDepth { get; set; } = 0.27f;

    /// <summary>How much of red, green and blue light the water absorbs per metre, which the view through it and the underwater fog take.</summary>
    public Float4 Absorption { get; set; } = new(0.3f, 0.22f, 0.3f, 1f);

    /// <summary>
    /// Scales <see cref="Absorption"/> for the light coming down through the water to what lies in it, which loses that
    /// much per metre on its way from the sun, so deep floors are darker.
    /// </summary>
    public float DepthLightScale { get; set; } = 0.3f;

    /// <summary>The colour the water scatters the sun and sky back in, above and below the surface.</summary>
    public Float4 ScatterColor { get; set; } = new(0.02f, 0.09f, 0.12f, 1f);

    /// <summary>
    /// Caustics: how far the texture spans in metres, its grey point, strength, the depth the pattern is sharpest
    /// at and the depth over which it blurs a mip away, both in metres.
    /// </summary>
    public float CausticsScale { get; set; } = 5f;
    public float CausticsGreyPoint { get; set; } = 0.07f;
    public float CausticsStrength { get; set; } = 3.2f;
    public float CausticsFocalDepth { get; set; } = 2f;
    public float CausticsDepthOfField { get; set; } = 0.33f;

    /// <summary>How far the wave normal map pushes the caustics about, in texture widths, and the metres it spans.</summary>
    public float CausticsDistortion { get; set; } = 0.1f;
    public float CausticsDistortionScale { get; set; } = 35f;

    /// <summary>The distance each cascade has faded out by, in metres, the largest first.</summary>
    public Float4 CascadeFade { get; set; } = new(1000000f, 6000f, 1500f, 400f);

    public override void Extract()
    {
        _rings = Math.Clamp(Rings, 2, 12);
        _minScale = MathF.Max(1f, MathF.Pow(2f, MathF.Round(MathF.Log2(MathF.Max(MinScale, 1f)))));
        _maxScale = MathF.Max(_minScale, MathF.Pow(2f, MathF.Round(MathF.Log2(MathF.Max(MaxScale, 1f)))));
        _maxVertical = MathF.Max(MaxVerticalDisplacement, 0f);
        _seaLevel = Ocean.SeaLevel;
        _viewer = Viewer;
        _depthViewer = Ocean.Viewer;
        for (int i = 0; i < OceanFeature.Cascades; i++)
            _sizes[i] = Ocean.CascadeSize(i);
        Material.SetFloat4("_OceanSizes", new Float4(_sizes[0], _sizes[1], _sizes[2], _sizes[3]));
        Material.SetFloat4("_OceanFade", CascadeFade);
        Material.SetColor("_Absorption", Absorption);
        Material.SetColor("_ScatterColor", ScatterColor);
        Material.SetFloat4("_OceanLightAbsorption", Absorption * MathF.Max(DepthLightScale, 0f));
        Material.SetFloat4("_OceanWhitecapRule", new Float4(Ocean.FoamCoverage, MathF.Max(Ocean.FoamStrength, 0f), MathF.Max(Ocean.FoamFade, 0f), 0f));
        SetCaustics((name, value) => Material.SetFloat(name, value));
        Material.SetFloat("_PlanetRadius", MathF.Max(Ocean.PlanetRadius, 0f));
        Material.SetFloat4("_OceanDepthParams", Ocean.DepthParams);
        Material.SetFloat4("_OceanRippleParams", new Float4(3f, 0.3f, OceanFeature.RippleSlices, 0f));
        Material.SetFloat("_ShorelineFoamMinDepth", ShorelineFoamMinDepth);
        Material.SetFloat4("_OceanShore", new Float4(Math.Clamp(Ocean.ShallowAttenuation, 0f, 1f), MathF.Max(ShorelineFoamDepth, 1e-3f), MathF.Max(ShorelineFoamStrength, 0f), OceanFeature.DepthSlices));
    }

    public override void Cull(in ProviderView view, DrawItemSink sink)
    {
        Double3 viewer = _viewer ?? view.Camera;
        float level = Math.Clamp((float)Math.Abs(viewer.Y - _seaLevel) + 0.2f * _maxVertical - 4f, _minScale, 1.99f * _maxScale);
        float steps = MathF.Log2(level);
        float scale = MathF.Pow(2f, MathF.Floor(steps));
        float altitude = steps - MathF.Floor(steps);
        float blackPoint = 0.4f / (_quads / 8f);
        double largest = _sizes[0];

        for (int ring = 0; ring < _rings; ring++)
        {
            double size = scale * Math.Pow(2, ring);
            double snap = 2.0 * size / _quads;
            Double3 centre = new(Math.Floor(viewer.X / snap) * snap, _seaLevel, Math.Floor(viewer.Z / snap) * snap);
            float reach = (float)size * 2f * (ring == _rings - 1 ? SkirtReach : 1f);
            if (!view.Sees(centre, reach * 1.42f + _maxVertical))
                continue;

            DrawItem item = new()
            {
                Transform = Double4x4.CreateTranslation(centre) * Double4x4.CreateScale(size, 1, size),
                Flags = RenderableFlags.ReceiveShadows,
                Custom0 = new Float4((float)(centre.X - viewer.X), (float)(centre.Z - viewer.Z), Phase(centre.X, snap), Phase(centre.Z, snap)),
                Custom1 = new Float4((float)Wrap(centre.X, largest), (float)Wrap(centre.Z, largest), (float)size, _quads),
                Color = new Float4(ring == 0 ? altitude : 0f, blackPoint, (float)(viewer.X - _depthViewer.X), (float)(viewer.Z - _depthViewer.Z)),
            };
            sink.Add(ring == 0 ? _centre : ring == _rings - 1 ? _outer : _ring, 0, Material, item);
        }
    }

    // Where a snapped centre sits on the lattice twice its snap, 0 or a half
    private static float Phase(double centre, double snap) => (float)(Wrap(centre, 2.0 * snap) / (2.0 * snap));

    private static double Wrap(double value, double period) => value - Math.Floor(value / period) * period;

    // Takes the foam texture's broad light and dark bands out of its red channel, a wrapping box blur subtracted,
    // so only its small detail tiles
    private static void FlattenFoam(byte[] pixels, int size)
    {
        const int Radius = 48;
        float[] red = new float[size * size], across = new float[size * size];
        double mean = 0;
        for (int i = 0; i < red.Length; i++)
            mean += red[i] = pixels[i * 4];
        mean /= red.Length;

        for (int y = 0; y < size; y++)
        {
            float sum = 0f;
            for (int k = -Radius; k <= Radius; k++)
                sum += red[y * size + (k + size) % size];
            for (int x = 0; x < size; x++)
            {
                across[y * size + x] = sum;
                sum += red[y * size + (x + Radius + 1) % size] - red[y * size + (x - Radius + size) % size];
            }
        }
        float scale = 1f / ((2 * Radius + 1) * (2 * Radius + 1));
        for (int x = 0; x < size; x++)
        {
            float sum = 0f;
            for (int k = -Radius; k <= Radius; k++)
                sum += across[(k + size) % size * size + x];
            for (int y = 0; y < size; y++)
            {
                int i = y * size + x;
                pixels[i * 4] = (byte)Math.Clamp(red[i] - sum * scale + mean, 0.0, 255.0);
                sum += across[(y + Radius + 1) % size * size + x] - across[(y - Radius + size) % size * size + x];
            }
        }
    }

    // A ring's grid from -2 to 2 tiles, quads per tile along each side. With a hole, the middle 2 by 2 tiles are left out
    // but for one row of quads around their edge, which covers the gap to the ring inside as each snaps to its own grid.
    // A skirt adds a row outside whose outer edge reaches that many times the ring's width.
    private static MeshData Grid(int quads, bool hole, float skirt, Float3 margin)
    {
        int extra = skirt > 0f ? 1 : 0;
        int cells = 4 * quads + 2 * extra;
        int side = cells + 1;
        float step = 1f / quads;
        float start = -2f - extra * step;

        Float3[] positions = new Float3[side * side];
        for (int j = 0; j < side; j++)
        {
            for (int i = 0; i < side; i++)
            {
                float x = start + i * step, z = start + j * step;
                if (extra > 0 && (i == 0 || i == side - 1))
                    x = MathF.Sign(x) * 2f * skirt;
                if (extra > 0 && (j == 0 || j == side - 1))
                    z = MathF.Sign(z) * 2f * skirt;
                positions[j * side + i] = new Float3(x, 0f, z);
            }
        }

        float inner = 1f - step;
        List<uint> indices = new();
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                float x0 = start + i * step, z0 = start + j * step;
                if (hole && x0 >= -inner - 1e-4f && x0 + step <= inner + 1e-4f && z0 >= -inner - 1e-4f && z0 + step <= inner + 1e-4f)
                    continue;
                uint a = (uint)(j * side + i), b = a + 1, c = a + (uint)side, d = c + 1;
                if ((((i + extra) ^ (j + extra)) & 1) == 0)
                    indices.AddRange([a, c, d, a, d, b]);
                else
                    indices.AddRange([a, c, b, b, c, d]);
            }
        }

        return new MeshData
        {
            VertexCount = (uint)positions.Length,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = indices.ToArray(),
            Displacement = margin,
        };
    }

    public void Dispose()
    {
        _centre.Dispose();
        _ring.Dispose();
        _outer.Dispose();
        _foamTexture.Dispose();
        _caustics.Dispose();
        _normals.Dispose();
        Material.Dispose();
    }
}
