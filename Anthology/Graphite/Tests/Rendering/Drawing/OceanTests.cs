using System;
using System.Linq;
using System.Numerics;

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
