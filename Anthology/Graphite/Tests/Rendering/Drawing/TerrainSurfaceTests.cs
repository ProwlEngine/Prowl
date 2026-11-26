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

    // A flat package of the given quads a side, every sample at the middle of a 0 to 100 m range, or the finest level at
    // fine, with one detail prototype of the given density everywhere when there is one
    private static TerrainData Flat(int quads, ushort fine = 32768, Func<int, int, bool>? hole = null, byte? detail = null)
    {
        TerrainHeader header = new() { Spacing = 1, Bottom = 0, Range = 100, QuadsX = quads, QuadsZ = quads, Levels = TerrainHeader.LevelsFor(quads, quads), SplatLevels = 1, Details = detail != null ? 1 : 0 };
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
                    byte[]? holes = null;
                    if (level == 0 && hole != null)
                    {
                        holes = new byte[TerrainPage.Samples];
                        for (int j = -1; j <= TerrainPage.Quads + 1; j++)
                            for (int i = -1; i <= TerrainPage.Quads + 1; i++)
                                if (hole(x * TerrainPage.Quads + i, z * TerrainPage.Quads + j))
                                    holes[TerrainPage.Index(i, j)] = 1;
                    }
                    byte[]? details = null;
                    if (level == 0 && detail != null)
                    {
                        details = new byte[TerrainPage.Samples];
                        Array.Fill(details, detail.Value);
                    }
                    writer.Add(new TerrainPage(new TerrainPageKey(level, x, z), heights, normals, null, holes, details), [32768, 32768, 32768, 32768, 32768, 32768, 32768, 32768]);
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
            foreach (string tier in (string[])["FULL", "LITE", "MAX"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
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

        using TerrainData dataPerView = Flat(1000);
        TerrainSurface surfacePerView = new(_harness.Renderer, dataPerView) { Layers = Checker(), CacheBudget = 1 << 25 };
        _harness.Owned.Add(surfacePerView);
        _harness.World.Providers.Add(surfacePerView);
        _harness.World.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(1, 1, 1, 1), Fog = default };

        Float4x4 downPerView = new(new Float4(1, 0, 0, 0), new Float4(0, 0, -1, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1));
        RenderView left = _harness.View(1), right = _harness.View(2);
        left.Viewport = new Float4(0, 0, 0.5f, 1);
        right.Viewport = new Float4(0.5f, 0, 0.5f, 1);
        left.Camera = new ViewCamera(new ViewLayer { View = downPerView, Projection = RendererHarness.Projection, Position = new Double3(300.5, 60, 300.25) });
        right.Camera = new ViewCamera(new ViewLayer { View = downPerView, Projection = RendererHarness.Projection, Position = new Double3(700.25, 60, 700.5) });
        Float4[] cachedPerView = [];
        for (int framePerView = 0; framePerView < 5; framePerView++)
            cachedPerView = _harness.Frame(left, right);
        surfacePerView.UseCache = false;
        Float4[] directPerView = _harness.Frame(left, right);

        double differencePerView = 0, brightnessPerView = 0;
        for (int i = 0; i < directPerView.Length; i++)
        {
            differencePerView += Math.Abs(cachedPerView[i].X - directPerView[i].X) + Math.Abs(cachedPerView[i].Y - directPerView[i].Y);
            brightnessPerView += directPerView[i].X + directPerView[i].Y;
        }
        Assert.True(brightnessPerView / directPerView.Length > 0.05, $"brightnessPerView {brightnessPerView / directPerView.Length}");
        Assert.True(differencePerView / brightnessPerView < 0.1, $"differencePerView {differencePerView / brightnessPerView}");
    }

    [Fact]
    public void GrassShader_Compiles()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        Shader shader = _harness.Renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get($"{ShaderLibrary.Prefix}Terrain/Grass.shader")!));
        foreach (ShaderPass pass in shader.Definition.Passes!)
        {
            foreach (string tier in (string[])["FULL", "LITE", "MAX"])
                TestCompiler.Shared.CompileProgram(pass, [new Keyword(GlobalAxis.PbrTier.Name, tier)]);
        }
    }

    private static TerrainDetailPrototype BrightGrass => new() { Density = 400, Range = 20, MinHeight = 0.3f, MaxHeight = 0.5f, MinWidth = 0.06f, MaxWidth = 0.06f, RootColor = new Float3(0.6f, 0.9f, 0.3f), TipColor = new Float3(0.6f, 0.9f, 0.3f) };

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

    private TerrainPropKind Quad()
    {
        Material material = _harness.Renderer.CreateMaterial(_harness.Renderer.Standard.Lit);
        _harness.Owned.Add(material);
        return new TerrainPropKind { Mesh = _harness.CreateQuad(), Materials = [material] };
    }
}
