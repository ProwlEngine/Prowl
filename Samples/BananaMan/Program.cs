// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// BananaMan Animation Demo
// Controls:
//   Movement:  WASD / Arrow Keys
//   Look:      Mouse Move (when RMB held)
//   Fly Up:    E
//   Fly Down:  Q
//   Sprint:    Left Shift
//

using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using MouseButton = Prowl.Runtime.MouseButton;

namespace BananaMan;

internal class Program
{
    static void Main(string[] args)
    {
        new MyGame().Run("BananaMan Animation Demo", 1280, 720);
    }
}

public sealed class MyGame : Game
{
    private GameObject? cameraGO;
    private GameObject? bananaManGO;
    private Scene? scene;

    // Input Actions
    private InputActionMap cameraMap = null!;
    private InputAction moveAction = null!;
    private InputAction lookAction = null!;
    private InputAction lookEnableAction = null!;
    private InputAction flyUpAction = null!;
    private InputAction flyDownAction = null!;
    private InputAction sprintAction = null!;

    public override void Initialize()
    {
        DrawGizmos = true;
        scene = new Scene();
        SetupInputActions();

        // Create directional light
        GameObject lightGO = new("Directional Light");
        DirectionalLight light = lightGO.AddComponent<DirectionalLight>();
        light.Color = Color.White;
        lightGO.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        scene.Add(lightGO);

        // Create camera
        cameraGO = new("Main Camera");
        cameraGO.Tag = "Main Camera";
        cameraGO.Transform.Position = new(0, 1.5f, -5);
        Camera camera = cameraGO.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;
        camera.Effects =
        [
            new FXAAEffect(),
            new KawaseBloomEffect(),
            new TonemapperEffect(),
        ];
        scene.Add(cameraGO);

        // Create ground plane
        GameObject groundGO = new("Ground");
        MeshRenderer mr = groundGO.AddComponent<MeshRenderer>();
        mr.Mesh = Mesh.CreateCube(Float3.One);
        mr.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        mr.Material.SetColor("_MainColor", new Color(0.5f, 0.5f, 0.5f, 1.0f));
        groundGO.Transform.Position = new(0, -1, 0);
        groundGO.Transform.LocalScale = new(20, 0.1f, 20);
        scene.Add(groundGO);

        // Load and create BananaMan
        CreateBananaMan();

        Input.SetCursorVisible(false);
        Scene.Load(scene);
    }

    private void CreateBananaMan()
    {
        // Try to load BananaMan.fbx
        // Note: User needs to place BananaMan.fbx in the project directory
        //string fbxPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Banana Man", "scene.gltf");
        string fbxPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Banana Man", "Dancing Twerk.fbx");

        if (!System.IO.File.Exists(fbxPath))
        {
            Debug.LogError($"BananaMan.fbx not found at: {fbxPath}");
            Debug.LogError("Please place BananaMan.fbx in the project directory.");
            return;
        }

        // Import the model
        ModelImporter importer = new ModelImporter();
        Model? model = importer.Import(new System.IO.FileInfo(fbxPath));
        if (model.IsNotValid())
        {
            Debug.LogError("Failed to import BananaMan.fbx");
            return;
        }

        Debug.Log($"Loaded model: {model.Name}");
        Debug.Log($"Animations found: {model.Animations.Count}");

        // Create GameObject for BananaMan
        bananaManGO = new("BananaMan");
        bananaManGO.Transform.Position = new Float3(0, 0, 0);
        bananaManGO.Transform.LocalScale = new Float3(0.01f, 0.01f, 0.01f); // Models are often large

        // Add AnimatedModelRenderer
        ModelRenderer renderer = bananaManGO.AddComponent<ModelRenderer>();
        renderer.Model = model;

        // Set up skeleton and animation if available
        if (model.Animations.Count > 0)
        {
            AnimationClip firstAnim = model.Animations[0];
            //renderer.Skeleton = firstAnim.Skeleton;
            renderer.CurrentAnimation = firstAnim;
            renderer.PlayAutomatically = true;
            renderer.Loop = true;
            renderer.AnimationSpeed = 1.0f;

            Debug.Log($"Playing animation: {firstAnim.Name ?? "Unnamed"}");
            Debug.Log($"Duration: {firstAnim.Duration:F2}s");
        }
        else
        {
            Debug.LogWarning("No animations found in BananaMan.fbx - will display static model");
        }

        scene.Add(bananaManGO);
    }

    private void SetupInputActions()
    {
        cameraMap = new InputActionMap("Camera");

        // Movement (WASD)
        moveAction = cameraMap.AddAction("Move", InputActionType.Value);
        moveAction.ExpectedValueType = typeof(Float2);
        moveAction.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W),
            InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A),
            InputBinding.CreateKeyBinding(KeyCode.D),
            true
        ));

        // Look enable (RMB)
        lookEnableAction = cameraMap.AddAction("LookEnable", InputActionType.Button);
        lookEnableAction.AddBinding(MouseButton.Right);

        // Look (Mouse)
        lookAction = cameraMap.AddAction("Look", InputActionType.Value);
        lookAction.ExpectedValueType = typeof(Float2);
        var mouse = new DualAxisCompositeBinding(
            InputBinding.CreateMouseAxisBinding(0),
            InputBinding.CreateMouseAxisBinding(1));
        mouse.Processors.Add(new ScaleProcessor(0.1f));
        lookAction.AddBinding(mouse);

        // Fly Up/Down
        flyUpAction = cameraMap.AddAction("FlyUp", InputActionType.Button);
        flyUpAction.AddBinding(KeyCode.E);
        flyDownAction = cameraMap.AddAction("FlyDown", InputActionType.Button);
        flyDownAction.AddBinding(KeyCode.Q);

        // Sprint
        sprintAction = cameraMap.AddAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding(KeyCode.ShiftLeft);

        Input.RegisterActionMap(cameraMap);
        cameraMap.Enable();
    }

    public override void BeginUpdate()
    {
        // Camera controls
        Float2 movement = moveAction.ReadValue<Float2>();
        float speedMultiplier = sprintAction.IsPressed() ? 2.5f : 1.0f;
        float moveSpeed = 5f * speedMultiplier * (float)Time.DeltaTime;

        cameraGO.Transform.Position += cameraGO.Transform.Forward * movement.Y * moveSpeed;
        cameraGO.Transform.Position += cameraGO.Transform.Right * movement.X * moveSpeed;

        float upDown = 0;
        if (flyUpAction.IsPressed()) upDown += 1;
        if (flyDownAction.IsPressed()) upDown -= 1;
        cameraGO.Transform.Position += Float3.UnitY * upDown * moveSpeed;

        Float2 lookInput = lookAction.ReadValue<Float2>();
        if (lookEnableAction.IsPressed())
        {
            cameraGO.Transform.LocalEulerAngles += new Float3(lookInput.Y, lookInput.X, 0);
        }

        // Slowly rotate BananaMan for better viewing
        if (bananaManGO != null)
        {
            //bananaManGO.Transform.LocalEulerAngles += new Float3(0, 30 * (float)Time.DeltaTime, 0);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            Input.SetCursorVisible(true);
    }
}
