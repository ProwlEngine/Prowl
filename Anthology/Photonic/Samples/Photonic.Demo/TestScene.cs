using System.Collections.Generic;
using Prowl.Vector;

namespace Photonic.Demo;

/// <summary>
/// Deterministic procedural stress scene: one big floor plus a pile of separately-generated shapes.
/// Every entry is its own <see cref="LoadedModel"/>, so each one is a separate draw call and a
/// separate atlas placement, which is what pushes the auto packer into opening multiple pages.
/// </summary>
internal static class TestScene
{
    public const float FloorSize = 46f;

    private static readonly Float3[] Palette =
    {
        new Float3(0.82f, 0.28f, 0.24f),
        new Float3(0.24f, 0.55f, 0.82f),
        new Float3(0.90f, 0.72f, 0.22f),
        new Float3(0.30f, 0.72f, 0.40f),
        new Float3(0.72f, 0.34f, 0.74f),
        new Float3(0.86f, 0.86f, 0.84f),
        new Float3(0.20f, 0.68f, 0.66f),
        new Float3(0.92f, 0.50f, 0.20f),
    };

    public static (List<SceneModel> Models, List<SceneLight> Lights) Build(int seed = 1337)
    {
        var rng = new RNG((ulong)seed);
        var models = new List<SceneModel>();

        void Add(LoadedModel source, Float3 position, float yawDeg = 0f, float pitchDeg = 0f, float rollDeg = 0f)
            => models.Add(new SceneModel
            {
                Name = source.DisplayName,
                Source = source,
                UV1Mode = UV1Strategy.UseExisting,
                Position = new System.Numerics.Vector3(position.X, position.Y, position.Z),
                RotationEulerDeg = new System.Numerics.Vector3(pitchDeg, yawDeg, rollDeg),
            });

        var checker = ProceduralShapes.Checker("floor_checker", 512, 8,
            new Float3(0.78f, 0.78f, 0.76f), new Float3(0.30f, 0.31f, 0.33f));
        Add(ProceduralShapes.Ground("Floor", FloorSize, FloorSize, 12, new Float3(0.72f, 0.72f, 0.70f), checker, 4f),
            Float3.Zero);

        // ---- landmarks --------------------------------------------------------------------------
        Add(ProceduralShapes.Room("Room", 9f, 7f, 3.4f, 0.25f, new Float3(0.78f, 0.74f, 0.68f), new Float3(0.55f, 0.56f, 0.60f)),
            new Float3(-14f, 0f, 12f), yawDeg: 14f);
        Add(ProceduralShapes.Stairs("Stairs", 9, 3.2f, 0.28f, 0.42f, new Float3(0.66f, 0.64f, 0.62f)),
            new Float3(12f, 0f, 9f), yawDeg: -22f);
        Add(ProceduralShapes.Terrain("TerrainPatch", 14f, 44, 1.7f, 5, new Float3(0.44f, 0.52f, 0.32f)),
            new Float3(15f, 0.02f, -13f));
        Add(ProceduralShapes.Arch("Arch", 2.6f, 2.2f, 0.35f, new Float3(0.80f, 0.78f, 0.70f)),
            new Float3(0f, 0f, 15f), yawDeg: 90f);
        Add(ProceduralShapes.Pyramid("Pyramid", 3.6f, 3.2f, new Float3(0.86f, 0.74f, 0.42f)),
            new Float3(10f, 0f, -3f), yawDeg: 18f);
        Add(ProceduralShapes.Wedge("Ramp", 3.2f, 1.8f, 2.8f, new Float3(0.55f, 0.58f, 0.64f)),
            new Float3(-9f, 0f, 1f), yawDeg: -35f);

        // ---- trees ------------------------------------------------------------------------------
        Add(ProceduralShapes.Tree("TreeA", 7, 6.8f, new Float3(0.34f, 0.23f, 0.15f), new Float3(0.18f, 0.44f, 0.16f)),
            new Float3(-8f, 0f, -12f));
        Add(ProceduralShapes.Tree("TreeB", 21, 5.4f, new Float3(0.30f, 0.21f, 0.14f), new Float3(0.26f, 0.48f, 0.20f)),
            new Float3(-14f, 0f, -6f), yawDeg: 40f);

        // ---- curvy things -----------------------------------------------------------------------
        Add(ProceduralShapes.Torus("TorusUpright", 2.4f, 0.5f, 48, 20, Palette[1]),
            new Float3(0f, 2.5f, -7f), pitchDeg: 90f);
        Add(ProceduralShapes.Torus("TorusFlat", 1.3f, 0.35f, 40, 16, Palette[3]),
            new Float3(5f, 0.36f, 3f));
        Add(ProceduralShapes.TorusKnot("TorusKnot", 2.0f, 0.32f, 2, 3, Palette[4]),
            new Float3(-4f, 3.2f, 5f));
        Add(ProceduralShapes.Cylinder("Column", 0.9f, 4.2f, 28, new Float3(0.80f, 0.79f, 0.75f)),
            new Float3(-2f, 0f, -11f));
        Add(ProceduralShapes.Cone("Cone", 1.2f, 3.0f, 30, Palette[7]),
            new Float3(7f, 0f, 6f));
        Add(ProceduralShapes.Capsule("Capsule", 0.65f, 1.9f, Palette[6]),
            new Float3(3f, 0.66f, -9f), rollDeg: 68f);
        Add(ProceduralShapes.Quad("BounceCard", 4.5f, 3.2f, new Float3(0.92f, 0.30f, 0.22f)),
            new Float3(-6f, 2.4f, -2f), yawDeg: 34f);

        // ---- scattered spheres, some deliberately overlapping ------------------------------------
        var lastSphere = new Float3(0f, 0f, 0f);
        float lastRadius = 0f;
        for (int i = 0; i < 12; i++)
        {
            float radius = rng.Range(0.45f, 2.1f);
            Float3 position;
            if (i > 0 && i % 3 == 0)
            {
                var dir = Float3.Normalize(new Float3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)));
                position = lastSphere + dir * ((lastRadius + radius) * 0.72f);
                position.Y = radius;
            }
            else
            {
                position = ScatterPoint(rng, radius);
            }
            Add(ProceduralShapes.Sphere($"Sphere{i}", radius, 40, 22, Palette[i % Palette.Length]), position);
            lastSphere = position;
            lastRadius = radius;
        }

        // ---- scattered boxes ---------------------------------------------------------------------
        for (int i = 0; i < 10; i++)
        {
            var size = new Float3(rng.Range(0.7f, 2.8f), rng.Range(0.6f, 3.2f), rng.Range(0.7f, 2.8f));
            var position = ScatterPoint(rng, size.Y * 0.5f);
            if (i % 4 == 0) position.Y = size.Y * 0.5f + rng.Range(0.5f, 2.5f); // a few floating occluders
            Add(ProceduralShapes.Box($"Box{i}", size, Palette[(i + 3) % Palette.Length]),
                position, yawDeg: rng.Range(0f, 90f), pitchDeg: i % 5 == 0 ? rng.Range(-25f, 25f) : 0f);
        }

        // ---- blobs -------------------------------------------------------------------------------
        for (int i = 0; i < 3; i++)
        {
            float radius = rng.Range(1.1f, 2.0f);
            Add(ProceduralShapes.Blob($"Blob{i}", radius, 0.30f, 11 + i * 4, Palette[(i * 2 + 1) % Palette.Length]),
                ScatterPoint(rng, radius * 0.9f));
        }

        var lights = new List<SceneLight>
        {
            new SceneLight
            {
                Name = "Sun",
                Kind = SceneLightKind.Directional,
                Direction = new System.Numerics.Vector3(-0.45f, -1f, -0.35f),
                Color = new System.Numerics.Vector3(3.2f, 3.0f, 2.7f),
            },
            new SceneLight
            {
                Name = "RoomLamp",
                Kind = SceneLightKind.Point,
                Position = new System.Numerics.Vector3(-14f, 2.4f, 12f),
                Color = new System.Numerics.Vector3(16f, 8f, 3.5f),
                Range = 14f,
            },
            new SceneLight
            {
                Name = "CyanFill",
                Kind = SceneLightKind.Point,
                Position = new System.Numerics.Vector3(6f, 3.5f, 4f),
                Color = new System.Numerics.Vector3(2f, 7f, 9f),
                Range = 18f,
            },
            new SceneLight
            {
                Name = "MagentaFill",
                Kind = SceneLightKind.Point,
                Position = new System.Numerics.Vector3(-6f, 1.6f, -8f),
                Color = new System.Numerics.Vector3(8f, 2f, 7f),
                Range = 15f,
            },
            new SceneLight
            {
                Name = "StairSpot",
                Kind = SceneLightKind.Spot,
                Position = new System.Numerics.Vector3(12f, 7f, 9f),
                Direction = new System.Numerics.Vector3(0f, -1f, -0.15f),
                Color = new System.Numerics.Vector3(14f, 13f, 11f),
                Range = 22f,
                ConeAngleDeg = 34f,
            },
        };

        return (models, lights);
    }

    /// <summary>Random spot on the floor, kept out of the footprints of the room, terrain and stairs.</summary>
    private static Float3 ScatterPoint(RNG rng, float restHeight)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float x = rng.Range(-17f, 17f);
            float z = rng.Range(-17f, 17f);
            if (Near(x, z, -14f, 12f, 8f)) continue;  // room
            if (Near(x, z, 15f, -13f, 9f)) continue;  // terrain
            if (Near(x, z, 12f, 9f, 5f)) continue;    // stairs
            return new Float3(x, restHeight, z);
        }
        return new Float3(rng.Range(-6f, 6f), restHeight, rng.Range(-6f, 6f));
    }

    private static bool Near(float x, float z, float cx, float cz, float radius)
    {
        float dx = x - cx, dz = z - cz;
        return dx * dx + dz * dz < radius * radius;
    }
}
