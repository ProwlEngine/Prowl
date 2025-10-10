// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Raylib_cs;

using SoftwareRasterizer.Rasterizer;

using Float3 = Prowl.Vector.Float3;

namespace SoftwareRasterizer;

public struct FreelookCamera
{
    public Float3 Position;
    public float Yaw;    // Degrees
    public float Pitch;  // Degrees
    public float FovY;   // Degrees

    public Float3 Forward { get; private set; }
    public Float3 Right { get; private set; }
    public Float3 Up { get; private set; } // Camera's local up

    private Float3 worldUp;

    public FreelookCamera(Float3 position, float yaw, float pitch, float fovY, Float3 worldUp)
    {
        this.Position = position;
        this.Yaw = yaw;
        this.Pitch = pitch; // Clamped during update
        this.FovY = fovY;
        this.worldUp = worldUp;

        this.Forward = Float3.Zero;
        this.Right = Float3.Zero;
        this.Up = Float3.Zero;
        UpdateVectors(); // Initial calculation
    }

    public void UpdateVectors()
    {
        // Clamp pitch
        if (Pitch > 89.0f) Pitch = 89.0f;
        if (Pitch < -89.0f) Pitch = -89.0f;

        float yawRad = Maths.ToRadians(Yaw);
        float pitchRad = Maths.ToRadians(Pitch);

        Float3 calculatedForward;
        calculatedForward.X = Maths.Cos(yawRad) * Maths.Cos(pitchRad);
        calculatedForward.Y = Maths.Sin(pitchRad);
        calculatedForward.Z = Maths.Sin(yawRad) * Maths.Cos(pitchRad);
        Forward = Maths.Normalize(calculatedForward);

        Right = Maths.Normalize(Maths.Cross(worldUp, Forward));
        Up = Maths.Normalize(Maths.Cross(Forward, Right));
    }

    public Float4x4 GetViewMatrix()
    {
        return Float4x4.CreateLookAt(Position, Position + Forward, Up);
    }
}


public class RaylibDemo
{
    private const int DownscaleFactor = 4;
    private const string WindowTitle = "Software Renderer Demo";

    private GraphicsDevice renderer;
    private DiffuseShader diffuseShader;
    private RaylibPresenter presenter;
    private RenderTexture2D renderTexture;
    private MeshGenerator.MeshData sphereModel;
    private MeshGenerator.MeshData cubeModel;
    private MeshGenerator.MeshData corridorModel;
    private float cubeRotation;

    private int width;
    private int height;

    private FreelookCamera camera;
    private bool isMouseLocked = false;
    private float moveSpeed = 5.0f;
    private float lookSensitivity = 0.1f;
    private Float2 previousMousePosition;

    public RaylibDemo()
    {
        width = 1080;
        height = 720;
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);

        Raylib.InitWindow(width, height, WindowTitle);
        Raylib.SetExitKey(KeyboardKey.Null);

        renderer = new GraphicsDevice(width / DownscaleFactor, height / DownscaleFactor);
        presenter = new RaylibPresenter(width / DownscaleFactor, height / DownscaleFactor);
        diffuseShader = new DiffuseShader();
        renderTexture = Raylib.LoadRenderTexture(100, 100);

        sphereModel = MeshGenerator.GenerateSphereMesh(1, 8, 8);
        sphereModel.CreateVAttributes();

        cubeModel = MeshGenerator.GenerateCubeMesh(1);
        cubeModel.CreateVAttributes();

        corridorModel = MeshGenerator.GenerateCorridorMesh(2f, 3f);
        corridorModel.CreateVAttributes();

        // Initialize FreelookCamera
        camera = new FreelookCamera(
            position: new Float3(0, 1, -3),
            yaw: 90.0f,
            pitch: -18.43f,
            fovY: 70.0f,
            worldUp: Float3.UnitY
        );

        previousMousePosition = Raylib.GetMousePosition();

