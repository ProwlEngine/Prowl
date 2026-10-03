// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Particle Showcase
//
// A row of stations, each built around a different part of the particle system:
//   1  Fountain        cone emission, gravity, world collision
//   2  Campfire        additive flipbook flames, soft particles, particle lights, lit smoke, wind, stretched embers
//   3  Fireworks       bursts, death sub emitters with color inheritance, trails, drag
//   4  Grinder         stretched billboards, color by speed, plane collision, collision sub emitters
//   5  Vortex          orbital and radial velocity, random between two curves, lights
//   6  World vs Local  simulation spaces, rate over distance, inherit velocity
//   7  Snow            box shape, wind zone and turbulence, resting on the ground, culling with catch up
//   8  Debris          mesh particles, 3D rotation, lit shading, world collision
//   9  Shapes          every emission shape side by side
//   0  Flipbooks       texture sheet modes, frame blending, flips, horizontal billboards
//
// Controls:
//   1 to 9, 0   Jump to a station
//   WASD, Q/E   Fly, hold Right Mouse to look, Shift to go faster
//   Space       Restart every system
//   P           Pause or resume every system
//   F1          Hide the HUD
//

using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Vector;

using static Prowl.Samples.Sample;

using Gradient = Prowl.Vector.Gradient;

namespace ParticleShowcase;

internal class Program
{
    static void Main(string[] args)
    {
        new ParticleShowcaseGame().Run("Particle Showcase", 1600, 900);
    }
}

public sealed class ParticleShowcaseGame : StationGame
{
    private const float StationSpacing = 24f;

    private readonly List<GameObject> _roots = new();
    private readonly List<ParticleSystemComponent> _systems = new();

    private Material _dotAlpha = null!;
    private Material _dotAdditive = null!;
    private Material _flame = null!;
    private Material _smoke = null!;
    private Material _flipbook = null!;
    private Material _ring = null!;
    private Material _solid = null!;

    protected override string ExtraKeys => "Space  restart    P  pause";

    public override string Stats
    {
        get
        {
            int particles = 0;
            foreach (ParticleSystemComponent system in _systems)
                foreach (ParticleSystemComponent child in system.GetComponentsInChildren<ParticleSystemComponent>())
                    particles += child.ParticleCount;
            return $"{particles:N0} live particles";
        }
    }

    protected override void Build()
    {
        CreateMaterials();
        CreateEnvironment();

        BuildFountain(StationCenter(0));
        BuildCampfire(StationCenter(1));
        BuildFireworks(StationCenter(2));
        BuildGrinder(StationCenter(3));
        BuildVortex(StationCenter(4));
        BuildSpaces(StationCenter(5));
        BuildSnow(StationCenter(6));
        BuildDebris(StationCenter(7));
        BuildShapes(StationCenter(8));
        BuildFlipbooks(StationCenter(9));

        foreach (GameObject root in _roots)
            Add(root);
    }

    private static readonly Float3 DefaultView = new(0f, 5f, -17f);

    private static Float3 StationCenter(int index) => new(index * StationSpacing, 0f, 0f);

    // ----------------------------------------------------------------
    //  Scene
    // ----------------------------------------------------------------

    private void CreateEnvironment()
    {
        GameObject light = new("Moon Light");
        DirectionalLight directional = light.AddComponent<DirectionalLight>();
        directional.Color = new Color(0.65f, 0.72f, 1f, 1f);
        directional.Intensity = 0.35f;
        light.Transform.LocalEulerAngles = new Float3(50f, 210f, 0f);
        _roots.Add(light);

        MainCamera.ClearFlags = CameraClearFlags.SolidColor;
        MainCamera.ClearColor = new Color(0.02f, 0.025f, 0.045f, 1f);
        MainCamera.Effects =
        [
            new BloomEffect { Intensity = 0.6f, Threshold = 1.0f },
            new FXAAEffect(),
            new TonemapperEffect(),
        ];

        // One long floor under every station, with a collider for the world collision stations.
        float length = StationSpacing * 10f + 20f;
        GameObject floor = new("Floor");
        MeshRenderer renderer = floor.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(new Float3(length, 1f, 40f));
        renderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        renderer.Material.SetColor("_MainColor", new Color(0.035f, 0.037f, 0.045f, 1f));
        floor.AddComponent<BoxCollider>().Size = new Float3(length, 1f, 40f);
        floor.Transform.Position = new Float3(StationSpacing * 4.5f, -0.5f, 0f);
        _roots.Add(floor);
    }

    private void CreateMaterials()
    {
        Texture2D dot = Load<Texture2D>("Textures/Soft Dot");
        _dotAlpha = ParticleMaterial(dot);
        _dotAdditive = ParticleMaterial(dot);
        _flame = ParticleMaterial(Load<Texture2D>("Textures/Flame Sheet"));
        _smoke = ParticleMaterial(Load<Texture2D>("Textures/Smoke Sheet"));
        _flipbook = ParticleMaterial(Load<Texture2D>("Textures/Spinner Sheet"));
        _ring = ParticleMaterial(Load<Texture2D>("Textures/Ring"));
        _solid = ParticleMaterial(Texture2D.LoadDefault(DefaultTexture.White));
    }

