// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
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
        lightGO.Transform.localEulerAngles = new Vector3(-45, 45, 0);
        scene.Add(lightGO);

        // Create camera
        GameObject cam = new("Main Camera");
        cam.tag = "Main Camera";
        cam.Transform.position = new(0, 5, -15);
        cam.Transform.localEulerAngles = new Vector3(15, 0, 0);
        var camera = cam.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;
        cameraGO = cam;

        camera.Effects = new List<ImageEffect>()
        {
            new TonemapperEffect(),
        };

        scene.Add(cam);

        // Create materials
        Material floorMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        floorMaterial.SetColor("_MainColor", new Color(0.8f, 0.8f, 0.8f, 1.0f));

        Material cubeMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));
        cubeMaterial.SetColor("_MainColor", new Color(0.2f, 0.5f, 1.0f, 1.0f));

        // Create floor (static)
        GameObject floor = new GameObject("Floor");
        var floorRenderer = floor.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = Mesh.CreateCube(new Vector3(20, 1, 20));
        floorRenderer.Material = floorMaterial;
        floor.Transform.position = new Vector3(0, -0.5f, 0);

        // Add static rigidbody for floor
        var floorRigidbody = floor.AddComponent<Rigidbody3D>();
        floorRigidbody.IsStatic = true;
        var floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Vector3(20, 1, 20);

        scene.Add(floor);

        // Create falling cubes
        System.Random random = new System.Random();
        for (int i = 0; i < 10; i++)
        {
            GameObject cube = new GameObject($"Cube {i}");
            var cubeRenderer = cube.AddComponent<MeshRenderer>();
            cubeRenderer.Mesh = Mesh.CreateCube(Vector3.one);
            cubeRenderer.Material = cubeMaterial;

            // Random position above the floor
            float x = (float)(random.NextDouble() * 10 - 5);
            float y = 5 + i * 2;
            float z = (float)(random.NextDouble() * 10 - 5);
            cube.Transform.position = new Vector3(x, y, z);

            // Random rotation
            cube.Transform.localEulerAngles = new Vector3(
                (float)(random.NextDouble() * 360),
                (float)(random.NextDouble() * 360),
                (float)(random.NextDouble() * 360)
            );

            // Add dynamic rigidbody
            var rigidbody = cube.AddComponent<Rigidbody3D>();
            rigidbody.IsStatic = false;
            rigidbody.Mass = 1.0f;

            var collider = cube.AddComponent<BoxCollider>();
            collider.Size = Vector3.one;

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
        Vector2 movement = Vector2.zero;
        if (Input.GetKey(Key.W)) movement += Vector2.up;
        if (Input.GetKey(Key.S)) movement += Vector2.down;
        if (Input.GetKey(Key.A)) movement += Vector2.left;
        if (Input.GetKey(Key.D)) movement += Vector2.right;

        // forward/back
        cameraGO.Transform.position += cameraGO.Transform.forward * movement.y * 10f * Time.deltaTime;
        // left/right
        cameraGO.Transform.position += cameraGO.Transform.right * movement.x * 10f * Time.deltaTime;

        // up/down
        float upDown = 0;
        if (Input.GetKey(Key.E)) upDown += 1;
        if (Input.GetKey(Key.Q)) upDown -= 1;
        cameraGO.Transform.position += Vector3.up * upDown * 10f * Time.deltaTime;

        // rotate with mouse
        if (Input.GetMouseButton(1))
        {
            Vector2 delta = Input.MouseDelta;
            cameraGO.Transform.localEulerAngles += new Vector3(delta.y, delta.x, 0) * 0.1f;
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