        //Raylib.SetTargetFPS(60);
    }

    private void HandleInput(float deltaTime)
    {
        // Mouse lock/unlock
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (isMouseLocked)
            {
                Raylib.EnableCursor();
                Raylib.SetMousePosition((int)previousMousePosition.X, (int)previousMousePosition.Y); // Restore mouse
                isMouseLocked = false;
            }
        }
        else if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !isMouseLocked)
        {
            previousMousePosition = Raylib.GetMousePosition(); // Store current mouse position
            Raylib.DisableCursor();
            isMouseLocked = true;
        }

        if (isMouseLocked)
        {
            // Mouse Look
            Float2 mouseDelta = Raylib.GetMouseDelta();
            camera.Yaw -= mouseDelta.X * lookSensitivity;
            camera.Pitch -= mouseDelta.Y * lookSensitivity;

            if (camera.Yaw > 360.0f) camera.Yaw -= 360.0f;
            if (camera.Yaw < 0.0f) camera.Yaw += 360.0f;

            camera.UpdateVectors(); // Recalculate Forward, Right, Up vectors

            // Keyboard Movement
            Float3 moveDir = Float3.Zero;
            if (Raylib.IsKeyDown(KeyboardKey.W)) moveDir += camera.Forward;
            if (Raylib.IsKeyDown(KeyboardKey.S)) moveDir -= camera.Forward;
            if (Raylib.IsKeyDown(KeyboardKey.A)) moveDir -= camera.Right;
            if (Raylib.IsKeyDown(KeyboardKey.D)) moveDir += camera.Right;
            if (Raylib.IsKeyDown(KeyboardKey.E)) moveDir += Float3.UnitY; // Use world Up for Q/E
            if (Raylib.IsKeyDown(KeyboardKey.Q)) moveDir -= Float3.UnitY;

            if (Maths.LengthSquared(moveDir) > Maths.EpsilonF) // Check if there's any movement input
            {
                camera.Position += Maths.Normalize(moveDir) * moveSpeed * deltaTime;
            }
        }
    }

    private void DrawMesh(MeshGenerator.MeshData mesh, Float4x4 modelMatrix, float alpha = 1.0f)
    {
        diffuseShader.modelMatrix = modelMatrix;
        diffuseShader.alpha = alpha;
        //renderer.DrawVertexBufferTiled(mesh.VertexBuffer);
        renderer.DrawVertexBuffer(mesh.VertexBuffer);
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            if (width != Raylib.GetScreenWidth() || height != Raylib.GetScreenHeight())
            {
                width = Raylib.GetScreenWidth();
                height = Raylib.GetScreenHeight();
                renderer.Resize(width / DownscaleFactor, height / DownscaleFactor);
                presenter?.Dispose();
                presenter = new RaylibPresenter(width / DownscaleFactor, height / DownscaleFactor);
            }

            float deltaTime = Raylib.GetFrameTime();
            HandleInput(deltaTime); // Handle camera input

            Draw();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            //presenter.Present(renderer.GetDisplayBuffer());
            presenter.Present(renderer.BackBuffer);
            Raylib.DrawFPS(10, 10);
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    private void Draw()
    {
        //renderer.BeginFrame();

        renderer.SetCullMode(CullMode.Back);
        renderer.ClearFramebuffer(0, 0, 0, 1);
        renderer.SetPolygonMode(PolygonMode.Triangles);

        // Get view and projection matrices from our camera
        var viewMatrix = camera.GetViewMatrix();
        var projectionMatrix = Float4x4.CreatePerspectiveFov(
            Maths.ToRadians(camera.FovY),
            (float)width / height, // Aspect ratio of the render target
            0.1f,
            100f
        );

        diffuseShader.viewMatrix = viewMatrix;
        diffuseShader.projectionMatrix = projectionMatrix;
        diffuseShader.lightDir = Maths.Normalize(new Float3(0.5f, -0.85f, 0.4f));
        renderer.BindShader(diffuseShader);

        DrawMesh(corridorModel, Float4x4.Identity);

        // Draw opaque sphere
        DrawMesh(sphereModel, Float4x4.CreateTranslation(new Float3(0, 0, 0)));

        // Draw rotating cube
        cubeRotation += Raylib.GetFrameTime();
        var cubeModelMatrix = Maths.Mul(Float4x4.CreateTranslation(new Float3(-1, 0, 0)), Float4x4.FromAxisAngle(Maths.Normalize(Float3.One), cubeRotation));
        DrawMesh(cubeModel, cubeModelMatrix);

        // Draw transparent sphere
        renderer.SetBlendFunction(BlendFunction.AlphaBlend);
        DrawMesh(sphereModel, Float4x4.CreateTranslation(new Float3(1, 0, 0)), 0.5f);
        renderer.SetBlendFunction(BlendFunction.Normal);
    }
}