    private static Material ParticleMaterial(Texture2D texture)
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Particle));
        material.SetTexture("_MainTex", texture);
        material.SetColor("_MainColor", Color.White);
        return material;
    }

    // ----------------------------------------------------------------
    //  Stations
    // ----------------------------------------------------------------

    private void BuildFountain(Float3 c)
    {
        AddStation("Fountain", "Cone emission with gravity. Particles bounce off the physics floor and lose lifetime on every hit.", c, DefaultView, 3f);

        var ps = CreateSystem("Fountain", c + new Float3(0f, 0.2f, 0f), _dotAlpha);
        ps.MaxParticles = 3000;
        ps.SimulationSpace = SimulationSpace.World;
        ps.Initial.StartLifetime = new MinMaxCurve(2.5f, 3.5f);
        ps.Initial.StartSpeed = new MinMaxCurve(9f, 11f);
        ps.Initial.StartSize = new MinMaxCurve(0.12f, 0.2f);
        ps.Initial.StartColor = new MinMaxGradient(new Color(0.45f, 0.75f, 1f, 1f), Color.White);
        ps.Initial.GravityModifier = 1f;
        ps.Emission.RateOverTime = new MinMaxCurve(450f);
        ps.Shape.Type = ParticleShapeType.Cone;
        ps.Shape.Angle = 12f;
        ps.Shape.Radius = 0.2f;

        ps.ColorOverLifetime.Enabled = true;
        ps.ColorOverLifetime.Color = new MinMaxGradient(Grad(
            [(0f, new Color(0.6f, 0.85f, 1f, 1f)), (1f, Color.White)],
            [(0f, 1f), (0.8f, 0.9f), (1f, 0f)]));

        ps.SizeOverLifetime.Enabled = true;
        ps.SizeOverLifetime.Size = new MinMaxCurve(Curve((0f, 0.6f), (0.2f, 1f), (1f, 1.4f)));

        ps.Collision.Enabled = true;
        ps.Collision.Type = ParticleCollisionType.World;
        ps.Collision.Bounce = 0.35f;
        ps.Collision.Dampen = 0.1f;
        ps.Collision.LifetimeLoss = 0.25f;
    }

    private void BuildCampfire(Float3 c)
    {
        AddStation("Campfire", "Additive flipbook flames with frame blending and soft particles, a few flames cast real lights. Embers are stretched and blown around, smoke is lit and drifts on the wind.", c, new Float3(0f, 3f, -8f), 1.8f);

        var logs = new GameObject("Logs");
        MeshRenderer logRenderer = logs.AddComponent<MeshRenderer>();
        logRenderer.Mesh = Mesh.CreateCube(new Float3(1.6f, 0.25f, 0.3f));
        logRenderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        logRenderer.Material.SetColor("_MainColor", new Color(0.25f, 0.14f, 0.08f, 1f));
        logs.Transform.Position = c + new Float3(0f, 0.12f, 0f);
        logs.Transform.LocalEulerAngles = new Float3(0f, 35f, 0f);
        _roots.Add(logs);

        var flames = CreateSystem("Flames", c + new Float3(0f, 0.2f, 0f), _flame);
        flames.Initial.StartLifetime = new MinMaxCurve(0.8f, 1.2f);
        flames.Initial.StartSpeed = new MinMaxCurve(0.3f, 0.8f);
        flames.Initial.StartSize = new MinMaxCurve(1.2f, 1.8f);
        flames.Initial.StartRotation = new MinMaxCurve(-15f, 15f);
        flames.Initial.StartColor = new MinMaxGradient(new Color(3f, 1.4f, 0.4f, 1f));
        flames.Emission.RateOverTime = new MinMaxCurve(45f);
        flames.Shape.Type = ParticleShapeType.Circle;
        flames.Shape.Radius = 0.45f;

        flames.VelocityOverLifetime.Enabled = true;
        flames.VelocityOverLifetime.Space = ParticleSpace.World;
        flames.VelocityOverLifetime.Y = new MinMaxCurve(Curve((0f, 0.5f), (1f, 3f)));

        flames.SizeOverLifetime.Enabled = true;
        flames.SizeOverLifetime.Size = new MinMaxCurve(Curve((0f, 0.7f), (0.3f, 1f), (1f, 0.2f)));

        flames.ColorOverLifetime.Enabled = true;
        flames.ColorOverLifetime.Color = new MinMaxGradient(Grad(
            [(0f, new Color(1f, 0.95f, 0.7f, 1f)), (0.4f, new Color(1f, 0.55f, 0.2f, 1f)), (1f, new Color(0.6f, 0.1f, 0.05f, 1f))],
            [(0f, 0f), (0.15f, 1f), (0.7f, 0.7f), (1f, 0f)]));

        flames.TextureSheet.Enabled = true;
        flames.TextureSheet.TilesX = 4;
        flames.TextureSheet.TilesY = 4;
        flames.TextureSheet.FrameBlending = true;
        flames.TextureSheet.StartFrame = new MinMaxCurve(0f, 16f);

        flames.Light.Enabled = true;
        flames.Light.Ratio = 0.15f;
        flames.Light.MaxLights = 4;
        flames.Light.Color = new Color(1f, 0.6f, 0.3f, 1f);
        flames.Light.UseParticleColor = false;
        flames.Light.Range = new MinMaxCurve(6f);
        flames.Light.Intensity = new MinMaxCurve(Curve((0f, 0f), (0.2f, 1.5f), (1f, 0f)));

        flames.Renderer.BlendMode = ParticleBlendMode.Additive;
        flames.Renderer.SoftParticleDistance = 0.5f;

        var embers = CreateSystem("Embers", c + new Float3(0f, 0.4f, 0f), _dotAdditive, flames.GameObject);
        embers.Initial.StartLifetime = new MinMaxCurve(1f, 2.5f);
        embers.Initial.StartSpeed = new MinMaxCurve(1f, 3f);
        embers.Initial.StartSize = new MinMaxCurve(0.03f, 0.06f);
        embers.Initial.StartColor = new MinMaxGradient(new Color(5f, 1.8f, 0.4f, 1f));
        embers.Initial.GravityModifier = -0.1f;
        embers.Emission.RateOverTime = new MinMaxCurve(25f);
        embers.Shape.Type = ParticleShapeType.Cone;
        embers.Shape.Angle = 25f;
        embers.Shape.Radius = 0.4f;
        embers.Wind.Enabled = true;
        embers.Wind.AmbientWind = new Float3(0.6f, 0f, 0f);
        embers.Wind.Drag = 0.5f;
        embers.Wind.Turbulence = 1.5f;
        embers.Wind.TurbulenceScale = 3f;
        embers.ColorOverLifetime.Enabled = true;
        embers.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, new Color(1f, 0.3f, 0.1f, 1f))], [(0f, 1f), (0.7f, 1f), (1f, 0f)]));
        embers.Renderer.RenderMode = ParticleRenderMode.StretchedBillboard;
        embers.Renderer.LengthScale = 3f;
        embers.Renderer.VelocityScale = 0.04f;
        embers.Renderer.BlendMode = ParticleBlendMode.Additive;

        var smoke = CreateSystem("Smoke", c + new Float3(0f, 1.6f, 0f), _smoke, flames.GameObject);
        smoke.MaxParticles = 200;
        smoke.Initial.StartLifetime = new MinMaxCurve(4f, 6f);
        smoke.Initial.StartSpeed = new MinMaxCurve(0.3f, 0.8f);
        smoke.Initial.StartSize = new MinMaxCurve(1f, 1.6f);
        smoke.Initial.StartRotation = new MinMaxCurve(0f, 360f);
        smoke.Initial.StartColor = new MinMaxGradient(new Color(0.55f, 0.55f, 0.6f, 1f));
        smoke.Emission.RateOverTime = new MinMaxCurve(6f);
        smoke.Shape.Type = ParticleShapeType.Circle;
        smoke.Shape.Radius = 0.3f;
        smoke.VelocityOverLifetime.Enabled = true;
        smoke.VelocityOverLifetime.Space = ParticleSpace.World;
        smoke.VelocityOverLifetime.Y = new MinMaxCurve(1f);
        smoke.SizeOverLifetime.Enabled = true;
        smoke.SizeOverLifetime.Size = new MinMaxCurve(Curve((0f, 1f), (1f, 3f)));
        smoke.RotationOverLifetime.Enabled = true;
        smoke.RotationOverLifetime.Z = new MinMaxCurve(-20f, 20f);
        smoke.ColorOverLifetime.Enabled = true;
        smoke.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 0f), (0.2f, 0.55f), (1f, 0f)]));
        smoke.Wind.Enabled = true;
        smoke.Wind.AmbientWind = new Float3(1.2f, 0f, 0f);
        smoke.Wind.Drag = 0.4f;
        smoke.Wind.Turbulence = 0.6f;
        smoke.TextureSheet.Enabled = true;
        smoke.TextureSheet.TilesX = 4;
        smoke.TextureSheet.TilesY = 4;
        smoke.TextureSheet.FrameBlending = true;
        smoke.Renderer.Lit = true;
        smoke.Renderer.SoftParticleDistance = 0.8f;
    }

    private void BuildFireworks(Float3 c)
    {
        AddStation("Fireworks", "Timed bursts launch shells with trails. Each shell's death fires a sub emitter that inherits its color, with drag slowing the sparks.", c, DefaultView, 3f);

        Gradient rainbow = Grad(
            [(0f, new Color(3f, 0.4f, 0.4f, 1f)), (0.25f, new Color(3f, 2.5f, 0.3f, 1f)), (0.5f, new Color(0.4f, 3f, 0.6f, 1f)),
             (0.75f, new Color(0.4f, 1.2f, 3f, 1f)), (1f, new Color(2.5f, 0.5f, 3f, 1f))],
            [(0f, 1f), (1f, 1f)]);

        var shells = CreateSystem("Shells", c, _dotAdditive);
        shells.Duration = 2.5f;
        shells.SimulationSpace = SimulationSpace.World;
        shells.Initial.StartLifetime = new MinMaxCurve(1.1f, 1.4f);
        shells.Initial.StartSpeed = new MinMaxCurve(13f, 16f);
        shells.Initial.StartSize = new MinMaxCurve(0.25f);
        shells.Initial.StartColor = new MinMaxGradient(rainbow) { Mode = MinMaxGradientMode.RandomColor };
        shells.Initial.GravityModifier = 1f;
        shells.Emission.RateOverTime = new MinMaxCurve(0f);
        shells.Emission.Bursts.Add(new ParticleBurst(0f, 1, 2, 1, 0.01f));
        shells.Emission.Bursts.Add(new ParticleBurst(1.2f, 1));
        shells.Shape.Type = ParticleShapeType.Cone;
        shells.Shape.Angle = 12f;
        shells.Shape.Radius = 0.5f;
        shells.Trails.Enabled = true;
        shells.Trails.Lifetime = new MinMaxCurve(0.35f);
        shells.Trails.MinVertexDistance = 0.15f;
        shells.Trails.WidthMultiplier = 0.6f;
        shells.Trails.ColorOverTrail = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 1f), (1f, 0f)]));
        shells.Renderer.BlendMode = ParticleBlendMode.Additive;

        var burst = CreateSystem("Explosion", c, _dotAdditive, shells.GameObject);
        burst.MaxParticles = 4000;
        burst.SimulationSpace = SimulationSpace.World;
        burst.Initial.StartLifetime = new MinMaxCurve(1.2f, 1.8f);
        burst.Initial.StartSpeed = new MinMaxCurve(5f, 8f);
        burst.Initial.StartSize = new MinMaxCurve(0.08f, 0.14f);
        burst.Initial.StartColor = new MinMaxGradient(new Color(1.6f, 1.6f, 1.6f, 1f));
        burst.Initial.GravityModifier = 0.4f;
        burst.Emission.Bursts.Add(new ParticleBurst(0f, 90, 130, 1, 0.01f));
        burst.Shape.Type = ParticleShapeType.Sphere;
        burst.Shape.Radius = 0.1f;
        burst.Shape.RadiusThickness = 0f;
        burst.LimitVelocityOverLifetime.Enabled = true;
        burst.LimitVelocityOverLifetime.Limit = new MinMaxCurve(100f);
        burst.LimitVelocityOverLifetime.Drag = new MinMaxCurve(1.5f);
        burst.LimitVelocityOverLifetime.MultiplyDragBySize = false;
        burst.LimitVelocityOverLifetime.MultiplyDragByVelocity = false;
        burst.ColorOverLifetime.Enabled = true;
        burst.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, new Color(1f, 0.6f, 0.4f, 1f))], [(0f, 1f), (0.6f, 1f), (1f, 0f)]));
        burst.Trails.Enabled = true;
        burst.Trails.Lifetime = new MinMaxCurve(0.2f);
        burst.Trails.MinVertexDistance = 0.1f;
        burst.Trails.WidthMultiplier = 0.8f;
        burst.Renderer.BlendMode = ParticleBlendMode.Additive;

        shells.SubEmitters.Enabled = true;
        shells.SubEmitters.Emitters.Add(new SubEmitter { System = burst, Type = SubEmitterType.Death, InheritColor = true });
    }

    private void BuildGrinder(Float3 c)
    {
        AddStation("Grinder", "Stretched billboards cooling from white hot as they slow (color by speed). They bounce off a collision plane and spit smaller sparks from a collision sub emitter.", c, new Float3(0f, 3.5f, -10f), 1f);

        var wheel = new GameObject("Grinder Wheel");
        MeshRenderer wheelRenderer = wheel.AddComponent<MeshRenderer>();
        wheelRenderer.Mesh = Mesh.CreateCylinder(0.6f, 0.15f, 24);
        wheelRenderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        wheelRenderer.Material.SetColor("_MainColor", new Color(0.4f, 0.4f, 0.45f, 1f));
        wheel.Transform.Position = c + new Float3(-3f, 2f, 0f);
        wheel.Transform.LocalEulerAngles = new Float3(90f, 0f, 0f);
        _roots.Add(wheel);

        var plane = new GameObject("Collision Plane");
        plane.Transform.Position = c;
        _roots.Add(plane);

        var sparks = CreateSystem("Sparks", c + new Float3(-3f, 1.4f, 0f), _dotAdditive);
        sparks.Transform.LocalEulerAngles = new Float3(0f, 0f, -65f);
        sparks.MaxParticles = 3000;
        sparks.SimulationSpace = SimulationSpace.World;
        sparks.Initial.StartLifetime = new MinMaxCurve(0.8f, 1.6f);
        sparks.Initial.StartSpeed = new MinMaxCurve(6f, 11f);
        sparks.Initial.StartSize = new MinMaxCurve(0.04f, 0.07f);
        sparks.Initial.StartColor = new MinMaxGradient(new Color(6f, 3f, 1f, 1f));
        sparks.Initial.GravityModifier = 1f;
        sparks.Emission.RateOverTime = new MinMaxCurve(250f);
        sparks.Shape.Type = ParticleShapeType.Cone;
        sparks.Shape.Angle = 18f;
        sparks.Shape.Radius = 0.02f;

        sparks.ColorBySpeed.Enabled = true;
        sparks.ColorBySpeed.Range = new Float2(0f, 8f);
        sparks.ColorBySpeed.Color = new MinMaxGradient(Grad([(0f, new Color(1f, 0.25f, 0.05f, 1f)), (1f, Color.White)], [(0f, 1f), (1f, 1f)]));

        sparks.Collision.Enabled = true;
        sparks.Collision.Type = ParticleCollisionType.Planes;
        sparks.Collision.Planes.Add(plane);
        sparks.Collision.Bounce = 0.45f;
        sparks.Collision.Dampen = 0.15f;
        sparks.Collision.LifetimeLoss = 0.2f;
        sparks.Collision.MinKillSpeed = 0.5f;

        sparks.Renderer.RenderMode = ParticleRenderMode.StretchedBillboard;
        sparks.Renderer.LengthScale = 1f;
        sparks.Renderer.VelocityScale = 0.05f;
        sparks.Renderer.BlendMode = ParticleBlendMode.Additive;

        var splash = CreateSystem("Splash", c, _dotAdditive, sparks.GameObject);
        splash.SimulationSpace = SimulationSpace.World;
        splash.Initial.StartLifetime = new MinMaxCurve(0.3f, 0.5f);
        splash.Initial.StartSpeed = new MinMaxCurve(1f, 3f);
        splash.Initial.StartSize = new MinMaxCurve(0.03f);
        splash.Initial.StartColor = new MinMaxGradient(new Color(5f, 2f, 0.6f, 1f));
        splash.Initial.GravityModifier = 1f;
        splash.Emission.Bursts.Add(new ParticleBurst(0f, 3));
        splash.Shape.Type = ParticleShapeType.Hemisphere;
        splash.Shape.Radius = 0.05f;
        splash.Renderer.RenderMode = ParticleRenderMode.StretchedBillboard;
        splash.Renderer.VelocityScale = 0.05f;
        splash.Renderer.BlendMode = ParticleBlendMode.Additive;

        sparks.SubEmitters.Enabled = true;
        sparks.SubEmitters.Emitters.Add(new SubEmitter { System = splash, Type = SubEmitterType.Collision, Probability = 0.3f });
    }

    private void BuildVortex(Float3 c)
    {
        AddStation("Vortex", "Particles orbit and fall inward with orbital and radial velocity. Their size pulses between two random curves and a few carry lights.", c, DefaultView, 3f);

        var vortex = CreateSystem("Vortex", c + new Float3(0f, 0.3f, 0f), _dotAdditive);
        vortex.Initial.StartLifetime = new MinMaxCurve(4f, 6f);
        vortex.Initial.StartSpeed = new MinMaxCurve(0f);
        vortex.Initial.StartSize = new MinMaxCurve(0.1f, 0.25f);
        vortex.Initial.StartColor = new MinMaxGradient(new Color(2.2f, 0.5f, 3f, 1f), new Color(0.3f, 2.4f, 3f, 1f));
        vortex.Emission.RateOverTime = new MinMaxCurve(160f);
        vortex.Shape.Type = ParticleShapeType.Circle;
        vortex.Shape.Radius = 3.5f;
        vortex.Shape.RadiusThickness = 0.15f;

        vortex.VelocityOverLifetime.Enabled = true;
        vortex.VelocityOverLifetime.Y = new MinMaxCurve(Curve((0f, 0f), (1f, 2.5f)));
        vortex.VelocityOverLifetime.OrbitalY = new MinMaxCurve(60f, 120f);
        vortex.VelocityOverLifetime.Radial = new MinMaxCurve(-0.45f);

        vortex.SizeOverLifetime.Enabled = true;
        vortex.SizeOverLifetime.Size = new MinMaxCurve(Curve((0f, 0.5f), (0.5f, 1.4f), (1f, 0f)), Curve((0f, 1f), (0.3f, 0.6f), (0.7f, 1.6f), (1f, 0f)));

        vortex.ColorOverLifetime.Enabled = true;
        vortex.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 0f), (0.1f, 1f), (0.8f, 1f), (1f, 0f)]));

        vortex.Light.Enabled = true;
        vortex.Light.Ratio = 0.03f;
        vortex.Light.MaxLights = 5;
        vortex.Light.Range = new MinMaxCurve(3f);
        vortex.Light.Intensity = new MinMaxCurve(2f);

        vortex.Renderer.BlendMode = ParticleBlendMode.Additive;
    }

    private void BuildSpaces(Float3 c)
    {
        AddStation("World vs Local", "Two identical emitters flying in circles. Left simulates in world space and leaves a wake (rate over distance, inherit velocity), right simulates in local space and carries its particles along.", c, new Float3(0f, 6f, -15f), 2.5f);

        ParticleSystemComponent Make(string name, Float3 center, SimulationSpace space, Color color)
        {
            var ps = CreateSystem(name, center, _dotAdditive);
            ps.SimulationSpace = space;
            ps.Initial.StartLifetime = new MinMaxCurve(1.2f);
            ps.Initial.StartSpeed = new MinMaxCurve(0.2f, 0.6f);
            ps.Initial.StartSize = new MinMaxCurve(0.2f, 0.35f);
            ps.Initial.StartColor = new MinMaxGradient(color);
            ps.Emission.RateOverTime = new MinMaxCurve(10f);
            ps.Emission.RateOverDistance = new MinMaxCurve(12f);
            ps.Shape.Type = ParticleShapeType.Sphere;
            ps.Shape.Radius = 0.1f;
            ps.InheritVelocity.Enabled = true;
            ps.InheritVelocity.Multiplier = new MinMaxCurve(0.3f);
            ps.SizeOverLifetime.Enabled = true;
            ps.SizeOverLifetime.Size = new MinMaxCurve(Curve((0f, 1f), (1f, 0f)));
            ps.Renderer.BlendMode = ParticleBlendMode.Additive;
            var orbit = ps.GameObject.AddComponent<Orbiter>();
            orbit.Center = center + new Float3(0f, 2.5f, 0f);
            return ps;
        }

        Make("World Space", c + new Float3(-4.5f, 0f, 0f), SimulationSpace.World, new Color(3f, 1.4f, 0.4f, 1f));
        Make("Local Space", c + new Float3(4.5f, 0f, 0f), SimulationSpace.Local, new Color(0.4f, 1.6f, 3f, 1f));
    }

    private void BuildSnow(Float3 c)
    {
        AddStation("Snow", "A wide box emits flakes that a wind zone and turbulence push around. They settle on a collision plane. Culling is Pause And Catch Up, so it fast forwards when you look back.", c, DefaultView, 3f);

        var zone = new GameObject("Wind Zone");
        WindZone wind = zone.AddComponent<WindZone>();
        wind.Radius = 14f;
        wind.WindMain = 2.5f;
        wind.Turbulence = 1f;
        zone.Transform.Position = c + new Float3(-6f, 3f, 0f);
        _roots.Add(zone);

        var ground = new GameObject("Snow Ground");
        ground.Transform.Position = c + new Float3(0f, 0.02f, 0f);
        _roots.Add(ground);

        var snow = CreateSystem("Snow", c + new Float3(0f, 9f, 0f), _dotAlpha);
        snow.MaxParticles = 5000;
        snow.Prewarm = true;
        snow.CullingMode = ParticleCullingMode.PauseAndCatchUp;
        snow.SimulationSpace = SimulationSpace.World;
        snow.Initial.StartLifetime = new MinMaxCurve(12f);
        snow.Initial.StartSpeed = new MinMaxCurve(0.5f, 1f);
        snow.Initial.StartSize = new MinMaxCurve(0.05f, 0.12f);
        snow.Initial.StartColor = new MinMaxGradient(new Color(1f, 1f, 1f, 0.9f));
        snow.Initial.GravityModifier = 0.05f;
        snow.Emission.RateOverTime = new MinMaxCurve(300f);
        snow.Shape.Type = ParticleShapeType.Box;
        snow.Shape.BoxSize = new Float3(16f, 0.1f, 10f);
        snow.Shape.Rotation = new Float3(180f, 0f, 0f);

        snow.Wind.Enabled = true;
        snow.Wind.Drag = 0.8f;
        snow.Wind.Turbulence = 1.2f;
        snow.Wind.TurbulenceScale = 4f;

        snow.Collision.Enabled = true;
        snow.Collision.Type = ParticleCollisionType.Planes;
        snow.Collision.Planes.Add(ground);
        snow.Collision.Bounce = 0f;
        snow.Collision.Dampen = 1f;

        snow.ColorOverLifetime.Enabled = true;
        snow.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 0f), (0.05f, 1f), (0.9f, 1f), (1f, 0f)]));
    }

    private void BuildDebris(Float3 c)
    {
        AddStation("Debris", "Mesh particles with full 3D rotation, lit by the scene. A burst every loop, tumbling and bouncing on the physics floor.", c, DefaultView, 3f);

        var debris = CreateSystem("Debris", c + new Float3(0f, 0.5f, 0f), _solid);
        debris.Duration = 3f;
        debris.SimulationSpace = SimulationSpace.World;
        debris.Initial.StartLifetime = new MinMaxCurve(5f, 6f);
        debris.Initial.StartSpeed = new MinMaxCurve(4f, 7f);
        debris.Initial.StartSize = new MinMaxCurve(0.15f, 0.35f);
        debris.Initial.StartRotation3D = true;
        debris.Initial.StartRotationX = new MinMaxCurve(0f, 360f);
        debris.Initial.StartRotationY = new MinMaxCurve(0f, 360f);
        debris.Initial.StartRotation = new MinMaxCurve(0f, 360f);
        debris.Initial.StartColor = new MinMaxGradient(new Color(0.35f, 0.33f, 0.3f, 1f), new Color(0.9f, 0.55f, 0.25f, 1f));
        debris.Initial.GravityModifier = 1f;
        debris.Emission.RateOverTime = new MinMaxCurve(0f);
        debris.Emission.Bursts.Add(new ParticleBurst(0f, 40));
        debris.Shape.Type = ParticleShapeType.Hemisphere;
        debris.Shape.Radius = 0.5f;

        debris.RotationOverLifetime.Enabled = true;
        debris.RotationOverLifetime.SeparateAxes = true;
        debris.RotationOverLifetime.X = new MinMaxCurve(-180f, 180f);
        debris.RotationOverLifetime.Y = new MinMaxCurve(-180f, 180f);
        debris.RotationOverLifetime.Z = new MinMaxCurve(-180f, 180f);

        debris.Collision.Enabled = true;
        debris.Collision.Bounce = 0.3f;
        debris.Collision.Dampen = 0.25f;

        debris.ColorOverLifetime.Enabled = true;
        debris.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 1f), (0.85f, 1f), (1f, 0f)]));

        debris.Renderer.RenderMode = ParticleRenderMode.Mesh;
        debris.Renderer.Mesh = Mesh.CreateCube(Float3.One);
        debris.Renderer.Alignment = ParticleRenderAlignment.World;
        debris.Renderer.Lit = true;
    }

    private void BuildShapes(Float3 c)
    {
        AddStation("Shapes", "Every emission shape: sphere, hemisphere, cone volume, box edges, 270 degree circle, donut, edge, rectangle and mesh triangles.", c, new Float3(0f, 2.5f, -13f), 2f);

        (ParticleShapeType type, Color color)[] shapes =
        [
            (ParticleShapeType.Sphere, new Color(2.5f, 0.6f, 0.6f, 1f)),
            (ParticleShapeType.Hemisphere, new Color(2.5f, 1.5f, 0.4f, 1f)),
            (ParticleShapeType.Cone, new Color(2.2f, 2.4f, 0.4f, 1f)),
            (ParticleShapeType.Box, new Color(0.5f, 2.5f, 0.6f, 1f)),
            (ParticleShapeType.Circle, new Color(0.4f, 2.3f, 2.3f, 1f)),
            (ParticleShapeType.Donut, new Color(0.5f, 1.2f, 2.8f, 1f)),
            (ParticleShapeType.Edge, new Color(1.3f, 0.6f, 2.8f, 1f)),
            (ParticleShapeType.Rectangle, new Color(2.6f, 0.6f, 2.2f, 1f)),
            (ParticleShapeType.Mesh, new Color(2.4f, 2.4f, 2.4f, 1f)),
        ];

        Mesh sphere = Mesh.CreateSphere(0.8f, 10, 14);
        for (int i = 0; i < shapes.Length; i++)
        {
            var ps = CreateSystem($"Shape {shapes[i].type}", c + new Float3((i - (shapes.Length - 1) * 0.5f) * 2.3f, 2f, 0f), _dotAdditive);
            ps.Initial.StartLifetime = new MinMaxCurve(1.2f);
            ps.Initial.StartSpeed = new MinMaxCurve(0.08f);
            ps.Initial.StartSize = new MinMaxCurve(0.09f);
            ps.Initial.StartColor = new MinMaxGradient(shapes[i].color);
            ps.Emission.RateOverTime = new MinMaxCurve(140f);
            ps.Shape.Type = shapes[i].type;
            ps.Shape.Radius = 0.7f;
            ps.Shape.Angle = 30f;
            ps.Shape.Arc = shapes[i].type == ParticleShapeType.Circle ? 270f : 360f;
            ps.Shape.RadiusThickness = shapes[i].type == ParticleShapeType.Sphere ? 0f : 1f;
            ps.Shape.ConeEmitFrom = ConeEmitFrom.Volume;
            ps.Shape.Length = 1f;
            ps.Shape.BoxEmitFrom = BoxEmitFrom.Edge;
            ps.Shape.Scale = shapes[i].type == ParticleShapeType.Rectangle ? new Float3(1.4f, 1f, 1.4f) : Float3.One;
            ps.Shape.Mesh = sphere;
            // Tip the flat shapes up so they face the camera instead of lying edge on.
            if (shapes[i].type is ParticleShapeType.Circle or ParticleShapeType.Donut or ParticleShapeType.Rectangle or ParticleShapeType.Hemisphere or ParticleShapeType.Cone)
                ps.Shape.Rotation = new Float3(-70f, 0f, 0f);
            ps.ColorOverLifetime.Enabled = true;
            ps.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 1f), (1f, 0f)]));
            ps.Renderer.BlendMode = ParticleBlendMode.Additive;
        }
    }

    private void BuildFlipbooks(Float3 c)
    {
        AddStation("Flipbooks", "A 4x4 sheet played three ways: stepped frames, blended frames, and single rows at a fixed FPS with random mirroring. Ripples on the ground are horizontal billboards.", c, new Float3(0f, 4f, -10f), 1.5f);

        ParticleSystemComponent Make(string name, float x)
        {
            var ps = CreateSystem(name, c + new Float3(x, 1f, 0f), _flipbook);
            ps.Initial.StartLifetime = new MinMaxCurve(3f);
            ps.Initial.StartSpeed = new MinMaxCurve(0.5f);
            ps.Initial.StartSize = new MinMaxCurve(1.2f);
            ps.Emission.RateOverTime = new MinMaxCurve(1.5f);
            ps.Shape.Enabled = false;
            ps.ColorOverLifetime.Enabled = true;
            ps.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 0f), (0.1f, 1f), (0.85f, 1f), (1f, 0f)]));
            ps.TextureSheet.Enabled = true;
            ps.TextureSheet.TilesX = 4;
            ps.TextureSheet.TilesY = 4;
            return ps;
        }

        Make("Stepped", -4f);

        var blended = Make("Blended", 0f);
        blended.TextureSheet.FrameBlending = true;

        var rows = Make("Rows", 4f);
        rows.TextureSheet.Animation = TextureSheetAnimation.SingleRow;
        rows.TextureSheet.RandomRow = true;
        rows.TextureSheet.TimeMode = TextureSheetTimeMode.FPS;
        rows.TextureSheet.FPS = 6f;
        rows.Renderer.FlipU = 0.5f;

        var ripples = CreateSystem("Ripples", c + new Float3(0f, 0.03f, 3f), _ring);
        ripples.Initial.StartLifetime = new MinMaxCurve(2f);
        ripples.Initial.StartSpeed = new MinMaxCurve(0f);
        ripples.Initial.StartSize = new MinMaxCurve(2.5f);
        ripples.Initial.StartColor = new MinMaxGradient(new Color(0.5f, 1.4f, 2.5f, 1f));
        ripples.Emission.RateOverTime = new MinMaxCurve(3f);
        ripples.Shape.Type = ParticleShapeType.Rectangle;
        ripples.Shape.Scale = new Float3(8f, 1f, 3f);
        ripples.SizeOverLifetime.Enabled = true;
        ripples.SizeOverLifetime.Size = new MinMaxCurve(Curve((0f, 0f), (1f, 1f)));
        ripples.ColorOverLifetime.Enabled = true;
        ripples.ColorOverLifetime.Color = new MinMaxGradient(Grad([(0f, Color.White), (1f, Color.White)], [(0f, 1f), (1f, 0f)]));
        ripples.Renderer.RenderMode = ParticleRenderMode.HorizontalBillboard;
        ripples.Renderer.BlendMode = ParticleBlendMode.Additive;
    }

    /// <summary>A particle system on its own GameObject. Root systems are added to the scene once every station is built.</summary>
    private ParticleSystemComponent CreateSystem(string name, Float3 position, Material material, GameObject? parent = null)
    {
        var go = new GameObject(name);
        go.Transform.Position = position;
        var ps = go.AddComponent<ParticleSystemComponent>();
        ps.Renderer.Material = material;
        ps.Initial.StartColor = new MinMaxGradient(Color.White);

        if (parent != null)
        {
            go.SetParent(parent);
        }
        else
        {
            _roots.Add(go);
            _systems.Add(ps);
        }
        return ps;
    }

    // ----------------------------------------------------------------
    //  Input
    // ----------------------------------------------------------------

    protected override void Tick()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            foreach (ParticleSystemComponent system in _systems)
            {
                system.Stop(true, ParticleStopBehavior.StopEmittingAndClear);
                system.Play();
            }
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            bool anyPlaying = _systems.Any(s => s.IsPlaying);
            foreach (ParticleSystemComponent system in _systems)
            {
                if (anyPlaying) system.Pause();
                else system.Play();
            }
        }
    }
}

/// <summary>Flies its GameObject around a horizontal circle, for the simulation space station.</summary>
public sealed class Orbiter : MonoBehaviour
{
    public Float3 Center;
    public float Radius = 3f;
    public float Speed = 1.6f;

    private float _angle;

    public override void Update()
    {
        _angle += Speed * Time.DeltaTime;
        Transform.Position = Center + new Float3(MathF.Cos(_angle) * Radius, MathF.Sin(_angle * 2f) * 0.8f, MathF.Sin(_angle) * Radius);
    }
}
