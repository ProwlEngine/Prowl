// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// LineRenderer Demo
//
// This demo demonstrates the LineRenderer component with:
// - Billboard rendering (always faces camera)
// - Animated lines with dynamic point updates
// - Color gradients and width variation
// - Looped lines
// - Different texture wrap modes
//
// Controls:
//   Movement:  WASD / Arrow Keys / Gamepad Left Stick
//   Look:      Mouse Move (when RMB held) / Gamepad Right Stick
//   Fly Up:    E / Gamepad A Button
//   Fly Down:  Q / Gamepad B Button
//   Sprint:    Left Shift / Gamepad Left Stick Click
//

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using MouseButton = Prowl.Runtime.MouseButton;

namespace SimpleCube;

internal class Program
{
    static void Main(string[] args)
    {
        new MyGame().Run("LineRenderer Demo", 1280, 720);
    }
}

public sealed class MyGame : Game
{
    private GameObject? cameraGO;
    private Scene? scene;

    // Input Actions
    private InputActionMap cameraMap = null!;
    private InputAction moveAction = null!;
    private InputAction lookAction = null!;
    private InputAction lookEnableAction = null!;
    private InputAction flyUpAction = null!;
    private InputAction flyDownAction = null!;
    private InputAction sprintAction = null!;

    // Line Renderer Examples
    private LineRenderer? helix;
    private LineRenderer? sineWave;
    private LineRenderer? orbitalRing;
    private float time = 0;

    public override void Initialize()
    {
        scene = new Scene();
        SetupInputActions();

        // Create directional light
        GameObject lightGO = new("Directional Light");
        lightGO.AddComponent<DirectionalLight>();
        lightGO.Transform.LocalEulerAngles = new Float3(-80, 5, 0);
        scene.Add(lightGO);

        // Create camera
        cameraGO = new("Main Camera");
        cameraGO.Tag = "Main Camera";
        cameraGO.Transform.Position = new(0, 2, -8);
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
        groundGO.Transform.Position = new(0, -3, 0);
        groundGO.Transform.LocalScale = new(20, 1, 20);
        scene.Add(groundGO);

        // Create Line Renderer Examples
        CreateLineExamples();


        Input.SetCursorVisible(false);

        Scene.Load(scene);
    }


    private void CreateLineExamples()
    {
        Texture2D texture = Texture2D.LoadDefault(DefaultTexture.Grid);
        Material lineMat = new(Shader.LoadDefault(DefaultShader.Line));
        lineMat.SetTexture("_MainTex", texture);

        // 1. Animated Helix with width variation
        GameObject helixGO = new("Helix");
        helix = helixGO.AddComponent<LineRenderer>();
        helix.Material = lineMat;
        helix.StartWidth = 0.05f;
        helix.EndWidth = 0.15f;
        helix.TextureTiling = 10f;
        helix.StartColor = new Color(1, 0.2f, 0.2f, 1);  // Red
        helix.EndColor = new Color(1, 0.8f, 0.2f, 1);    // Orange
        helix.TextureMode = TextureWrapMode.Tile;

        helix.Points = [];
        for (int i = 0; i <= 80; i++)
        {
            float t = i / 80f;
            float angle = t * MathF.PI * 4;
            helix.Points.Add(new Float3(
                MathF.Cos(angle) * 0.8f,
                t * 3f - 1.5f,
                MathF.Sin(angle) * 0.8f
            ));
        }
        helixGO.Transform.Position = new Float3(-4, 0, 0);
        scene.Add(helixGO);

        // 2. Animated Sine Wave
        GameObject sineGO = new("SineWave");
        sineWave = sineGO.AddComponent<LineRenderer>();
        sineWave.Material = lineMat;
        sineWave.StartWidth = 0.08f;
        sineWave.EndWidth = 0.08f;
        sineWave.StartColor = new Color(0.2f, 1, 0.5f, 1);  // Green
        sineWave.EndColor = new Color(0.2f, 0.5f, 1, 1);    // Blue
        sineWave.TextureMode = TextureWrapMode.Stretch;

        sineWave.Points = [];
        for (int i = 0; i <= 60; i++)
        {
            float t = i / 60f;
            sineWave.Points.Add(new Float3(
                t * 5f - 2.5f,
                MathF.Sin(t * MathF.PI * 3) * 0.8f,
                0
            ));
        }
        sineGO.Transform.Position = new Float3(0, 1, 3);
        scene.Add(sineGO);

        // 3. Looping Orbital Ring
        GameObject ringGO = new("OrbitalRing");
        orbitalRing = ringGO.AddComponent<LineRenderer>();
        orbitalRing.Material = lineMat;
        orbitalRing.StartWidth = 0.06f;
        orbitalRing.EndWidth = 0.06f;
        orbitalRing.Loop = true;
        orbitalRing.StartColor = new Color(1, 0.3f, 1, 1);
        orbitalRing.EndColor = new Color(1, 0.3f, 1, 1);
        orbitalRing.TextureMode = TextureWrapMode.RepeatPerSegment;

        orbitalRing.Points = [];
        for (int i = 0; i < 48; i++)
        {
            float angle = (i / 48f) * MathF.PI * 2;
            orbitalRing.Points.Add(new Float3(
                MathF.Cos(angle) * 1.2f,
                MathF.Sin(angle) * 0.3f,
                MathF.Sin(angle) * 1.2f
            ));
        }
        ringGO.Transform.Position = new Float3(4, 1, 0);
        ringGO.Transform.LocalEulerAngles = new Float3(30, 45, 0);
        scene.Add(ringGO);
    }

