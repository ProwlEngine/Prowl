using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model's transmission on the GPU: a glass slab's absorption over a flat backdrop.
public sealed partial class LightingTests
{
    // A slab of thickness d facing the camera: its front at z 0 and its back at z d, wider than the view
    private GpuMesh Slab(float d)
    {
        const float w = 40;
        Float3[] positions = [new(-w, -w, 0), new(w, -w, 0), new(w, w, 0), new(-w, w, 0), new(-w, -w, d), new(w, -w, d), new(w, w, d), new(-w, w, d)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)];
        return _renderer.CreateMesh(new MeshData
        {
            VertexCount = 8,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 },
        });
    }

    private RenderableHandle AddSlab(float d, Material material)
        => _world.Add(new RenderableDesc { Mesh = Slab(d), Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [material]);

    // A one sided transmissive material, whose slab draws its front in the Transmission pass and its back in BackDepth
    private Material Glass(Action<Material> set) => PBR(m =>
    {
        m.Surface = new SurfaceSettings();
        set(m);
    });

    private void DarkWorld()
        => _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };

    private void AddBackdrop(Material material)
        => _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, 20)) * Double4x4.CreateScale(4) }, [material]);

    // Under a coat light still refracts into the glass at its own IOR, Snell's law through parallel layers
    [Fact]
    public void ACoatedGlassSlab_RefractsAtTheGlassIor() => AssertSlabAbsorbs(3, 2, 1);

    private void AssertSlabAbsorbs(float d, float depth, float coat)
    {
        DarkWorld();
        Vector3 backdrop = new(0.2f, 0.4f, 0.8f);
        Material flat = UnitUnlit();
        flat.SetColor("base_color", new Float4(backdrop.X, backdrop.Y, backdrop.Z, 1));
        AddBackdrop(flat);

        Vector3 color = new(0.9f, 0.6f, 0.3f);
        AddSlab(d, Glass(m =>
        {
            m.SetFloat("transmission_weight", 1);
            m.SetColor("transmission_color", new Float4(color.X, color.Y, color.Z, 1));
            m.SetFloat("transmission_depth", depth);
            m.SetFloat("specular_roughness", 0.2f);
            if (coat > 0)
            {
                m.SetFloat("coat_weight", coat);
                m.SetFloat("coat_ior", 1.6f);
            }
        }));
        Frame();
        Float4[] pixels = Frame();

        PbrInputs inputs = new(new Vector3(0.8f), Roughness: 0.2f) { TransmissionWeight = 1, TransmissionColor = color, TransmissionDepth = depth, CoatWeight = coat };
        Vector3 n = new(0, 0, -1);
        int checkedPixels = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 position = PixelPosition(x, y);
                Vector3 v = Vector3.Normalize(-position);
                double nv = Vector3.Dot(n, v);
                double mu = PbrReference.RefractedCosine(nv, 1.5);
                Vector3 exit = position + Vector3.Normalize(new Vector3(-v.X, -v.Y, 0)) * (float)(Math.Sqrt(1 - mu * mu) / mu * d);
                if (MathF.Abs(exit.X) > 3.7f || MathF.Abs(exit.Y) > 3.7f)
                    continue;
                checkedPixels++;
                Vector3 transmittance = new PbrReference(inputs, n, v).Transmittance;
                Float4 got = pixels[y * Size + x];
                for (int c = 0; c < 3; c++)
                {
                    double want = backdrop[c] * transmittance[c] * Math.Exp(Math.Log(color[c]) / depth * d / mu);
                    Assert.True(Math.Abs(got[c] - want) <= 2e-3 + 0.01 * want, $"d {d} depth {depth} coat {coat} pixel {x},{y} channel {c}: expected {want}, got {got[c]}");
                }
            }
        }
        Assert.True(checkedPixels > Size * Size / 2, $"only {checkedPixels} pixels refract onto the view");
    }
}
