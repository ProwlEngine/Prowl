// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// SimpleCube Demo - Showcasing Prowl's new Input Action System
//
// This demo demonstrates:
// - Action-based input with multiple input sources
// - Full keyboard + mouse + gamepad support
// - Input processing (deadzone, normalization, scaling)
// - Seamless switching between input methods
//
// Controls:
//   Movement:  WASD / Arrow Keys / Gamepad Left Stick
//   Look:      Right Mouse + Drag / Gamepad Right Stick
//   Fly Up:    E / Gamepad A Button
//   Fly Down:  Q / Gamepad B Button
//   Sprint:    Left Shift / Gamepad Left Stick Click
//

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
    private ModelRenderer model;

    // Input Actions
    private InputActionMap cameraMap = null!;
    private InputAction moveAction = null!;
    private InputAction lookAction = null!;
    private InputAction flyUpAction = null!;
    private InputAction flyDownAction = null!;
    private InputAction sprintAction = null!;

    public override void Initialize()
    {
        scene = new Scene();

        // Setup Input Actions
        SetupInputActions();

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
            //new ScreenSpaceReflectionEffect(),
            //new KawaseBloomEffect(),
            //new BokehDepthOfFieldEffect(),
            new TonemapperEffect(),
        };

        scene.Add(cameraGO);

        Mesh cube = Mesh.CreateCube(Double3.One);
        Material mat = new Material(Shader.LoadDefault(DefaultShader.Standard));

        GameObject cubeGO = new GameObject("Cube");
        var mr = cubeGO.AddComponent<MeshRenderer>();
        mr.Mesh = cube;
        mr.Material = mat;

        cubeGO.Transform.position = new(0, -1, 0);
        cubeGO.Transform.localScale = new(10, 1, 10);

        scene.Add(cubeGO);

        //var m = Model.LoadFromFile("Banana Man\\scene.gltf");
        var m = Model.LoadFromFile("glTF-Sponza\\Sponza.gltf");
        model = new GameObject("Model").AddComponent<ModelRenderer>();
        model.Model = m;
        scene.Add(model.GameObject);

        Input.SetCursorVisible(false);
    }

    private void SetupInputActions()
    {
        // Create input action map
        cameraMap = new InputActionMap("Camera");

        // Movement action
        {
            moveAction = cameraMap.AddAction("Move", InputActionType.Value);
            moveAction.ExpectedValueType = typeof(Float2);

            // Add WASD composite
            var wasdComp = new Vector2CompositeBinding(
                InputBinding.CreateKeyBinding(KeyCode.W),
                InputBinding.CreateKeyBinding(KeyCode.S),
                InputBinding.CreateKeyBinding(KeyCode.A),
                InputBinding.CreateKeyBinding(KeyCode.D),
                true // normalize
            );
            moveAction.AddBinding(wasdComp);

            // Also add gamepad left stick with deadzone
            var leftStick = InputBinding.CreateGamepadAxisBinding(0, deviceIndex: 0);
            leftStick.Processors.Add(new DeadzoneProcessor(0.15f));
            leftStick.Processors.Add(new NormalizeProcessor());
            moveAction.AddBinding(leftStick);
        }

        // Look action
        {
            lookAction = cameraMap.AddAction("Look", InputActionType.Value);
            lookAction.ExpectedValueType = typeof(Float2);

            var mouse = new DualAxisCompositeBinding(InputBinding.CreateMouseAxisBinding(0), InputBinding.CreateMouseAxisBinding(1));
            mouse.Processors.Add(new ScaleProcessor(0.25f)); // Decreased sensitivity for looking
            lookAction.AddBinding(mouse);

            // Gamepad right stick for looking with higher sensitivity and deadzone
            var rightStick = InputBinding.CreateGamepadAxisBinding(1, deviceIndex: 0);
            rightStick.Processors.Add(new DeadzoneProcessor(0.15f));
            rightStick.Processors.Add(new ScaleProcessor(3.0f)); // Increase sensitivity for looking
            lookAction.AddBinding(rightStick);
        }

        // Fly Up (E key + Gamepad A button)
        {
            flyUpAction = cameraMap.AddAction("FlyUp", InputActionType.Button);
            flyUpAction.AddBinding(KeyCode.E);
            flyUpAction.AddBinding(GamepadButton.A);
        }

        // Fly Down (Q key + Gamepad B button)
        {
            flyDownAction = cameraMap.AddAction("FlyDown", InputActionType.Button);
            flyDownAction.AddBinding(KeyCode.Q);
            flyDownAction.AddBinding(GamepadButton.B);
        }

        // Sprint (Shift + Gamepad Left Stick Click)
        {
            sprintAction = cameraMap.AddAction("Sprint", InputActionType.Button);
            sprintAction.AddBinding(KeyCode.ShiftLeft);
            sprintAction.AddBinding(GamepadButton.LeftStick);
        }

        // Register and enable
        Input.RegisterActionMap(cameraMap);
        cameraMap.Enable();
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

        // Read movement from action (works with WASD, arrows, and gamepad left stick)
        Float2 movement = moveAction.ReadValue<Float2>();

        // Calculate speed multiplier (sprint makes you move faster)
        float speedMultiplier = sprintAction.IsPressed() ? 2.5f : 1.0f;
        float moveSpeed = 5f * speedMultiplier * (float)Time.deltaTime;

        // Apply movement
        cameraGO.Transform.position += cameraGO.Transform.forward * movement.Y * moveSpeed;
        cameraGO.Transform.position += cameraGO.Transform.right * movement.X * moveSpeed;

        // Vertical movement (fly up/down)
        float upDown = 0;
        if (flyUpAction.IsPressed()) upDown += 1;
        if (flyDownAction.IsPressed()) upDown -= 1;
        cameraGO.Transform.position += Double3.UnitY * upDown * moveSpeed;

        // Look/rotate camera
        Float2 lookInput = lookAction.ReadValue<Float2>();

        // Apply look rotation from gamepad right stick
        float lookSpeed = 100f * (float)Time.deltaTime;
        cameraGO.Transform.localEulerAngles += new Double3(lookInput.Y, lookInput.X, 0) * lookSpeed;

        // Unlock cursor on press Escape
        if (Input.GetKeyDown(KeyCode.Escape))
            Input.SetCursorVisible(true);
    }
}
