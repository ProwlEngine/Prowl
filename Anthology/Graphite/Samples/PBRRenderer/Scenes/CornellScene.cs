using Prowl.Graphite.Rendering;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>
/// A Cornell box standing outdoors in daylight, open toward the camera: a red and a green wall around white ones and
/// two white blocks, the sun reaching in over the floor, so the walls' colour shows on what the sun never touches.
/// </summary>
internal sealed class CornellScene : DemoScene
{
    private const double Size = 6, Wall = 0.2;

    public CornellScene(Demo demo) : base(demo)
    {
        Material white = Pbr(new Float3(0.73f, 0.73f, 0.73f), 0f, 0.9f);
        Material red = Pbr(new Float3(0.65f, 0.05f, 0.05f), 0f, 0.9f);
        Material green = Pbr(new Float3(0.12f, 0.45f, 0.15f), 0f, 0.9f);
        double half = Size * 0.5, outer = Size + Wall * 2;

        Add(Demo.Cube, Pbr(new Float3(0.4f, 0.4f, 0.4f), 0f, 0.9f, grid: 4f), new Double3(0, -0.25, 0), new Double3(600, 0.5, 600), name: "Ground");
        Add(Demo.Cube, white, new Double3(0, Wall * 0.5, 0), new Double3(outer, Wall, Size), name: "Floor");
        Add(Demo.Cube, white, new Double3(0, Size + Wall * 1.5, 0), new Double3(outer, Wall, Size), name: "Ceiling");
        Add(Demo.Cube, white, new Double3(0, half + Wall, -half - Wall * 0.5), new Double3(outer, Size + Wall * 2, Wall), name: "Back");
        Add(Demo.Cube, red, new Double3(-half - Wall * 0.5, half + Wall, 0), new Double3(Wall, Size, Size), name: "Left");
        Add(Demo.Cube, green, new Double3(half + Wall * 0.5, half + Wall, 0), new Double3(Wall, Size, Size), name: "Right");
        Add(Demo.Cube, white, new Double3(-1.1, Wall + 1.8, -1.0), new Double3(1.7, 3.6, 1.7), Quaternion.AxisAngle(Float3.UnitY, 0.3f), name: "Tall block");
        Add(Demo.Cube, white, new Double3(1.2, Wall + 0.85, 0.9), new Double3(1.7, 1.7, 1.7), Quaternion.AxisAngle(Float3.UnitY, -0.3f), name: "Short block");

        LightHandle sun = World.AddLight(new LightDesc
        {
            Name = "Sun",
            Type = LightType.Directional,
            Direction = Float3.Normalize(new Float3(-0.25f, -0.6f, -0.75f)),
            Color = new Float3(1f, 0.95f, 0.88f),
            Intensity = 100000f,
            AngularRadius = 0.5f,
            Shadow = new ShadowDesc { Enabled = true, Resolution = 2048, Cascades = 4, Distance = 200 },
        });
        World.Environment = new EnvironmentDesc
        {
            Ambient = AmbientMode.Sky,
            AmbientIntensity = 1f,
            Sun = sun,
            Sky = Track(demo.Renderer.CreateMaterial(demo.Renderer.Skies.Procedural)),
        };
    }

    public override string Name => "Cornell box";

    public override Double3 StartPosition => new(0, 3.2, 12);

    public override float StartYaw => System.MathF.PI;

    public override float StartPitch => 0f;

    public override void Update(float time, float deltaTime)
    {
    }
}
