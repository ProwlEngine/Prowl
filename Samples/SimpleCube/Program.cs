// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using Silk.NET.Input;

namespace SimpleCube;

internal class Program
{
    static void Main(string[] args)
    {
        new MyGame().Run("Demo", 1280, 720, new BasicAssetProvider());
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
        lightGO.Transform.localEulerAngles = new Vector3(-80, 5, 0);
        scene.Add(lightGO);

        // Create camera
        GameObject cam = new("Main Camera");
        cam.tag = "Main Camera";
        cam.Transform.position = new(0, 0, -10);
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

        Mesh cube = Mesh.CreateCube(Vector3.one);
        Material mat = new Material(Shader.Find("$Assets/Defaults/Standard.shader"));

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


        Vector2 movement = Vector2.zero;
        if (Input.GetKey(Key.W)) movement += Vector2.up;
        if (Input.GetKey(Key.S)) movement += Vector2.down;
        if (Input.GetKey(Key.A)) movement += Vector2.left;
        if (Input.GetKey(Key.D)) movement += Vector2.right;

        // forward/back
        cameraGO.Transform.position += cameraGO.Transform.forward * movement.y * 5f * Time.deltaTime;
        // left/right
        cameraGO.Transform.position += cameraGO.Transform.right * movement.x * 5f * Time.deltaTime;

        float upDown = 0;
        if (Input.GetKey(Key.E)) upDown += 1;
        if (Input.GetKey(Key.Q)) upDown -= 1;
        // up/down
        cameraGO.Transform.position += Vector3.up * upDown * 5f * Time.deltaTime;

        // rotate with mouse
        if (Input.GetMouseButton(1))
        {
            Vector2 delta = Input.MouseDelta;
            cameraGO.Transform.localEulerAngles += new Vector3(delta.y, delta.x, 0) * 0.1f;
        }

    }
}
