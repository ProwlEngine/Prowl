// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace ShapeCastDemo;

internal class Program
{
    static void Main(string[] args)
    {
        new ShapeCastDemoGame().Run("Shape Cast Demo - Character Controller", 1280, 720);
    }
}

public sealed class ShapeCastDemoGame : Game
{
    private GameObject? cameraGO;
    private Scene? scene;
    private Material? standardMaterial;
    private GameObject? playerGO;
    private PlayerController? playerController;

    public override void Initialize()
    {
        DrawGizmos = true;

        scene = new Scene();

        // Create directional light
        GameObject lightGO = new("Directional Light");
        DirectionalLight light = lightGO.AddComponent<DirectionalLight>();
        light.ShadowQuality = ShadowQuality.Soft;
        light.ShadowBias = 0.5f;
        lightGO.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        scene.Add(lightGO);

        // Create camera
        GameObject cam = new("Main Camera");
        cam.Tag = "Main Camera";
        cam.Transform.Position = new(0, 5, -10);
        cam.Transform.LocalEulerAngles = new Float3(20, 0, 0);
        Camera camera = cam.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;
        cameraGO = cam;

        camera.Effects =
        [
            new ScreenSpaceReflectionEffect(),
            new FXAAEffect(),
            new KawaseBloomEffect(),
            new TonemapperEffect(),
        ];

        scene.Add(cam);

        // Create single shared material
        standardMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));

        // Create floor (static)
        CreateFloor();

        // Create stairs
        CreateStairs();

        // Create slopes
        CreateSlope(new Float3(-8, 0, 5), 2, 10, -30);
        CreateSlope(new Float3(-2, 0, 5), 2, 10, -45);

        CreateSlope(new Float3(8, 0, 5), 2, 10, -60);

        // Create some obstacles
        CreateObstacles();

        // Create player character
        CreatePlayer();

        Scene.Load(scene);
    }

    private void CreateFloor()
    {
        GameObject floor = new("Floor");
        MeshRenderer floorRenderer = floor.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = Mesh.CreateCube(new Float3(40, 1, 40));
        floorRenderer.Material = standardMaterial;
        floorRenderer.MainColor = new Color(0.7f, 0.7f, 0.7f, 1.0f);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        Rigidbody3D floorRigidbody = floor.AddComponent<Rigidbody3D>();
        floorRigidbody.IsStatic = true;
        BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Float3(40, 1, 40);

        scene.Add(floor);
    }

    private void CreateStairs()
    {
        int stepCount = 10;
        float stepWidth = 2.0f;
        float stepHeight = 0.3f;
        float stepDepth = 0.5f;

        for (int i = 0; i < stepCount; i++)
        {
            GameObject step = new($"Step {i}");
            MeshRenderer stepRenderer = step.AddComponent<MeshRenderer>();
            stepRenderer.Mesh = Mesh.CreateCube(new Float3(stepWidth, stepHeight, stepDepth));
            stepRenderer.Material = standardMaterial;
            stepRenderer.MainColor = new Color(0.6f, 0.6f, 0.8f, 1.0f);

            step.Transform.Position = new Float3(
                -10,
                i * stepHeight,
                i * stepDepth
            );

            Rigidbody3D stepRb = step.AddComponent<Rigidbody3D>();
            stepRb.IsStatic = true;
            BoxCollider stepCollider = step.AddComponent<BoxCollider>();
            stepCollider.Size = new Float3(stepWidth, stepHeight, stepDepth);

            scene.Add(step);
        }
    }

    private void CreateSlope(Float3 position, float width, float length, float angleDegrees)
    {
        GameObject slope = new("Slope");
        MeshRenderer slopeRenderer = slope.AddComponent<MeshRenderer>();
        slopeRenderer.Mesh = Mesh.CreateCube(new Float3(width, 0.5f, length));
        slopeRenderer.Material = standardMaterial;
        slopeRenderer.MainColor = new Color(0.8f, 0.6f, 0.6f, 1.0f);

        slope.Transform.Position = position;
        slope.Transform.LocalEulerAngles = new Float3(angleDegrees, 0, 0);

        Rigidbody3D slopeRb = slope.AddComponent<Rigidbody3D>();
        slopeRb.IsStatic = true;
        BoxCollider slopeCollider = slope.AddComponent<BoxCollider>();
        slopeCollider.Size = new Float3(width, 0.5f, length);

        scene.Add(slope);
    }

    private void CreateObstacles()
    {
        // Create some boxes as obstacles
        for (int i = 0; i < 5; i++)
        {
            GameObject box = new($"Obstacle {i}");
            MeshRenderer boxRenderer = box.AddComponent<MeshRenderer>();
            float height = 0.5f + i * 0.5f;
            boxRenderer.Mesh = Mesh.CreateCube(new Float3(1, height, 1));
            boxRenderer.Material = standardMaterial;
            boxRenderer.MainColor = new Color(0.9f, 0.5f, 0.3f, 1.0f);

            box.Transform.Position = new Float3(
                i * 3 - 6,
                height * 0.5f,
                -5
            );

            Rigidbody3D boxRb = box.AddComponent<Rigidbody3D>();
            boxRb.IsStatic = true;
            BoxCollider boxCollider = box.AddComponent<BoxCollider>();
            boxCollider.Size = new Float3(1, height, 1);

            scene.Add(box);
        }
    }

    private void CreatePlayer()
    {
        playerGO = new GameObject("Player");
        playerGO.Transform.Position = new Float3(0, 2, 0);

        //var playerRenderer = playerGO.AddComponent<MeshRenderer>();
        //playerRenderer.Mesh = Mesh.CreateCapsule(0.5f, 1.8f, 16, 8);
        //playerRenderer.Material = standardMaterial;

        playerGO.AddComponent<CharacterController>();
        playerController = playerGO.AddComponent<PlayerController>();

        scene.Add(playerGO);
    }

    public override void BeginUpdate()
    {
        // Camera follows player from behind
        if (playerGO.IsValid())
        {
            Float3 targetPos = playerGO.Transform.Position + new Float3(0, 3, -8);
            cameraGO.Transform.Position = Maths.Lerp(cameraGO.Transform.Position, targetPos, 1.0f * Time.DeltaTime);

            // Look at player
            Float3 lookDir = playerGO.Transform.Position - cameraGO.Transform.Position;
            if (Float3.Length(lookDir) > 0.01)
            {
                float pitch = Maths.Atan2(lookDir.Y, Maths.Sqrt(lookDir.X * lookDir.X + lookDir.Z * lookDir.Z));
                float yaw = Maths.Atan2(lookDir.X, lookDir.Z);
                cameraGO.Transform.LocalEulerAngles = new Float3(
                    -pitch * 180.0f / Maths.PI,
                    yaw * 180.0f / Maths.PI,
                    0
                );
            }
        }

        // Reset with R
        if (Input.GetKeyDown(KeyCode.R))
        {
            scene.Clear();
            Initialize();
        }
    }
}

