// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// Fly Camera Demo
//
// This demo demonstrates a simple scene with:
// - A rotating cube
// - Fly camera controls
// - Basic lighting
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
using Prowl.Vector.Geometry;

using MouseButton = Prowl.Runtime.MouseButton;

namespace FlyCamera;

internal class Program
{
    static void Main(string[] args)
    {
        new MyGame().Run("Fly Camera Demo", 1280, 720);
    }
}

public sealed class MyGame : Game
{
    private GameObject? cameraGO;
    private GameObject? cubeGO;
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
        lightGO.AddComponent<DirectionalLight>();
        lightGO.Transform.LocalEulerAngles = new Float3(-45, 45, 0);
        scene.Add(lightGO);

        // Create camera
        cameraGO = new("Main Camera");
        cameraGO.Tag = "Main Camera";
        cameraGO.Transform.Position = new(0, 2, -5);
        Camera camera = cameraGO.AddComponent<Camera>();
        camera.ClearFlags = CameraClearFlags.SolidColor;
        camera.ClearColor = new Color(0.02f, 0.02f, 0.05f, 1.0f);
        camera.Depth = -1;
        camera.HDR = true;
        camera.Effects =
        [
            new FXAAEffect(),
            new TonemapperEffect(),
        ];
        scene.Add(cameraGO);

        // Create cube
        cubeGO = new("Cube");
        MeshRenderer mr = cubeGO.AddComponent<MeshRenderer>();
        mr.Mesh = Mesh.CreateCube(Float3.One);
        mr.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        mr.Material.SetColor("_BaseColor", new Color(0.8f, 0.3f, 0.3f, 1.0f));
        cubeGO.Transform.Position = new(0, 1, 0);
        scene.Add(cubeGO);

        // Create Voxel Planet with Octree LOD
        GameObject planetGO = new("Voxel Planet");
        VoxelPlanet voxelPlanet = planetGO.AddComponent<VoxelPlanet>();
        voxelPlanet.camera = camera;
        voxelPlanet.material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        scene.Add(planetGO);

        Input.SetCursorVisible(false);

