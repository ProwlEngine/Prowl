using System;
using System.IO;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class TerrainSurfaceTests : IDisposable
{
    private readonly RendererHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // A flat package of the given quads a side, every sample at the middle of a 0 to 100 m range, or the finest level at fine
    private static TerrainData Flat(int quads, ushort fine = 32768, Func<int, int, bool>? hole = null)
    {
        TerrainHeader header = new() { Spacing = 1, Bottom = 0, Range = 100, QuadsX = quads, QuadsZ = quads, Levels = TerrainHeader.LevelsFor(quads, quads), ControlLevels = 1 };
        MemoryStream stream = new();
        TerrainPackage.Writer writer = TerrainPackage.Begin(stream, header);
        for (int level = 0; level < header.Levels; level++)
            for (int z = 0; z < header.PagesZ(level); z++)
                for (int x = 0; x < header.PagesX(level); x++)
                {
                    ushort[] heights = new ushort[TerrainPage.Samples];
                    Array.Fill(heights, level == 0 ? fine : (ushort)32768);
                    byte[] normals = new byte[TerrainPage.Samples * 4];
                    for (int i = 0; i < TerrainPage.Samples; i++)
                        (normals[i * 4], normals[i * 4 + 1], normals[i * 4 + 2], normals[i * 4 + 3]) = (128, 128, 255, 128);
                    uint[]? control = null;
                    if (level == 0 && hole != null)
                    {
                        control = new uint[TerrainPage.Samples];
                        for (int j = -1; j <= TerrainPage.Quads + 1; j++)
                            for (int i = -1; i <= TerrainPage.Quads + 1; i++)
                                if (hole(x * TerrainPage.Quads + i, z * TerrainPage.Quads + j))
                                    control[TerrainPage.Index(i, j)] = TerrainControl.Hole;
                    }
                    writer.Add(new TerrainPage(new TerrainPageKey(level, x, z), heights, normals, control), [32768, 32768, 32768, 32768, 32768, 32768, 32768, 32768]);
                }
        writer.Finish();
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    [Fact]
    public void TerrainModel_Compiles()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Terrain/Default.shader")!));
        foreach (ShaderPass pass in shader.Definition.Passes!)
        {
            foreach (string tier in (string[])["FULL", "LITE"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }

    // Seen straight down over a white clear the ground covers the view, and nothing once the surface is off
    [Fact]
    public void Surface_CoversTheViewFromAbove()
    {
        using TerrainData data = Flat(600);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 80, 220.25) });
        Float4[] pixels = _harness.Frame(view);
        Assert.All(pixels, p => Assert.True(p.X < 0.9f && float.IsFinite(p.X), $"{p}"));
        Assert.True(surface.Drawn > 0);

        surface.Enabled = false;
        Float4[] clear = _harness.Frame(view);
        Assert.All(clear, p => Assert.True(p.X > 0.99f, $"{p}"));
    }

    // The ground stands at its page height of 50 m: looking down from 60 m shows it, from 40 m under it shows nothing
    [Fact]
    public void Surface_StandsAtThePageHeight()
    {
        using TerrainData data = Flat(600);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 60, 220.25) });
        Assert.All(_harness.Frame(view), p => Assert.True(p.X < 0.9f, $"{p}"));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 40, 220.25) });
        Assert.All(_harness.Frame(view), p => Assert.True(p.X > 0.99f, $"{p}"));
    }

    // Renders until the finest level has pages in, as they load on worker threads
    private void FrameUntilFine(TerrainSurface surface, RenderView view)
    {
        for (int frame = 0; frame < 200; frame++)
        {
            _harness.Frame(view);
            if (surface.Streaming.ResidentByLevel[0] > 0 && surface.Streaming.Loading == 0)
                return;
            System.Threading.Thread.Sleep(5);
        }
    }

    // Only the top levels start in; the finest pages around the camera stream in, and none far from it
    [Fact]
    public void Streaming_LoadsFinePagesAroundTheCamera()
    {
        using TerrainData data = Flat(4096);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        Assert.Equal(0, surface.Streaming.ResidentByLevel[0]);

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(1000, 60, 3000) });
        FrameUntilFine(surface, view);

        int fine = surface.Streaming.ResidentByLevel[0];
        Assert.InRange(fine, 1, 16);
        Assert.True(surface.Streaming.Resident <= surface.PoolPages);
    }

    // With a pool too small for the world, moving the camera evicts the pages left behind for the new ones
    [Fact]
    public void Streaming_EvictsPagesLeftBehind()
    {
        using TerrainData data = Flat(4096);
        TerrainSurface surface = new(_harness.Renderer, data, poolPages: 64);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        foreach (Double3 at in (Double3[])[new(500, 60, 500), new(3500, 60, 3500), new(500, 60, 3500)])
        {
            view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = at });
            FrameUntilFine(surface, view);
            Assert.True(surface.Streaming.ResidentByLevel[0] > 0);
            Assert.True(surface.Streaming.Resident <= surface.PoolPages);
            Assert.All(_harness.Frame(view), p => Assert.True(p.X < 0.9f, $"{p}"));
        }
    }

    // Streamed finest pages reach the GPU: from 55 m the coarse ground at 50 m shows, and once the finest pages at 60 m
    // stream in there is nothing under the camera, where empty slices would draw ground at 0 m
    [Fact]
    public void Streaming_UploadsThePagesItLoads()
    {
        using TerrainData data = Flat(4096, fine: (ushort)(65535 * 60 / 100));
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(1000.5, 55, 3000.25) });
        Assert.All(_harness.Frame(view), p => Assert.True(p.X < 0.9f, $"{p}"));
        FrameUntilFine(surface, view);
        Assert.All(_harness.Frame(view), p => Assert.True(p.X > 0.99f, $"{p}"));
    }

    // A checkered layer, light and dark squares of 8 texels, flat and rough
    private TerrainLayers Checker()
    {
        const int size = 64;
        byte[] albedo = new byte[size * size * 4], surface = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                byte tone = ((x / 8) + (y / 8)) % 2 == 0 ? (byte)220 : (byte)60;
                (albedo[i], albedo[i + 1], albedo[i + 2], albedo[i + 3]) = (tone, (byte)(tone / 2), 40, 128);
                (surface[i], surface[i + 1], surface[i + 2], surface[i + 3]) = (128, 128, 230, 255);
            }
        TerrainLayers layers = new(_harness.Renderer, [new TerrainLayerDesc { Size = size, Albedo = albedo, Surface = surface, TileSize = 4f }]);
        _harness.Owned.Add(layers);
        return layers;
    }

    // The ground drawn from the material cache looks as it does with every pixel blending the layers itself
    [Fact]
    public void Cache_MatchesTheDirectPath()
    {
        using TerrainData data = Flat(600);
        TerrainSurface surface = new(_harness.Renderer, data) { Layers = Checker(), CacheBudget = 1 << 24 };
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        _harness.World.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(1, 1, 1, 1), Fog = default };

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 60, 220.25) });
        Float4[] cached = [];
        for (int frame = 0; frame < 4; frame++)
            cached = _harness.Frame(view);
        surface.UseCache = false;
        Float4[] direct = _harness.Frame(view);

        double difference = 0, brightness = 0;
        for (int i = 0; i < direct.Length; i++)
        {
            difference += Math.Abs(cached[i].X - direct[i].X) + Math.Abs(cached[i].Y - direct[i].Y);
            brightness += direct[i].X + direct[i].Y;
        }
        Assert.True(brightness / direct.Length > 0.05, $"brightness {brightness / direct.Length}");
        Assert.True(difference / brightness < 0.1, $"difference {difference / brightness}");
    }

    // Two views 400 m apart, side by side in one target, each keep their own cache and both match the direct path
    [Fact]
    public void Cache_KeepsOneForEachView()
    {
        using TerrainData data = Flat(1000);
        TerrainSurface surface = new(_harness.Renderer, data) { Layers = Checker(), CacheBudget = 1 << 25 };
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        _harness.World.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(1, 1, 1, 1), Fog = default };

        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        RenderView left = _harness.View(1), right = _harness.View(2);
        left.Viewport = new Float4(0, 0, 0.5f, 1);
        right.Viewport = new Float4(0.5f, 0, 0.5f, 1);
        left.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 60, 300.25) });
        right.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(700.25, 60, 700.5) });
        Float4[] cached = [];
        for (int frame = 0; frame < 5; frame++)
            cached = _harness.Frame(left, right);
        surface.UseCache = false;
        Float4[] direct = _harness.Frame(left, right);

        double difference = 0, brightness = 0;
        for (int i = 0; i < direct.Length; i++)
        {
            difference += Math.Abs(cached[i].X - direct[i].X) + Math.Abs(cached[i].Y - direct[i].Y);
            brightness += direct[i].X + direct[i].Y;
        }
        Assert.True(brightness / direct.Length > 0.05, $"brightness {brightness / direct.Length}");
        Assert.True(difference / brightness < 0.1, $"difference {difference / brightness}");
    }

    // A square of holes shows what is behind the ground through it, the ground beside it stays
    [Fact]
    public void Holes_CutTheGround()
    {
        using TerrainData data = Flat(600, hole: (x, z) => x >= 290 && x < 310 && z >= 210 && z < 230);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);

        RenderView view = _harness.View();
        view.ClearColor = new Float4(1, 1, 1, 1);
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300, 60, 220) });
        FrameUntilFine(surface, view);
        Assert.All(_harness.Frame(view), p => Assert.True(p.X > 0.99f, $"{p}"));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(330, 60, 220) });
        Assert.All(_harness.Frame(view), p => Assert.True(p.X < 0.9f, $"{p}"));
    }

    private float Brightness(RenderView view)
    {
        float sum = 0f;
        Float4[] pixels = _harness.Frame(view);
        foreach (Float4 p in pixels)
            sum += p.X + p.Y + p.Z;
        return sum / pixels.Length;
    }

    // A far shadow map the host fills darkens the ground under heights above it, and leaves it lit under heights below
    [Fact]
    public void FarShadow_DarkensWhatLiesBelowItsHeights()
    {
        using TerrainData data = Flat(600);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        _harness.World.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(0, -1, 0), Color = new Float3(1, 1, 1), Intensity = 10f });

        Texture heights = _harness.Device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));
        FarShadowMap far = new(_harness.Renderer) { Heights = heights, Min = new Double2(200, 120), Size = 200, Softness = 1f, Bias = 0f };
        _harness.Owned.Add(far);
        _harness.Owned.Add(heights);
        _harness.World.FarShadow = far;

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 60, 220.25) });

        _harness.Device.UpdateTexture<float>(heights, Enumerable.Repeat(10f, 16).ToArray(), TextureRegion.Whole(heights, 0, 0));
        float lit = Brightness(view);
        _harness.Device.UpdateTexture<float>(heights, Enumerable.Repeat(90f, 16).ToArray(), TextureRegion.Whole(heights, 0, 0));
        float shadowed = Brightness(view);
        Assert.True(lit > 0.05f, $"lit {lit}");
        Assert.True(shadowed < lit * 0.5f, $"shadowed {shadowed} lit {lit}");
    }

    // A package of the given quads a side shaped by a height in metres over 0 to 100 m at each finest sample
    private static TerrainData Shaped(int quads, Func<int, int, double> metres)
    {
        TerrainHeader header = new() { Spacing = 1, Bottom = 0, Range = 100, QuadsX = quads, QuadsZ = quads, Levels = TerrainHeader.LevelsFor(quads, quads), ControlLevels = 1 };
        MemoryStream stream = new();
        TerrainPackage.Writer writer = TerrainPackage.Begin(stream, header);
        for (int level = 0; level < header.Levels; level++)
            for (int z = 0; z < header.PagesZ(level); z++)
                for (int x = 0; x < header.PagesX(level); x++)
                {
                    ushort[] heights = new ushort[TerrainPage.Samples];
                    byte[] normals = new byte[TerrainPage.Samples * 4];
                    ushort low = ushort.MaxValue, high = 0;
                    for (int j = -1; j <= TerrainPage.Quads + 1; j++)
                        for (int i = -1; i <= TerrainPage.Quads + 1; i++)
                        {
                            int sx = Math.Clamp((x * TerrainPage.Quads + i) << level, 0, quads), sz = Math.Clamp((z * TerrainPage.Quads + j) << level, 0, quads);
                            ushort h = (ushort)Math.Round(metres(sx, sz) / 100.0 * 65535);
                            int index = TerrainPage.Index(i, j);
                            heights[index] = h;
                            (normals[index * 4], normals[index * 4 + 1], normals[index * 4 + 2], normals[index * 4 + 3]) = (128, 128, 255, 128);
                            low = Math.Min(low, h);
                            high = Math.Max(high, h);
                        }
                    writer.Add(new TerrainPage(new TerrainPageKey(level, x, z), heights, normals, null), [low, high, low, high, low, high, low, high]);
                }
        writer.Finish();
        stream.Position = 0;
        return TerrainData.Load(stream);
    }

    // With a low sun from -x a 90 m wall shadows the ground behind it through the terrain's own far shadow, and the
    // ground in front of it stays lit
    [Fact]
    public void FarShadow_TerrainShadowsWhatLiesBehindIt()
    {
        using TerrainData data = Shaped(1000, (x, z) => x >= 300 && x < 320 ? 90.0 : 10.0);
        TerrainSurface surface = new(_harness.Renderer, data) { CastFarShadow = true, FarShadowResolution = 256, FarShadowCoverage = 1000 };
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        LightHandle sun = _harness.World.AddLight(new LightDesc { Type = LightType.Directional, Direction = Float3.Normalize(new Float3(1, -0.5f, 0)), Color = new Float3(1, 1, 1), Intensity = 10f });
        _harness.World.Environment = new EnvironmentDesc { AmbientColor = default, Fog = default, Sun = sun };

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(400.5, 60, 500.25) });
        for (int frame = 0; frame < 16; frame++)
            _harness.Frame(view);
        Assert.NotNull(_harness.World.FarShadow);
        float behind = Brightness(view);
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(200.5, 60, 500.25) });
        float front = Brightness(view);
        Assert.True(front > 0.05f, $"front {front}");
        Assert.True(behind < front * 0.5f, $"behind {behind} front {front}");
    }

    [Fact]
    public void GrassShader_Compiles()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Terrain/Grass.shader")!));
        foreach (ShaderPass pass in shader.Definition.Passes!)
        {
            foreach (string tier in (string[])["FULL", "LITE"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }

    // Bright blades grown by the layer cover much of the dark ground seen from above, and none grow without a grass type
    [Fact]
    public void Grass_GrowsWhereItsLayerIs()
    {
        using TerrainData data = Flat(600);
        const int size = 4;
        byte[] dark = new byte[size * size * 4], flat = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            (dark[i * 4], dark[i * 4 + 1], dark[i * 4 + 2], dark[i * 4 + 3]) = (10, 10, 10, 128);
            (flat[i * 4], flat[i * 4 + 1], flat[i * 4 + 2], flat[i * 4 + 3]) = (128, 128, 230, 255);
        }
        TerrainLayers layers = new(_harness.Renderer, [new TerrainLayerDesc { Size = size, Albedo = dark, Surface = flat, Grass = 0, HexTiling = false, MacroVariation = 0 }]);
        _harness.Owned.Add(layers);
        TerrainSurface surface = new(_harness.Renderer, data) { Layers = layers };
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        _harness.World.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(1, 1, 1, 1), Fog = default };

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 55, 220.25) });
        for (int frame = 0; frame < 4; frame++)
            _harness.Frame(view);
        float bare = Brightness(view);

        surface.GrassTypes = [new TerrainGrassType { Density = 400, Range = 20, MinHeight = 0.3f, MaxHeight = 0.5f, Width = 0.06f, RootColor = new Float3(0.6f, 0.9f, 0.3f), TipColor = new Float3(0.6f, 0.9f, 0.3f) }];
        for (int frame = 0; frame < 4; frame++)
            _harness.Frame(view);
        float grown = Brightness(view);
        Assert.True(grown > bare * 3f, $"grown {grown} bare {bare}");
    }

    // Grass cut away by a brush stops growing there on the next frame, and a hole cut at run time shows the clear through it
    [Fact]
    public void ControlEdits_ReachTheGrassAndTheHoles()
    {
        using TerrainData data = Flat(600);
        const int size = 4;
        byte[] dark = new byte[size * size * 4], flat = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            (dark[i * 4], dark[i * 4 + 1], dark[i * 4 + 2], dark[i * 4 + 3]) = (10, 10, 10, 128);
            (flat[i * 4], flat[i * 4 + 1], flat[i * 4 + 2], flat[i * 4 + 3]) = (128, 128, 230, 255);
        }
        TerrainLayers layers = new(_harness.Renderer, [new TerrainLayerDesc { Size = size, Albedo = dark, Surface = flat, Grass = 0, HexTiling = false, MacroVariation = 0 }]);
        _harness.Owned.Add(layers);
        TerrainSurface surface = new(_harness.Renderer, data) { Layers = layers };
        surface.GrassTypes = [new TerrainGrassType { Density = 400, Range = 20, MinHeight = 0.3f, MaxHeight = 0.5f, Width = 0.06f, RootColor = new Float3(0.6f, 0.9f, 0.3f), TipColor = new Float3(0.6f, 0.9f, 0.3f) }];
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        _harness.World.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(1, 1, 1, 1), Fog = default };

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 55, 220.25) });
        for (int frame = 0; frame < 4; frame++)
            _harness.Frame(view);
        float grown = Brightness(view);

        data.Apply(new TerrainEdit(TerrainBrush.Grass, new Double2(300.5, 220.25), 40, 1));
        for (int frame = 0; frame < 2; frame++)
            _harness.Frame(view);
        float cut = Brightness(view);
        Assert.True(cut * 3f < grown, $"cut {cut} grown {grown}");

        view.ClearColor = new Float4(1, 1, 1, 1);
        data.Apply(new TerrainEdit(TerrainBrush.Hole, new Double2(300.5, 220.25), 40, 1));
        _harness.Frame(view);
        float holed = Brightness(view);
        Assert.True(holed > 2.5f, $"holed {holed}");
    }

    // Far from the camera the walk draws coarser nodes, so a large world takes few draws
    [Fact]
    public void Walk_DrawsFewNodesForALargeWorld()
    {
        using TerrainData data = Flat(4096);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(2048, 60, 2048) });
        _harness.Frame(view);
        Assert.InRange(surface.Drawn, 1, 8);
    }

    // Props fill the cells around the viewer, standing on the ground, and leave the world once it walks away
    [Fact]
    public void Props_FollowTheViewer()
    {
        using TerrainData data = Flat(600);
        TerrainScatter scatter = new(data, [new TerrainScatterRule { Id = 1, Density = 0.5f, Footprint = 2f }], seed: 2);
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.Standard.Lit);
        _harness.Owned.Add(material);
        using TerrainProps props = new(_harness.World, scatter, [new TerrainPropKind { Mesh = _harness.CreateQuad(), Materials = [material] }]) { Range = 100, CellsPerUpdate = 100 };

        void Settle(Double3 viewer)
        {
            props.Update(viewer);
            props.Settle().Wait();
            props.Update(viewer);
        }

        int before = _harness.World.Count;
        Settle(new Double3(300, 50, 300));
        Assert.True(props.Cells >= 9, $"{props.Cells}");
        Assert.True(props.Count > 100, $"{props.Count}");
        Assert.Equal(before + props.Count, _harness.World.Count);

        Settle(new Double3(5000, 50, 5000));
        Assert.Equal(0, props.Count);
        Assert.Equal(before, _harness.World.Count);
    }

    // A raise under placed props moves the ones that stay onto the new ground in the same call, keeping their renderables
    [Fact]
    public void Props_FollowAnEdit()
    {
        using TerrainData data = Flat(600);
        TerrainScatter scatter = new(data, [new TerrainScatterRule { Id = 1, Density = 2f, Footprint = 1f }], seed: 2);
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.Standard.Lit);
        _harness.Owned.Add(material);
        using TerrainProps props = new(_harness.World, scatter, [new TerrainPropKind { Mesh = _harness.CreateQuad(), Materials = [material] }]) { Range = 100, CellsPerUpdate = 100 };
        Double3 viewer = new(300, 50, 300);
        props.Update(viewer);
        props.Settle().Wait();
        props.Update(viewer);
        int count = props.Count, total = _harness.World.Count;

        data.Apply(new TerrainEdit(TerrainBrush.Raise, new Double2(300, 300), 20, 10));

        Assert.Equal(total - count + props.Count, _harness.World.Count);
        int moved = 0;
        for (int z = 3; z <= 5; z++)
            for (int x = 3; x <= 5; x++)
                foreach (TerrainInstance instance in scatter.Cell(x, z))
                {
                    Assert.True(data.TryGetHeight(new Double2(instance.Position.X, instance.Position.Z), out double ground));
                    Assert.Equal(ground, instance.Position.Y, 6);
                    Assert.True(props.TryGetHandle(instance.Id, out RenderableHandle handle));
                    Assert.Equal(ground, _harness.World.GetTransform(handle).Translation.Y, 6);
                    moved += ground > 50.5 ? 1 : 0;
                }
        Assert.True(moved > 20, $"{moved}");
    }

    // A crater under the camera changes the drawn ground on the next frame, its pages uploaded again
    [Fact]
    public void Edit_ReachesTheDrawnGround()
    {
        using TerrainData data = Flat(600);
        TerrainSurface surface = new(_harness.Renderer, data);
        _harness.Owned.Add(surface);
        _harness.World.Providers.Add(surface);
        _harness.World.AddLight(new LightDesc { Type = LightType.Directional, Direction = Float3.Normalize(new Float3(0.5f, -1, 0.2f)), Color = new Float3(1, 1, 1), Intensity = 5f });

        RenderView view = _harness.View();
        Float4x4 down = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        view.Camera = new ViewCamera(new ViewLayer { View = down, Projection = RendererHarness.Projection, Position = new Double3(300.5, 80, 300.25) });
        for (int frame = 0; frame < 60 && (frame < 4 || surface.Streaming.Loading > 0 || surface.Streaming.Wanted > surface.Streaming.Resident); frame++)
            _harness.Frame(view);
        Assert.Equal(0, surface.Streaming.Loading);
        Float4[] flat = _harness.Frame(view);

        data.Apply(new TerrainEdit(TerrainBrush.Crater, new Double2(300, 300), 15, 8));
        Float4[] crater = _harness.Frame(view);
        float changed = 0;
        for (int i = 0; i < flat.Length; i++)
            changed += MathF.Abs(flat[i].X - crater[i].X) > 0.02f ? 1 : 0;
        Assert.True(changed > flat.Length * 0.05f, $"{changed} of {flat.Length}");
    }
}
