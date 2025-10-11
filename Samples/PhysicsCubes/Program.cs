// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using Silk.NET.Input;

namespace PhysicsCubes;

internal class Program
{
    static void Main(string[] args)
    {
        new PhysicsDemo().Run("Physics Demo", 1280, 720);
    }
}

public sealed class PhysicsDemo : Game
{
    private GameObject cameraGO;
    private Scene scene;

    public override void Initialize()
    {
        scene = new Scene();

        // Create directional light
        GameObject lightGO = new("Directional Light");
        var light = lightGO.AddComponent<DirectionalLight>();
        lightGO.Transform.localEulerAngles = new Double3(-45, 45, 0);
        scene.Add(lightGO);

        // Create camera
        GameObject cam = new("Main Camera");
        cam.tag = "Main Camera";
        cam.Transform.position = new(0, 5, -15);
        cam.Transform.localEulerAngles = new Double3(15, 0, 0);
        var camera = cam.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;
        cameraGO = cam;

        camera.Effects = new List<ImageEffect>()
        {
            new ScreenSpaceReflectionEffect(),
            new KawaseBloomEffect(),
            new BokehDepthOfFieldEffect(),
            new TonemapperEffect(),
        };

        scene.Add(cam);

        // Create materials
        Material floorMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        floorMaterial.SetColor("_MainColor", new Float4(0.8f, 0.8f, 0.8f, 1.0f));

        Material cubeMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        cubeMaterial.SetColor("_MainColor", new Float4(0.2f, 0.5f, 1.0f, 1.0f));

        // Create floor (static)
        GameObject floor = new GameObject("Floor");
        var floorRenderer = floor.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = Mesh.CreateCube(new Double3(20, 1, 20));
        floorRenderer.Material = floorMaterial;
        floor.Transform.position = new Double3(0, -0.5f, 0);

        // Add static rigidbody for floor
        var floorRigidbody = floor.AddComponent<Rigidbody3D>();
        floorRigidbody.IsStatic = true;
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Double3(20, 1, 20);

        scene.Add(floor);

        // Create falling cubes
        System.Random random = new System.Random();
        for (int i = 0; i < 10; i++)
        {
            GameObject cube = new GameObject($"Cube {i}");
            var cubeRenderer = cube.AddComponent<MeshRenderer>();
            cubeRenderer.Mesh = Mesh.CreateCube(Double3.One);
            cubeRenderer.Material = cubeMaterial;

            // Random position above the floor
            float x = (float)(random.NextDouble() * 10 - 5);
            float y = 5 + i * 2;
            float z = (float)(random.NextDouble() * 10 - 5);
            cube.Transform.position = new Double3(x, y, z);

            // Random rotation
            cube.Transform.localEulerAngles = new Double3(
                (float)(random.NextDouble() * 360),
                (float)(random.NextDouble() * 360),
                (float)(random.NextDouble() * 360)
            );

            // Add dynamic rigidbody
            var rigidbody = cube.AddComponent<Rigidbody3D>();
            rigidbody.IsStatic = false;
            rigidbody.Mass = 1.0f;

            var collider = cube.AddComponent<BoxCollider>();
            collider.Size = Double3.One;

            scene.Add(cube);
        }
    }

    public override void FixedUpdate()
    {
        scene.FixedUpdate();
    }

    public override void Render()
    {
        scene.RenderScene();
    }

    public override void Update()
    {
        scene.Update();

        // Camera movement
        Double2 movement = Double2.Zero;
        if (Input.GetKey(Key.W)) movement += Double2.UnitY;
        if (Input.GetKey(Key.S)) movement -= Double2.UnitY;
        if (Input.GetKey(Key.A)) movement -= Double2.UnitX;
        if (Input.GetKey(Key.D)) movement += Double2.UnitX;

        // forward/back
        cameraGO.Transform.position += cameraGO.Transform.forward * movement.Y * 10f * Time.deltaTime;
        // left/right
        cameraGO.Transform.position += cameraGO.Transform.right * movement.X * 10f * Time.deltaTime;

        // up/down
        float upDown = 0;
        if (Input.GetKey(Key.E)) upDown += 1;
        if (Input.GetKey(Key.Q)) upDown -= 1;
        cameraGO.Transform.position += Double3.UnitY * upDown * 10f * Time.deltaTime;

        // rotate with mouse
        if (Input.GetMouseButton(1))
        {
            Double2 delta = Input.MouseDelta;
            cameraGO.Transform.localEulerAngles += new Double3(delta.Y, delta.X, 0) * 0.1f;
        }

        // Reset scene with R key
        if (Input.GetKeyDown(Key.R))
        {
            // Clear and reinitialize
            scene.Clear();
            Initialize();
        }
    }
}