        Scene.Load(scene);
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
        // Rotate the cube
        if (cubeGO.IsValid())
        {
            cubeGO.Transform.LocalEulerAngles += new Float3(25, 50, 15) * Time.DeltaTime;
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

public interface IBrush
{
    void Apply(Float3 position, ref float voxel);
}

public sealed class SphereBrush : IBrush
{
    public Float3 center;
    public float radius;

    public SphereBrush(Float3 position, float radius)
    {
        this.center = position;
        this.radius = radius;
    }

    public void Apply(Float3 position, ref float voxel)
    {
        float dist = Float3.Distance(position, center);
        float sdf = (float)(dist - radius);
        voxel = MathF.Min(voxel, sdf);
    }
}

public sealed class VoxelPlanet : MonoBehaviour
{
    public Camera? camera;
    private PlanetNode? rootNode;

    public List<IBrush> brushes = new();
    public Material material;

    public override void OnEnable()
    {
        AABB rootAABB = AABB.FromCenterAndSize(Float3.Zero, new Float3(20, 20, 20));
        rootNode = new PlanetNode(rootAABB, null!, this, 0);

        brushes.Add(new SphereBrush(Float3.Zero, 7.5f));
    }

    public float SampleVoxel(Float3 position)
    {
        float voxelValue = float.MaxValue;
        foreach (var brush in brushes)
        {
            brush.Apply(position, ref voxelValue);
        }
        return voxelValue;
    }

    public override void Update()
    {
        if (camera != null && camera.GameObject != null && rootNode != null)
        {
            Float3 cameraPos = camera.GameObject.Transform.Position;
            rootNode.Update(cameraPos);
        }
    }

    public override void LateUpdate()
    {
        if (camera != null && camera.GameObject != null && rootNode != null)
        {
            Float3 cameraPos = camera.GameObject.Transform.Position;
            rootNode.LateUpdate(cameraPos);
        }
    }

    public override void DrawGizmos()
    {
        if (rootNode != null)
        {
            rootNode.DrawGizmos();
        }
    }
}

public sealed class PlanetNode
{
    public const int MAXDEPTH = 3;
    public const int RESOLUTION = 16;

    public AABB AABB;
    public PlanetNode? parent;
    public VoxelPlanet planet;
    public int depth;
    public PlanetNode[]? children = null;

    public Mesh mesh = null;

    private static MarchingCubes MarchingCubes = new();
    static List<Float3> verts = new();
    static List<uint> indices = new();

    public PlanetNode(AABB localAABB, PlanetNode? parent, VoxelPlanet planet, int depth)
    {
        AABB = localAABB;
        this.parent = parent;
        this.planet = planet;
        this.depth = depth;
    }

    public bool ShouldSubdivide(Float3 cameraCenter)
    {
        // Subdivide if camera is close enough (distance squared < size squared)
        float distance = AABB.GetSqrDistanceToPoint(cameraCenter);
        float threshold = Float3.LengthSquared(AABB.Size) * 0.5f;
        return distance < threshold;
    }

    public void Update(Float3 cameraPos)
    {
        if (children == null)
        {
            // Currently merged - check if we should subdivide
            if (depth < MAXDEPTH && ShouldSubdivide(cameraPos))
            {
                Subdivide();
            }
            else
            {
                // Leaf node
            }
        }
        else
        {
            // Currently subdivided - update children first
            for (int i = 0; i < 8; i++)
            {
                children[i].Update(cameraPos);
            }

            // Check if we should merge
            if (!ShouldSubdivide(cameraPos))
            {
                Merge();
            }
        }
    }

    public void LateUpdate(Float3 cameraPos)
    {
        if (children == null)
        {
            // Leaf node
            TryDraw();
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                children[i].LateUpdate(cameraPos);
            }
        }
    }

    private void TryDraw()
    {
        if (mesh == null)
        {
            verts.Clear();
            indices.Clear();
            MarchingCubes.Generate(planet.SampleVoxel, RESOLUTION, RESOLUTION, RESOLUTION, AABB.Min, AABB.Max, verts, indices);

            mesh = new Mesh();
            if (verts.Count > 0)
            {
                // Convert vertices from local voxel grid space to world space
                for (int i = 0; i < verts.Count; i++)
                {
                    Float3 localPos = verts[i];
                    Float3 normalizedPos = new Float3(
                        localPos.X / (RESOLUTION - 1),
                        localPos.Y / (RESOLUTION - 1),
                        localPos.Z / (RESOLUTION - 1)
                    );
                    Float3 worldPos = AABB.Min + normalizedPos * AABB.Size;
                    verts[i] = new Float3(worldPos.X, worldPos.Y, worldPos.Z);
                }

                mesh.Vertices = verts.ToArray();
                mesh.Indices = indices.ToArray();
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                //mesh.RecalculateTangents();
            }
        }

        if (mesh != null && mesh.VertexCount > 0)
        {
            this.planet.GameObject.Scene.PushRenderable(new MeshRenderable(mesh, this.planet.material, Float4x4.Identity, this.planet.GameObject.LayerIndex));
        }
    }

    public void Subdivide()
    {
        if (children != null || depth >= MAXDEPTH)
            return;

        children = new PlanetNode[8];

        // Calculate half size for the children
        Float3 halfSize = AABB.Size * 0.5f;
        Float3 quarterSize = AABB.Size * 0.25f;

        // Create 8 children octants
        for (int i = 0; i < 8; i++)
        {
            // Calculate offset for this octant
            Float3 offset = new Float3(
                (i & 1) == 0 ? -quarterSize.X : quarterSize.X,
                (i & 2) == 0 ? -quarterSize.Y : quarterSize.Y,
                (i & 4) == 0 ? -quarterSize.Z : quarterSize.Z
            );

            Float3 childCenter = AABB.Center + offset;
            AABB childAABB = AABB.FromCenterAndSize(childCenter, halfSize);

            children[i] = new PlanetNode(childAABB, this, planet, depth + 1);
        }
    }

    public void Merge()
    {
        if (children == null)
            return;

        // Recursively merge all children first
        for (int i = 0; i < 8; i++)
        {
            children[i].Merge();
        }

        children = null;
    }

    public void DrawGizmos()
    {
        if (children == null)
        {
            if (mesh == null && mesh.VertexCount < 0) return;

            // Leaf node - draw this node's bounds
            Color color = GetColorForDepth(depth);
            Float3 halfExtents = AABB.Size * 0.5f;
            Debug.DrawWireCube(AABB.Center, halfExtents, color);
        }
        else
        {
            // Internal node - draw children
            for (int i = 0; i < 8; i++)
            {
                children[i].DrawGizmos();
            }
        }
    }

    private Color GetColorForDepth(int depth)
    {
        // Create different colors for different depths
        return depth switch
        {
            0 => new Color(1.0f, 0.0f, 0.0f, 1.0f),  // Red
            1 => new Color(1.0f, 0.5f, 0.0f, 1.0f),  // Orange
            2 => new Color(1.0f, 1.0f, 0.0f, 1.0f),  // Yellow
            3 => new Color(0.0f, 1.0f, 0.0f, 1.0f),  // Green
            4 => new Color(0.0f, 0.5f, 1.0f, 1.0f),  // Blue
            5 => new Color(0.5f, 0.0f, 1.0f, 1.0f),  // Purple
            _ => new Color(1.0f, 1.0f, 1.0f, 1.0f),  // White
        };
    }
}
