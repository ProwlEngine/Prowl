using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Graphite.Rendering;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>
/// A gallery of the PBR model: in front, Khronos glTF sample models on plinths whose textured materials load through
/// Clay, behind them one row of spheres per lobe group from front to back: dielectric base, metals, specular, coat, fuzz
/// and thin film, transmission, subsurface with emission and opacity.
/// </summary>
internal sealed class MaterialsScene : DemoScene
{
    private const double Spacing = 2.5;
    private const double RowDepth = 3.0;
    private const int Columns = 6;

    private const double ShowcaseSpacing = 3.2;

    private static readonly string[] RowNames = ["base", "metal", "specular", "coat", "fuzz and thin film", "transmission", "subsurface, emission, opacity"];

    // Name, largest side in metres, yaw
    private static readonly (string Name, double Size, float Yaw)[][] Showcase =
    [
        [("ClearCoatCarPaint", 1.4, 0f), ("CarbonFibre", 1.6, 0.6f), ("ChairDamaskPurplegold", 1.9, -0.5f), ("AnisotropyBarnLamp", 1.6, 0.6f), ("ToyCar", 2.2, 0.7f)],
        [("SunglassesKhronos", 1.5, 0f), ("IridescenceLamp", 1.9, 0f), ("GlassVaseFlowers", 1.8, 0f), ("DragonDispersion", 2.6, 0f), ("DiffuseTransmissionPlant", 1.9, 0f)],
    ];

    private readonly List<PbrModel> _models = new();
    private readonly List<ReflectionProbeHandle> _probes = new();
    private int _compiling;