/// <summary>
/// Handles player movement, gravity, jumping, crouching, and input.
/// Uses the CharacterController for collision detection and movement.
/// </summary>
public class PlayerController : MonoBehaviour
{
    public float MoveSpeed = 5.0f;
    public float CrouchMoveSpeed = 2.5f;
    public float JumpForce = 8.0f;
    public float Gravity = 20.0f;
    public float StandingHeight = 1.8f;
    public float CrouchHeight = 0.9f;

    private CharacterController? characterController;
    private Float3 velocity = Float3.Zero;
    private Float3 moveInput = Float3.Zero;
    private bool jumpInput = false;
    private bool crouchInput = false;
    private bool isCrouching = false;

    public override void OnEnable()
    {
        characterController = GameObject.GetComponent<CharacterController>();
    }

    public override void Update()
    {
        if (characterController.IsNotValid()) return;

        moveInput = Float3.Zero;
        if (Input.GetKey(KeyCode.W)) moveInput += new Float3(0, 0, 1);
        if (Input.GetKey(KeyCode.S)) moveInput -= new Float3(0, 0, 1);
        if (Input.GetKey(KeyCode.A)) moveInput -= new Float3(1, 0, 0);
        if (Input.GetKey(KeyCode.D)) moveInput += new Float3(1, 0, 0);
        moveInput = Float3.Normalize(moveInput);

        jumpInput = Input.GetKeyDown(KeyCode.Space);
        crouchInput = Input.GetKey(KeyCode.ControlLeft);

        // Handle crouching
        HandleCrouch();

        // Update horizontal velocity based on input
        float currentSpeed = isCrouching ? CrouchMoveSpeed : MoveSpeed;
        Float3 horizontalVelocity = moveInput * currentSpeed;
        velocity.X = horizontalVelocity.X;
        velocity.Z = horizontalVelocity.Z;

        HandleGravityAndJump();

        // Calculate total movement for this frame
        Float3 movement = velocity * Time.DeltaTime;

        // Move the character using the CharacterController (this also updates IsGrounded)
        characterController.Move(movement);
    }

    private void HandleCrouch()
    {
        if (crouchInput && !isCrouching)
        {
            // Try to crouch
            if (characterController.TrySetHeight(CrouchHeight))
            {
                isCrouching = true;
            }
        }
        else if (!crouchInput && isCrouching)
        {
            // Try to stand up (only if there's clearance above)
            if (characterController.TrySetHeight(StandingHeight))
            {
                isCrouching = false;
            }
            // If TrySetHeight fails, player remains crouched (not enough clearance)
        }
    }

    private void HandleGravityAndJump()
    {
        if (!characterController.IsGrounded)
        {
            velocity.Y -= Gravity * Time.DeltaTime;
        }
        else
        {
            if (velocity.Y < 0)
                velocity.Y = 0;

            // Handle jump when grounded (can't jump while crouching)
            if (jumpInput && !isCrouching)
            {
                velocity.Y = JumpForce;
            }
        }
    }

    public override void DrawGizmos()
    {
        // Draw velocity
        if (Float3.Length(velocity) > 0.1)
        {
            Float3 position = GameObject.Transform.Position;
            Debug.DrawArrow(position, velocity * 0.5f, new Color(255, 255, 0, 255));
        }
    }
}