    private void SetupInputActions()
    {
        cameraMap = new InputActionMap("Camera");

        // Movement (WASD + Gamepad)
        moveAction = cameraMap.AddAction("Move", InputActionType.Value);
        moveAction.ExpectedValueType = typeof(Float2);
        moveAction.AddBinding(new Vector2CompositeBinding(
            InputBinding.CreateKeyBinding(KeyCode.W),
            InputBinding.CreateKeyBinding(KeyCode.S),
            InputBinding.CreateKeyBinding(KeyCode.A),
            InputBinding.CreateKeyBinding(KeyCode.D),
            true
        ));
        var leftStick = InputBinding.CreateGamepadAxisBinding(0, deviceIndex: 0);
        leftStick.Processors.Add(new DeadzoneProcessor(0.15f));
        leftStick.Processors.Add(new NormalizeProcessor());
        moveAction.AddBinding(leftStick);

        // Look enable (RMB)
        lookEnableAction = cameraMap.AddAction("LookEnable", InputActionType.Button);
        lookEnableAction.AddBinding(MouseButton.Right);

        // Look (Mouse + Gamepad)
        lookAction = cameraMap.AddAction("Look", InputActionType.Value);
        lookAction.ExpectedValueType = typeof(Float2);
        var mouse = new DualAxisCompositeBinding(
            InputBinding.CreateMouseAxisBinding(0),
            InputBinding.CreateMouseAxisBinding(1));
        mouse.Processors.Add(new ScaleProcessor(0.1f));
        lookAction.AddBinding(mouse);
        var rightStick = InputBinding.CreateGamepadAxisBinding(1, deviceIndex: 0);
        rightStick.Processors.Add(new DeadzoneProcessor(0.15f));
        rightStick.Processors.Add(new NormalizeProcessor());
        lookAction.AddBinding(rightStick);

        // Fly Up/Down
        flyUpAction = cameraMap.AddAction("FlyUp", InputActionType.Button);
        flyUpAction.AddBinding(KeyCode.E);
        flyUpAction.AddBinding(GamepadButton.A);
        flyDownAction = cameraMap.AddAction("FlyDown", InputActionType.Button);
        flyDownAction.AddBinding(KeyCode.Q);
        flyDownAction.AddBinding(GamepadButton.B);

        // Sprint
        sprintAction = cameraMap.AddAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding(KeyCode.ShiftLeft);
        sprintAction.AddBinding(GamepadButton.LeftStick);

        Input.RegisterActionMap(cameraMap);
        cameraMap.Enable();
    }

    public override void BeginUpdate()
    {
        time += (float)Time.DeltaTime;

        // Animate helix rotation
        if (helix.IsValid())
        {
            helix.GameObject.Transform.LocalEulerAngles = new Float3(0, time * 25, 0);
        }

        // Animate sine wave
        if (sineWave.IsValid())
        {
            sineWave.Points.Clear();
            for (int i = 0; i <= 60; i++)
            {
                float t = i / 60f;
                sineWave.Points.Add(new Float3(
                    t * 5f - 2.5f,
                    MathF.Sin(t * MathF.PI * 3 + time * 2) * 0.8f,
                    0
                ));
            }
            sineWave.MarkDirty();
        }

        // Animate orbital ring rotation
        if (orbitalRing.IsValid())
        {
            orbitalRing.GameObject.Transform.LocalEulerAngles += new Float3(
                10 * Time.DeltaTime,
                20 * Time.DeltaTime,
                15 * Time.DeltaTime
            );
        }

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
        if (lookEnableAction.IsPressed() || Maths.Abs(lookInput.X) > 0.01f || Maths.Abs(lookInput.Y) > 0.01f)
        {
            cameraGO.Transform.LocalEulerAngles += new Float3(lookInput.Y, lookInput.X, 0);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            Input.SetCursorVisible(true);
    }
}