    public MaterialsScene(Demo demo) : base(demo)
    {
        Add(Demo.Cube, Pbr(new Float3(0.45f, 0.45f, 0.48f), 0f, 0.85f, grid: 1f), new Double3(0, -0.2, -4), new Double3(40, 0.4, 48));

        ShowcaseRows();

        BaseRow();
        MetalRow();
        SpecularRow();
        CoatRow();
        FuzzAndFilmRow();
        TransmissionRow();
        SubsurfaceRow();

        LightHandle sun = World.AddLight(new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(0.4f, -0.55f, -0.75f)),
            Color = new Float3(1f, 0.94f, 0.85f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 4096, Cascades = 4, Distance = 50 },
        });

        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            AmbientIntensity = 0.4f,
            Sun = sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
        };

        // Captured once, around the glTF models and over the sphere rows
        _probes.Add(StaticProbe(new Double3(0, 2.5, 8.75), new Float3(20, 6, 9)));
        _probes.Add(StaticProbe(new Double3(0, 2, -9), new Float3(18, 5, 22)));
    }

    private ReflectionProbeHandle StaticProbe(Double3 position, Float3 box)
        => World.AddReflectionProbe(new ReflectionProbeDesc
        {
            Position = position,
            BoxSize = box,
            BlendDistance = 1.5f,
            Mode = ReflectionProbeMode.Realtime,
            Refresh = ReflectionProbeRefresh.Once,
            Resolution = 256,
        });

    public override string Name => "Materials";

    public override Double3 StartPosition => new(0, 5.0, 20);

    public override float StartYaw => MathF.PI;

    public override float StartPitch => -0.3f;

    public override string Status => "glTF models, then sphere rows front to back: " + string.Join(", ", RowNames);

    public override string Controls => "F cycles debug views";

    // Captures again once shaders finish compiling, since a capture taken meanwhile misses what they draw
    public override void Update(float time, float deltaTime)
    {
        int compiling = Demo.Renderer.ShaderCompilesPending;
        if (compiling == 0 && _compiling > 0)
        {
            foreach (ReflectionProbeHandle probe in _probes)
                World.RenderReflectionProbe(probe);
        }
        _compiling = compiling;
    }

    public override void Dispose()
    {
        base.Dispose();
        foreach (PbrModel model in _models)
            model.Dispose();
    }

    // Front row coat, anisotropy, fuzz and specular, back row thin film, volumes with dispersion, diffuse transmission
    private void ShowcaseRows()
    {
        Material plinth = Pbr(new Float3(0.08f, 0.08f, 0.09f), 0f, 0.35f);
        for (int row = 0; row < Showcase.Length; row++)
        {
            double z = 11.0 - row * 4.5;
            double height = 0.3 + row * 0.6;
            for (int i = 0; i < Showcase[row].Length; i++)
            {
                (string name, double size, float yaw) = Showcase[row][i];
                double x = ((Showcase[row].Length - 1) * 0.5 - i + row * 0.5) * ShowcaseSpacing;
                Add(Demo.Cylinder, plinth, new Double3(x, height * 0.5, z), new Double3(2.4, height, 2.4));

                PbrModel model = PbrModel.Load(Demo, Path.Combine(AppContext.BaseDirectory, "Assets", "Models", name, name + ".glb"));
                model.AddTo(World, new Double3(x, height, z), size, yaw);
                _models.Add(model);
            }
        }
    }

    private void Sphere(int row, int column, Material material)
    {
        Double3 position = new(-(Columns - 1) * Spacing * 0.5 + column * Spacing, 1.0, -row * RowDepth);
        Add(Demo.Sphere, material, position, new Double3(1.6, 1.6, 1.6));
    }

    private static Float4 C(float r, float g, float b) => new(r, g, b, 1);

    // Plastic from glossy to rough, then a rough clay through EON's diffuse roughness
    private void BaseRow()
    {
        float[] rough = [0.05f, 0.2f, 0.4f, 0.7f];
        for (int i = 0; i < rough.Length; i++)
            Sphere(0, i, Pbr(new Float3(0.7f, 0.08f, 0.06f), 0f, rough[i]));

        Material clay = Pbr(new Float3(0.6f, 0.42f, 0.3f), 0f, 0.9f);
        Sphere(0, 4, clay);
        Material clayRough = Pbr(new Float3(0.6f, 0.42f, 0.3f), 0f, 0.9f);
        clayRough.SetFloat("base_diffuse_roughness", 1f);
        Sphere(0, 5, clayRough);
    }

    // Gold, copper and aluminium with F82 edge tints, brushed steel, coarse iron
    private void MetalRow()
    {
        Material gold = Pbr(new Float3(1f, 0.78f, 0.34f), 1f, 0.15f);
        gold.SetColor("specular_color", C(1f, 0.93f, 0.6f));
        Sphere(1, 0, gold);

        Material copper = Pbr(new Float3(0.96f, 0.64f, 0.54f), 1f, 0.25f);
        copper.SetColor("specular_color", C(1f, 0.85f, 0.75f));
        Sphere(1, 1, copper);

        Material aluminium = Pbr(new Float3(0.91f, 0.92f, 0.92f), 1f, 0.1f);
        aluminium.SetColor("specular_color", C(0.92f, 0.94f, 1f));
        Sphere(1, 2, aluminium);

        Material brushed = Pbr(new Float3(0.75f, 0.75f, 0.77f), 1f, 0.35f);
        brushed.SetFloat("specular_roughness_anisotropy", 0.85f);
        Sphere(1, 3, brushed);

        Material brushedLight = Pbr(new Float3(0.75f, 0.75f, 0.77f), 1f, 0.35f);
        brushedLight.SetFloat("specular_roughness_anisotropy", 0.4f);
        Sphere(1, 4, brushedLight);

        Sphere(1, 5, Pbr(new Float3(0.53f, 0.51f, 0.49f), 1f, 0.75f));
    }

    // specular_ior from water to diamond, specular_weight from none to doubled, a tinted specular_color
    private void SpecularRow()
    {
        Float3 black = new(0.02f, 0.02f, 0.025f);
        float[] ior = [1.33f, 1.5f, 2.42f];
        for (int i = 0; i < ior.Length; i++)
        {
            Material m = Pbr(black, 0f, 0.1f);
            m.SetFloat("specular_ior", ior[i]);
            Sphere(2, i, m);
        }

        Material none = Pbr(black, 0f, 0.1f);
        none.SetFloat("specular_weight", 0f);
        Sphere(2, 3, none);

        Material doubled = Pbr(black, 0f, 0.1f);
        doubled.SetFloat("specular_weight", 2f);
        Sphere(2, 4, doubled);

        Material tinted = Pbr(black, 0f, 0.1f);
        tinted.SetColor("specular_color", C(0.3f, 0.6f, 1f));
        Sphere(2, 5, tinted);
    }

    // Car paint, lacquer over rough metal, an amber coat, darkening off and on, a rough coat
    private void CoatRow()
    {
        Material carPaint = Pbr(new Float3(0.6f, 0.02f, 0.03f), 0.6f, 0.45f);
        carPaint.SetFloat("coat_weight", 1f);
        Sphere(3, 0, carPaint);

        Material lacquer = Pbr(new Float3(0.55f, 0.55f, 0.58f), 1f, 0.6f);
        lacquer.SetFloat("coat_weight", 1f);
        Sphere(3, 1, lacquer);

        Material amber = Pbr(new Float3(0.8f, 0.8f, 0.8f), 0f, 0.5f);
        amber.SetFloat("coat_weight", 1f);
        amber.SetColor("coat_color", C(1f, 0.6f, 0.2f));
        Sphere(3, 2, amber);

        Material bright = Pbr(new Float3(0.1f, 0.3f, 0.7f), 0f, 0.5f);
        bright.SetFloat("coat_weight", 1f);
        bright.SetFloat("coat_darkening", 0f);
        Sphere(3, 3, bright);

        Material dark = Pbr(new Float3(0.1f, 0.3f, 0.7f), 0f, 0.5f);
        dark.SetFloat("coat_weight", 1f);
        dark.SetFloat("coat_darkening", 1f);
        Sphere(3, 4, dark);

        Material satin = Pbr(new Float3(0.15f, 0.5f, 0.2f), 0f, 0.3f);
        satin.SetFloat("coat_weight", 1f);
        satin.SetFloat("coat_roughness", 0.35f);
        satin.SetFloat("coat_roughness_anisotropy", 0.7f);
        Sphere(3, 5, satin);
    }

    // Velvet and peach fuzz, then thin films: a soap like film on glassy black, oil on steel, two thicknesses
    private void FuzzAndFilmRow()
    {
        Material velvet = Pbr(new Float3(0.25f, 0.02f, 0.08f), 0f, 0.8f);
        velvet.SetFloat("fuzz_weight", 1f);
        velvet.SetColor("fuzz_color", C(0.9f, 0.35f, 0.5f));
        velvet.SetFloat("fuzz_roughness", 0.5f);
        Sphere(4, 0, velvet);

        Material peach = Pbr(new Float3(0.9f, 0.55f, 0.35f), 0f, 0.6f);
        peach.SetFloat("fuzz_weight", 0.8f);
        peach.SetColor("fuzz_color", C(1f, 0.9f, 0.8f));
        peach.SetFloat("fuzz_roughness", 0.3f);
        Sphere(4, 1, peach);

        float[] thickness = [0.3f, 0.5f];
        for (int i = 0; i < thickness.Length; i++)
        {
            Material film = Pbr(new Float3(0.02f, 0.02f, 0.02f), 0f, 0.05f);
            film.SetFloat("thin_film_weight", 1f);
            film.SetFloat("thin_film_thickness", thickness[i]);
            film.SetFloat("thin_film_ior", 1.33f);
            Sphere(4, 2 + i, film);
        }

        Material oil = Pbr(new Float3(0.6f, 0.6f, 0.62f), 1f, 0.1f);
        oil.SetFloat("thin_film_weight", 1f);
        oil.SetFloat("thin_film_thickness", 0.4f);
        oil.SetFloat("thin_film_ior", 1.5f);
        Sphere(4, 4, oil);

        Material coatedFilm = Pbr(new Float3(0.05f, 0.05f, 0.2f), 0.8f, 0.2f);
        coatedFilm.SetFloat("coat_weight", 1f);
        coatedFilm.SetFloat("thin_film_weight", 1f);
        coatedFilm.SetFloat("thin_film_thickness", 0.6f);
        Sphere(4, 5, coatedFilm);
    }

    // Clear, coloured, frosted, dispersive, milky and thin walled glass, with striped blocks behind to refract
    private void TransmissionRow()
    {
        Material clear = Glass();
        Sphere(5, 0, clear);

        Material coloured = Glass();
        coloured.SetColor("transmission_color", C(0.2f, 0.75f, 0.4f));
        coloured.SetFloat("transmission_depth", 1.2f);
        Sphere(5, 1, coloured);

        Material frosted = Glass();
        frosted.SetFloat("specular_roughness", 0.35f);
        Sphere(5, 2, frosted);

        Material prism = Glass();
        prism.SetFloat("transmission_dispersion_scale", 1f);
        prism.SetFloat("transmission_dispersion_abbe_number", 20f);
        prism.SetFloat("specular_ior", 1.7f);
        Sphere(5, 3, prism);

        Material milky = Glass();
        milky.SetFloat("transmission_depth", 0.6f);
        milky.SetColor("transmission_scatter", C(0.6f, 0.6f, 0.65f));
        Sphere(5, 4, milky);

        Material bubble = Glass();
        bubble.SetInt("geometry_thin_walled", 1);
        bubble.SetFloat("thin_film_weight", 1f);
        bubble.SetFloat("thin_film_thickness", 0.45f);
        Sphere(5, 5, bubble);

        Float3[] stripes = [new(0.9f, 0.1f, 0.1f), new(0.95f, 0.85f, 0.1f), new(0.1f, 0.6f, 0.95f)];
        for (int i = 0; i < 9; i++)
        {
            double x = -7.5 + i * 1.9;
            Add(Demo.Cube, Pbr(stripes[i % stripes.Length], 0f, 0.6f), new Double3(x, 1.2, -5 * RowDepth - 2.2), new Double3(0.8, 2.4, 0.3));
        }
    }

    // Wax and skin through the subsurface fallback, a backlit thin walled leaf, emission in nits, half and quarter opacity
    private void SubsurfaceRow()
    {
        Material wax = Pbr(new Float3(0.9f, 0.85f, 0.7f), 0f, 0.4f);
        wax.SetFloat("subsurface_weight", 1f);
        wax.SetColor("subsurface_color", C(0.95f, 0.85f, 0.6f));
        wax.SetFloat("subsurface_radius", 0.5f);
        Sphere(6, 0, wax);

        Material skin = Pbr(new Float3(0.8f, 0.55f, 0.45f), 0f, 0.5f);
        skin.SetFloat("subsurface_weight", 1f);
        skin.SetColor("subsurface_color", C(0.85f, 0.45f, 0.35f));
        skin.SetFloat("subsurface_radius", 1f);
        skin.SetColor("subsurface_radius_scale", C(1f, 0.35f, 0.2f));
        Sphere(6, 1, skin);

        Material leaf = Pbr(new Float3(0.2f, 0.45f, 0.1f), 0f, 0.5f);
        leaf.SetInt("geometry_thin_walled", 1);
        leaf.SetFloat("subsurface_weight", 0.7f);
        leaf.SetColor("subsurface_color", C(0.35f, 0.75f, 0.15f));
        leaf.SetFloat("subsurface_scatter_anisotropy", 0.4f);
        Double3 leafPosition = new(-(Columns - 1) * Spacing * 0.5 + 2 * Spacing, 1.0, -6 * RowDepth);
        Add(Demo.Cube, leaf, leafPosition, new Double3(1.6, 1.6, 0.02), Quaternion.AxisAngle(Float3.UnitY, 0.6f));

        Sphere(6, 3, Pbr(new Float3(0f, 0f, 0f), 0f, 0.5f, new Float3(20000f, 9000f, 3000f)));

        float[] opacity = [0.5f, 0.25f];
        for (int i = 0; i < opacity.Length; i++)
        {
            Material faded = Pbr(new Float3(0.1f, 0.4f, 0.9f), 0f, 0.2f);
            faded.SetFloat("geometry_opacity", opacity[i]);
            faded.Surface = new SurfaceSettings(SurfaceType.Transparent, SurfaceBlend.Premultiplied);
            Sphere(6, 4 + i, faded);
        }
    }

    private Material Glass()
    {
        Material glass = Pbr(new Float3(1f, 1f, 1f), 0f, 0.02f);
        glass.SetFloat("transmission_weight", 1f);
        return glass;
    }
}
