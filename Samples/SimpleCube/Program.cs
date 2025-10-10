// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Vector;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using Silk.NET.Input;

namespace SimpleCube;

internal class Program
{
    static void Main(string[] args)
    {
        new MyGame().Run("Demo", 1280, 720);
    }
}

public sealed class MyGame : Game
{
    private GameObject cameraGO;
    private Scene scene;

    public override void Initialize()
    {
        scene = new Scene();

        // Create directional light
        GameObject lightGO = new("Directional Light");
        var light = lightGO.AddComponent<DirectionalLight>();
        lightGO.Transform.localEulerAngles = new Double3(-80, 5, 0);
        scene.Add(lightGO);

        // Create camera
        cameraGO = new("Main Camera");
        cameraGO.tag = "Main Camera";
        cameraGO.Transform.position = new(0, 0, -10);
        var camera = cameraGO.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;

        camera.Effects = new List<ImageEffect>()
        {
            new ScreenSpaceReflectionEffect(),
            new KawaseBloomEffect(),
            new BokehDepthOfFieldEffect(),
            new TonemapperEffect(),
        };

        scene.Add(cameraGO);

        Mesh cube = Mesh.CreateCube(Double3.One);
        Material mat = new Material(Shader.LoadDefault(DefaultShader.Standard));

        GameObject cubeGO = new GameObject("Cube");
        var mr = cubeGO.AddComponent<MeshRenderer>();
        mr.Mesh = cube;
        mr.Material = mat;

        scene.Add(cubeGO);
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

        Double2 movement = Double2.Zero;
        if (Input.GetKey(Key.W)) movement += Double2.UnitY;
        if (Input.GetKey(Key.S)) movement -= Double2.UnitY;
        if (Input.GetKey(Key.A)) movement -= Double2.UnitX;
        if (Input.GetKey(Key.D)) movement += Double2.UnitX;

        // forward/back
        cameraGO.Transform.position += cameraGO.Transform.forward * movement.Y * 5f * Time.deltaTime;
        // left/right
        cameraGO.Transform.position += cameraGO.Transform.right * movement.X * 5f * Time.deltaTime;

        float upDown = 0;
        if (Input.GetKey(Key.E)) upDown += 1;
        if (Input.GetKey(Key.Q)) upDown -= 1;
        // up/down
        cameraGO.Transform.position += Double3.UnitY * upDown * 5f * Time.deltaTime;

        // rotate with mouse
        if (Input.GetMouseButton(1))
        {
            Double2 delta = Input.MouseDelta;
            cameraGO.Transform.localEulerAngles += new Double3(delta.Y, delta.X, 0) * 0.1f;
        }
    }
}
