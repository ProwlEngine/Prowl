using System;
using System.Linq;
using System.Numerics;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class OceanTests : IDisposable
{
    private const int Size = 16;
    private const double Time = 3.25;

    private readonly RendererHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void WaterShader_Compiles()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        Shader water = _harness.Renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Ocean/Water.shader")!));
        foreach (ShaderPass pass in water.Definition.Passes!)
        {
            foreach (string tier in (string[])["FULL", "LITE"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }

    // Seen straight down over a white clear the deep water covers the view, its body hiding what is behind it
    [Fact]
    public void Surface_CoversTheViewFromAbove()
    {
        OceanFeature ocean = new(_harness.Renderer, Size);
        OceanSurface surface = new(_harness.Renderer, ocean, quadsPerTile: 8);
        _harness.Owned.Add(surface);
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        _harness.World.Providers.Add(surface);
        _harness.World.Features.Add(surface.DrawFeature);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(3.5, 20, -7.25) });
        for (int frame = 0; frame < 2; frame++)
        {
            Float4[] pixels = _harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = Time + frame } });
            Assert.All(pixels, p => Assert.True(p.X < 0.5f && float.IsFinite(p.X), $"{p}"));
        }

        surface.Enabled = false;
        Float4[] clear = _harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = Time } });
        Assert.All(clear, p => Assert.True(p.X > 0.5f, $"{p}"));
    }

    // A box over the water cuts the surface inside it and leaves the rest
    [Fact]
    public void Clip_CutsTheSurfaceInsideABox()
    {
        Double3 under = new(3.5, 0, -7.25);
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = under };
        ocean.ClipInputs.Add(new OceanClipInput { Shape = OceanClipShape.Box, Position = under, Size = new Float3(4f, 10f, 4f) });
        OceanSurface surface = new(_harness.Renderer, ocean, quadsPerTile: 8);
        _harness.Owned.Add(surface);
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean.LocalPass);
        _harness.World.Providers.Add(surface);
        _harness.World.Features.Add(surface.DrawFeature);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = under + new Double3(0, 20, 0) });
        Float4[] pixels = [];
        for (int frame = 0; frame < 2; frame++)
            pixels = _harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = Time + frame } });

        const int Pixels = RendererHarness.Size;
        for (int y = 0; y < Pixels; y++)
        {
            for (int x = 0; x < Pixels; x++)
            {
                Float4 p = pixels[y * Pixels + x];
                bool inside = x >= 6 && x < 10 && y >= 6 && y < 10;
                bool outside = x < 2 || x >= Pixels - 2 || y < 2 || y >= Pixels - 2;
                if (inside)
                    Assert.True(p.X > 0.5f, $"({x}, {y}) {p} inside the box");
                else if (outside)
                    Assert.True(p.X < 0.5f, $"({x}, {y}) {p} outside the box");
            }
        }
    }

    // A height ramp lands in the first slice as depth below sea level, its rows along +z, around an off grid viewer
    [Fact]
    public void Depth_TakesTheHeightFieldAroundTheViewer()
    {
        const int Field = 64;
        float[] heights = new float[Field * Field];
        for (int v = 0; v < Field; v++)
            for (int u = 0; u < Field; u++)
                heights[v * Field + u] = -10f - 0.1f * (u + 0.5f - 32f) - 0.2f * (v + 0.5f - 32f);
        Texture field = _harness.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Field, Field, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        _harness.Device.UpdateTexture<float>(field, heights, TextureRegion.Whole(field, 0, 0));
        _harness.Owned.Add(field);

        Double3 viewer = new(1000.3, 0, -2000.7);
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = viewer, SeaLevel = 2, DepthSliceSize = 64 };
        ocean.DepthInputs.Add(new OceanDepthInput { Heights = field, Min = new Double2(viewer.X - 32, viewer.Z - 32), Size = new Float2(64, 64), Offset = 1 });
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time } });

        const int Resolution = OceanFeature.DepthResolution;
        float[] depth = ReadDepth(ocean, 0);

        double texel = 64.0 / Resolution;
        double shiftX = viewer.X - Math.Floor(viewer.X / texel) * texel, shiftZ = viewer.Z - Math.Floor(viewer.Z / texel) * texel;
        for (int k = 8; k < Resolution - 8; k += 7)
        {
            for (int i = 8; i < Resolution - 8; i += 7)
            {
                double x = (i + 0.5 - Resolution / 2) * texel - shiftX, z = (k + 0.5 - Resolution / 2) * texel - shiftZ;
                double expected = 2 - (1 - 10 - 0.1 * x - 0.2 * z);
                Near((float)expected, depth[k * Resolution + i], 1e-3f);
            }
        }
        Assert.Equal(1000f, ReadDepth(ocean, 1)[0]);
    }

    // A sloped plane seen from above comes back as its heights relative to the capture's centre, rows along +z
    [Fact]
    public void Capture_ReadsHeightsFromAbove()
    {
        const int Resolution = 64;
        static float Height(float x, float z) => 0.1f * x + 0.2f * z;
        Float3[] corners = [new(-40, Height(-40, -40), -40), new(40, Height(40, -40), -40), new(40, Height(40, 40), 40), new(-40, Height(-40, 40), 40)];
        GpuMesh plane = _harness.Renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = System.Runtime.InteropServices.MemoryMarshal.AsBytes(corners.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
        });
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.LoadShader(ShaderParser.Parse(RendererHarness.UnlitSource(false))));
        _harness.World.Add(new RenderableDesc { Mesh = plane, Transform = Double4x4.CreateTranslation(new Double3(500, 3, -200)) }, [material]);

        OceanDepthCapture capture = new(_harness.Renderer, _harness.World, Resolution);
        _harness.Owned.Add(capture);
        capture.Aim(new Double3(500, 1, -200), 64f);
        _harness.Submit(new FrameDesc { Views = new[] { capture.View }, Time = new FrameTime { Time = Time } });
        Assert.Empty(_harness.Errors);

        Texture heights = capture.Input.Heights!;
        DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(Resolution * Resolution * 4, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyTextureToBuffer(heights, staging, 0, TextureRegion.Whole(heights, 0, 0))).Wait();
        float[] texels = _harness.Device.Map<float>(staging).ToArray();
        _harness.Device.Unmap(staging);
        staging.Dispose();

        for (int v = 0; v < Resolution; v += 5)
        {
            for (int u = 0; u < Resolution; u += 5)
            {
                float x = -32 + u + 0.5f, z = -32 + v + 0.5f;
                Near(2 + Height(x, z), texels[v * Resolution + u], 0.05f);
            }
        }
        Assert.Equal(1f, capture.Input.Offset);
        Assert.Equal(new Double2(468, -232), capture.Input.Min);
    }

    // Calm water answers its sea level less the planet's drop, facing up, with the depth under each point
    [Fact]
    public void Queries_AnswerTheWaterAtTheirPoints()
    {
        float[] heights = [-7f, -7f, -7f, -7f];
        Texture field = _harness.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        _harness.Device.UpdateTexture<float>(field, heights, TextureRegion.Whole(field, 0, 0));
        _harness.Owned.Add(field);

        Double3 viewer = new(100, 50, 100);
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = viewer, SeaLevel = 3, PlanetRadius = 10000 };
        ocean.Spectrum.Multiplier = 0f;
        ocean.DepthInputs.Add(new OceanDepthInput { Heights = field, Min = new Double2(90, 90), Size = new Float2(20, 20) });
        OceanQueries queries = new(_harness.Renderer, ocean);
        OceanQuery query = new(2);
        query.Points[0] = new Double2(100, 100);
        query.Points[1] = new Double2(300, 100);
        queries.Queries.Add(query);
        _harness.Owned.Add(queries);
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        foreach (FrameFeature feature in queries.Features)
            _harness.World.FrameFeatures.Add(feature);

        for (int frame = 0; frame < 3; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame } });

        Assert.True(query.Ready);
        Assert.Equal(3.0, query.Heights[0], 3);
        Assert.Equal(3.0 - 200.0 * 200.0 / 20000.0, query.Heights[1], 3);
        Assert.Equal(10f, query.Depths[0], 3);
        Assert.True(query.Depths[1] >= 999f);
        Assert.Equal(1f, query.Normals[0].Y, 3);
    }

    // Under calm water the view fogs toward the unlit scatter colour, black here, and above it the view is left alone
    [Fact]
    public void Underwater_FogsTheViewBelowTheSea()
    {
        OceanFeature ocean = new(_harness.Renderer, Size) { SeaLevel = 0 };
        ocean.Spectrum.Multiplier = 0f;
        OceanSurface surface = new(_harness.Renderer, ocean, quadsPerTile: 8);
        OceanUnderwater underwater = new(_harness.Renderer, surface);
        _harness.Owned.Add(underwater);
        _harness.Owned.Add(surface);
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        _harness.World.Providers.Add(surface);
        _harness.World.Features.Add(surface.DrawFeature);
        _harness.World.Features.Add(underwater);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = new Double3(0, -6, 0) });
        Float4[] below = _harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = Time } });
        Assert.All(below, p => Assert.True(p.X < 0.1f, $"{p}"));

        view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = new Double3(0, 30, 0) });
        Float4[] above = _harness.Frame(new FrameDesc { Views = new[] { view }, Time = new FrameTime { Time = Time } });
        Assert.All(above, p => Assert.True(p.X > 0.9f, $"{p}"));
    }

    // Whitecaps build where the waves fold and linger, fading, once nothing builds them
    [Fact]
    public void Whitecaps_BuildAndLinger()
    {
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = new Double3(10, 5, 10), FoamCoverage = 2f };
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean.LocalPass);
        for (int frame = 0; frame < 10; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame / 60.0, DeltaTime = 1f / 60f } });
        float built = Centre(ocean);
        Assert.True(built > 0.1f, $"built {built}");

        ocean.FoamCoverage = -1f;
        for (int frame = 10; frame < 15; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame / 60.0, DeltaTime = 1f / 60f } });
        float lingering = Centre(ocean);
        Assert.True(lingering > 0.05f && lingering < built, $"built {built}, lingering {lingering}");

        float Centre(OceanFeature target)
        {
            const int Resolution = OceanFeature.DepthResolution;
            DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(Resolution * Resolution * 2, BufferUsage.Staging));
            _harness.Device.Record(cmd => cmd.CopyTextureToBuffer(target.Whitecaps, staging, 0, TextureRegion.Whole(target.Whitecaps, 0, 0))).Wait();
            Half[] texels = _harness.Device.Map<Half>(staging).ToArray();
            _harness.Device.Unmap(staging);
            staging.Dispose();
            return (float)texels[Resolution / 2 * Resolution + Resolution / 2];
        }
    }

    // A sphere plunging through calm water raises finite ripples around it, which have not reached the slice's corner
    [Fact]
    public void Ripples_SpreadFromAnInteractor()
    {
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = new Double3(10, 5, 10) };
        ocean.Spectrum.Multiplier = 0f;
        ocean.Interactors.Add(new OceanInteractor { Position = new Double3(10, 0, 10), Radius = 1, Velocity = new Float3(0, -3, 0) });
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        for (int frame = 0; frame < 20; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame / 60.0, DeltaTime = 1f / 60f } });

        const int Resolution = OceanFeature.DepthResolution;
        DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(Resolution * Resolution * 8, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyTextureToBuffer(ocean.Ripples, staging, 0, TextureRegion.Whole(ocean.Ripples, 0, 0))).Wait();
        Half[] texels = _harness.Device.Map<Half>(staging).ToArray();
        _harness.Device.Unmap(staging);
        staging.Dispose();

        float most = 0f;
        for (int t = 0; t < Resolution * Resolution; t++)
        {
            float height = (float)texels[t * 4];
            Assert.True(float.IsFinite(height));
            most = MathF.Max(most, MathF.Abs(height));
        }
        Assert.True(most > 1e-3f, $"tallest ripple {most}");
        Assert.Equal(0f, (float)texels[0]);
    }

    // With time stopped, moving the viewer leaves every ripple where it was in the world
    [Fact]
    public void Ripples_StayInTheWorldAsTheViewerMoves()
    {
        Double3 first = new(10.1, 5, 10.2), second = new(15.4, 5, 7.1);
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = first };
        ocean.Spectrum.Multiplier = 0f;
        ocean.Interactors.Add(new OceanInteractor { Position = new Double3(12, 0, 9), Radius = 1, Velocity = new Float3(0, -3, 0) });
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        for (int frame = 0; frame < 20; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame / 60.0, DeltaTime = 1f / 60f } });
        ocean.Interactors.Clear();
        _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + 1, DeltaTime = 0f } });
        float[] before = ReadRippleHeights(ocean);
        ocean.Viewer = second;
        _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + 1, DeltaTime = 0f } });
        float[] after = ReadRippleHeights(ocean);

        const int Resolution = OceanFeature.DepthResolution;
        double texel = ocean.DepthSliceSize / Resolution;
        int Corner(double v) => (int)Math.Floor(v / texel);
        int shiftX = Corner(second.X) - Corner(first.X), shiftZ = Corner(second.Z) - Corner(first.Z);
        float most = 0f;
        for (int z = 40; z < Resolution - 40; z++)
        {
            for (int x = 40; x < Resolution - 40; x++)
            {
                float was = before[z * Resolution + x], now = after[(z - shiftZ) * Resolution + x - shiftX];
                most = MathF.Max(most, MathF.Abs(was));
                Near(was, now, 1e-3f + MathF.Abs(was) * 0.01f);
            }
        }
        Assert.True(most > 1e-3f);
    }

    // A current carries the ripples downstream of the interactor that makes them
    [Fact]
    public void Flow_CarriesRipplesDownstream()
    {
        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = new Double3(10, 5, 10) };
        ocean.Spectrum.Multiplier = 0f;
        ocean.Interactors.Add(new OceanInteractor { Position = new Double3(10, 0, 10), Radius = 1, Velocity = new Float3(0, -3, 0) });
        ocean.FlowInputs.Add(new OceanFlowInput { Min = new Double2(-100, -100), Size = new Float2(200, 200), Velocity = new Float2(3, 0) });
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        for (int frame = 0; frame < 30; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame / 60.0, DeltaTime = 1f / 60f } });

        const int Resolution = OceanFeature.DepthResolution;
        float[] heights = ReadRippleHeights(ocean);
        double sum = 0, x = 0, z = 0;
        for (int row = 0; row < Resolution; row++)
        {
            for (int column = 0; column < Resolution; column++)
            {
                float weight = MathF.Abs(heights[row * Resolution + column]);
                sum += weight;
                x += weight * column;
                z += weight * row;
            }
        }
        double texel = ocean.DepthSliceSize / Resolution;
        double centre = Resolution / 2.0;
        Assert.True(sum > 1e-3);
        Assert.True((x / sum - centre) * texel > 0.5, $"ripples centred {(x / sum - centre) * texel} m downstream");
        Assert.True(Math.Abs(z / sum - centre) * texel < 0.25, $"ripples centred {(z / sum - centre) * texel} m across the current");
    }

    private float[] ReadRippleHeights(OceanFeature ocean)
    {
        const int Resolution = OceanFeature.DepthResolution;
        DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(Resolution * Resolution * 8, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyTextureToBuffer(ocean.Ripples, staging, 0, TextureRegion.Whole(ocean.Ripples, 0, 0))).Wait();
        Half[] texels = _harness.Device.Map<Half>(staging).ToArray();
        _harness.Device.Unmap(staging);
        staging.Dispose();
        float[] heights = new float[Resolution * Resolution];
        for (int t = 0; t < heights.Length; t++)
            heights[t] = (float)texels[t * 4];
        return heights;
    }

    // Inside a flat lake the water stands at the lake's height, its depth counted from there, and outside at sea level
    [Fact]
    public void Levels_RaiseALake()
    {
        float[] heights = [-7f, -7f, -7f, -7f];
        Texture field = _harness.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        _harness.Device.UpdateTexture<float>(field, heights, TextureRegion.Whole(field, 0, 0));
        _harness.Owned.Add(field);

        OceanFeature ocean = new(_harness.Renderer, Size) { Viewer = new Double3(100, 50, 100), SeaLevel = 3, PlanetRadius = 0 };
        ocean.Spectrum.Multiplier = 0f;
        ocean.DepthInputs.Add(new OceanDepthInput { Heights = field, Min = new Double2(90, 90), Size = new Float2(20, 20) });
        ocean.LevelInputs.Add(new OceanLevelInput { Min = new Double2(95, 95), Size = new Float2(10, 10), Offset = 8 });
        OceanQueries queries = new(_harness.Renderer, ocean);
        OceanQuery query = new(2);
        query.Points[0] = new Double2(100, 100);
        query.Points[1] = new Double2(300, 100);
        queries.Queries.Add(query);
        _harness.Owned.Add(queries);
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        foreach (FrameFeature feature in queries.Features)
            _harness.World.FrameFeatures.Add(feature);
        for (int frame = 0; frame < 3; frame++)
            _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time + frame } });

        Assert.Equal(8.0, query.Heights[0], 3);
        Assert.Equal(15f, query.Depths[0], 3);
        Assert.Equal(3.0, query.Heights[1], 3);
    }

    private float[] ReadDepth(OceanFeature ocean, uint slice)
    {
        const int Resolution = OceanFeature.DepthResolution;
        DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(Resolution * Resolution * 4, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyTextureToBuffer(ocean.Depth, staging, 0, TextureRegion.Whole(ocean.Depth, 0, slice))).Wait();
        float[] depth = _harness.Device.Map<float>(staging).ToArray();
        _harness.Device.Unmap(staging);
        staging.Dispose();
        return depth;
    }

    // The fields the transform writes match a direct inverse DFT of the spectra built from the starting amplitudes
    [Fact]
    public void Fields_MatchADirectTransformOfTheSpectrum()
    {
        OceanFeature ocean = new(_harness.Renderer, Size) { WindSpeed = 20f, TileSize = 256f };
        _harness.Owned.Add(ocean);
        _harness.World.FrameFeatures.Add(ocean);
        _harness.Frame(new FrameDesc { Views = new[] { _harness.View() }, Time = new FrameTime { Time = Time } });

        float chop = ocean.Spectrum.Chop;
        for (int cascade = 0; cascade < OceanFeature.Cascades; cascade++)
        {
            Float4[] initial = Read(ocean.Initial, cascade, half: false);
            Float4[] displacement = Read(ocean.Displacement, cascade, half: true);
            Float4[] slopes = Read(ocean.Slopes, cascade, half: true);
            Assert.Contains(initial, h => h != Float4.Zero);

            Complex[][] spectra = Spectra(initial, ocean.CascadeSize(cascade), ocean.Gravity, ocean.LoopPeriod, chop);
            float scale = 1e-3f + spectra.Max(s => s.Sum(c => (float)c.Magnitude)) * 2e-3f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Complex[] f = spectra.Select(s => Transform(s, x, y)).ToArray();
                    Float4 d = displacement[y * Size + x], s = slopes[y * Size + x];
                    Near((float)f[0].Real, d.X, scale);
                    Near((float)f[0].Imaginary, d.Y, scale);
                    Near((float)f[1].Real, d.Z, scale);
                    Near((float)f[1].Imaginary, d.W, scale);
                    Near((float)f[2].Real, s.X, scale);
                    Near((float)f[2].Imaginary, s.Y, scale);
                    Near((float)f[3].Real, s.Z, scale);
                    Near((float)f[3].Imaginary, s.W, scale);
                }
            }
        }
    }

    private static void Near(float expected, float actual, float tolerance)
        => Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    // The four packed spectra OceanUpdate.glsl builds at the test's time, by texel
    private static Complex[][] Spectra(Float4[] initial, float tile, float gravity, float period, float chop)
    {
        Complex[][] spectra = [new Complex[Size * Size], new Complex[Size * Size], new Complex[Size * Size], new Complex[Size * Size]];
        Complex i = Complex.ImaginaryOne;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int mx = x < Size / 2 ? x : x - Size, my = y < Size / 2 ? y : y - Size;
                double kx = 2 * Math.PI * mx / tile, kz = 2 * Math.PI * my / tile;
                double k = Math.Max(Math.Sqrt(kx * kx + kz * kz), 1e-6);
                double w = Math.Sqrt(gravity * k);
                w = 2 * Math.PI * Math.Ceiling(period * w / (2 * Math.PI)) / period;
                Float4 h0 = initial[y * Size + x];
                Complex turn = Complex.FromPolarCoordinates(1, w * Time);
                Complex h = new Complex(h0.X, h0.Y) * turn + new Complex(h0.Z, h0.W) * Complex.Conjugate(turn);
                int at = y * Size + x;
                spectra[0][at] = i * h * (chop * kx / k) + i * h;
                spectra[1][at] = i * h * (chop * kz / k) - i * (h * (chop * kx * kz / k));
                spectra[2][at] = i * h * kx + i * (i * h * kz);
                spectra[3][at] = -h * (chop * kx * kx / k) - i * (h * (chop * kz * kz / k));
            }
        }
        return spectra;
    }

    private static Complex Transform(Complex[] spectrum, int x, int y)
    {
        Complex sum = Complex.Zero;
        for (int v = 0; v < Size; v++)
        {
            for (int u = 0; u < Size; u++)
                sum += spectrum[v * Size + u] * Complex.FromPolarCoordinates(1, 2 * Math.PI * (u * x + v * y) / Size);
        }
        return sum;
    }

    private Float4[] Read(Texture texture, int layer, bool half)
    {
        uint bytes = Size * Size * (half ? 8u : 16u);
        DeviceBuffer staging = _harness.Device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
        _harness.Device.Record(cmd => cmd.CopyTextureToBuffer(texture, staging, 0, TextureRegion.Whole(texture, 0, (uint)layer))).Wait();
        Float4[] texels = new Float4[Size * Size];
        if (half)
        {
            Half[] values = _harness.Device.Map<Half>(staging).ToArray();
            for (int t = 0; t < texels.Length; t++)
                texels[t] = new Float4((float)values[t * 4], (float)values[t * 4 + 1], (float)values[t * 4 + 2], (float)values[t * 4 + 3]);
        }
        else
        {
            _harness.Device.Map<Float4>(staging).ToArray().CopyTo(texels, 0);
        }
        _harness.Device.Unmap(staging);
        staging.Dispose();
        return texels;
    }
}
