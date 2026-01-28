// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace PhysicsTesterDemo;

internal class Program
{
    static void Main(string[] args)
    {
        new PhysicsTesterDemo().Run("Physics Tester Demo - Static Collider Edge Cases", 1920, 1080);
    }
}

public sealed class PhysicsTesterDemo : Game
{
    private GameObject? cameraGO;
    private Scene? scene;
    private Material? standardMaterial;
    private GameObject? testCase5Child; // For test case 6 (removing child rigidbody)
    private float timeSinceStart = 0;

    // Click-and-drag functionality
    private Rigidbody3D? grabbedRigidbody;
    private Float3 grabOffset;
    private float grabDistance;

    public override void Initialize()
    {
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
        cam.Transform.Position = new(0, 10, -30);
        cam.Transform.LocalEulerAngles = new Float3(15, 0, 0);
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

        Debug.Log("=== Physics Tester Demo ===");
        Debug.Log("Testing edge cases for the new static collider system:");
        Debug.Log("");

        // TEST CASE 1: Giant floor with just a BoxCollider (no Rigidbody3D)
        // Expected: Should automatically attach to the static rigidbody
        CreateTestCase1_StaticFloor();

        // TEST CASE 2: Walls with just colliders (no Rigidbody3D)
        // Expected: Should automatically attach to the static rigidbody
        CreateTestCase2_StaticWalls();

        // TEST CASE 3: Create Rigidbody3D first, then add Collider
        // Expected: Collider should attach to the Rigidbody3D and fall
        CreateTestCase3_RigidbodyThenCollider();

        // TEST CASE 4: Create Collider first, then add Rigidbody3D
        // Expected: Collider should detach from static rigidbody and attach to the new Rigidbody3D, then fall
        CreateTestCase4_ColliderThenRigidbody();

        // TEST CASE 5: Parent with Rigidbody3D, child with Rigidbody3D
        // Expected: Each should claim their own colliders and fall independently
        CreateTestCase5_ParentAndChildRigidbodies();

        Scene.Load(scene);
    }

    private void CreateTestCase1_StaticFloor()
    {
        Debug.Log("TEST CASE 1: Creating giant floor with just BoxCollider (no Rigidbody3D)");

        GameObject floor = new("Test1_Floor");
        MeshRenderer floorRenderer = floor.AddComponent<MeshRenderer>();
        floorRenderer.Mesh = Mesh.CreateCube(new Float3(40, 1, 40));
        floorRenderer.Material = standardMaterial;
        floorRenderer.MainColor = new Color(0.7f, 0.7f, 0.7f, 1.0f);
        floor.Transform.Position = new Float3(0, -0.5f, 0);

        // Just add a collider - NO Rigidbody3D
        BoxCollider floorCollider = floor.AddComponent<BoxCollider>();
        floorCollider.Size = new Float3(40, 1, 40);

        scene.Add(floor);
        Debug.Log("  ✓ Floor created with collider only - should attach to static rigidbody automatically");
    }

    private void CreateTestCase2_StaticWalls()
    {
        Debug.Log("TEST CASE 2: Creating walls with just colliders (no Rigidbody3D)");

        // North wall
        CreateWall("Test2_Wall_North", new Float3(0, 2.5f, 20), new Float3(40, 5, 1), new Color(0.6f, 0.6f, 0.8f, 1.0f));

        // South wall
        CreateWall("Test2_Wall_South", new Float3(0, 2.5f, -20), new Float3(40, 5, 1), new Color(0.6f, 0.6f, 0.8f, 1.0f));

        // East wall
        CreateWall("Test2_Wall_East", new Float3(20, 2.5f, 0), new Float3(1, 5, 40), new Color(0.6f, 0.8f, 0.6f, 1.0f));

        // West wall
        CreateWall("Test2_Wall_West", new Float3(-20, 2.5f, 0), new Float3(1, 5, 40), new Color(0.6f, 0.8f, 0.6f, 1.0f));

        Debug.Log("  ✓ 4 walls created with colliders only - should attach to static rigidbody automatically");
    }

    private void CreateWall(string name, Float3 position, Float3 size, Color color)
    {
        GameObject wall = new(name);
        MeshRenderer wallRenderer = wall.AddComponent<MeshRenderer>();
        wallRenderer.Mesh = Mesh.CreateCube(size);
        wallRenderer.Material = standardMaterial;
        wallRenderer.MainColor = color;
        wall.Transform.Position = position;

        // Just add a collider - NO Rigidbody3D
        BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
        wallCollider.Size = size;

        scene.Add(wall);
    }

    private void CreateTestCase3_RigidbodyThenCollider()
    {
        Debug.Log("TEST CASE 3: Creating Rigidbody3D first, then adding Collider");

        GameObject cube = new("Test3_RigidbodyFirst");
        MeshRenderer cubeRenderer = cube.AddComponent<MeshRenderer>();
        cubeRenderer.Mesh = Mesh.CreateCube(new Float3(2, 2, 2));
        cubeRenderer.Material = standardMaterial;
        cubeRenderer.MainColor = new Color(1.0f, 0.5f, 0.5f, 1.0f);
        cube.Transform.Position = new Float3(-10, 10, 0);

        // Add Rigidbody3D FIRST
        Rigidbody3D rb = cube.AddComponent<Rigidbody3D>();
        rb.Mass = 2.0f;

        // Then add Collider
        BoxCollider collider = cube.AddComponent<BoxCollider>();
        collider.Size = new Float3(2, 2, 2);

        scene.Add(cube);
        Debug.Log("  ✓ Cube created with Rigidbody3D first - should fall");
    }

    private void CreateTestCase4_ColliderThenRigidbody()
    {
        Debug.Log("TEST CASE 4: Creating Collider first, then adding Rigidbody3D");

        GameObject cube = new("Test4_ColliderFirst");
        MeshRenderer cubeRenderer = cube.AddComponent<MeshRenderer>();
        cubeRenderer.Mesh = Mesh.CreateCube(new Float3(2, 2, 2));
        cubeRenderer.Material = standardMaterial;
        cubeRenderer.MainColor = new Color(0.5f, 1.0f, 0.5f, 1.0f);
        cube.Transform.Position = new Float3(-5, 10, 0);

        // Add Collider FIRST (will attach to static rigidbody)
        BoxCollider collider = cube.AddComponent<BoxCollider>();
        collider.Size = new Float3(2, 2, 2);

        // Then add Rigidbody3D (should claim the collider from static rigidbody)
        Rigidbody3D rb = cube.AddComponent<Rigidbody3D>();
        rb.Mass = 2.0f;

        scene.Add(cube);
        Debug.Log("  ✓ Cube created with Collider first, then Rigidbody3D - should fall");
    }

    private void CreateTestCase5_ParentAndChildRigidbodies()
    {
        Debug.Log("TEST CASE 5: Parent with Rigidbody3D, child with Rigidbody3D");

        // Create parent
        GameObject parent = new("Test5_Parent");
        MeshRenderer parentRenderer = parent.AddComponent<MeshRenderer>();
        parentRenderer.Mesh = Mesh.CreateCube(new Float3(3, 1, 3));
        parentRenderer.Material = standardMaterial;
        parentRenderer.MainColor = new Color(0.5f, 0.5f, 1.0f, 1.0f);
        parent.Transform.Position = new Float3(5, 10, 0);

        // Add Rigidbody3D to parent
        Rigidbody3D parentRb = parent.AddComponent<Rigidbody3D>();
        parentRb.Mass = 3.0f;

        // Add collider to parent
        BoxCollider parentCollider = parent.AddComponent<BoxCollider>();
        parentCollider.Size = new Float3(3, 1, 3);

        scene.Add(parent);

        // Create child
        GameObject child = new("Test5_Child");
        MeshRenderer childRenderer = child.AddComponent<MeshRenderer>();
        childRenderer.Mesh = Mesh.CreateCube(new Float3(1, 1, 1));
        childRenderer.Material = standardMaterial;
        childRenderer.MainColor = new Color(1.0f, 1.0f, 0.5f, 1.0f);
        child.Transform.Parent = parent.Transform;
        child.Transform.LocalPosition = new Float3(0, 2, 0);

        // Add Rigidbody3D to child (should claim its own collider)
        Rigidbody3D childRb = child.AddComponent<Rigidbody3D>();
        childRb.Mass = 1.0f;

        // Add collider to child
        BoxCollider childCollider = child.AddComponent<BoxCollider>();
        childCollider.Size = new Float3(1, 1, 1);

        scene.Add(child);

        // Store reference for test case 6
        testCase5Child = child;

        Debug.Log("  ✓ Parent and child both have Rigidbody3D - each should claim their own collider and fall independently");
        Debug.Log("  ✓ After 5 seconds, child's Rigidbody3D will be removed for TEST CASE 6");
    }

    private void HandleRigidbodyDragging()
    {
        Camera camera = cameraGO.GetComponent<Camera>();
        if (!camera.IsValid() || scene?.Physics == null)
            return;

        // On mouse down, try to grab a rigidbody
        if (Input.GetMouseButtonDown(0))
        {
            // Cast a ray from the mouse position
            Float2 mousePos = (Float2)Input.MousePosition;
            Ray ray = camera.ScreenPointToRay(mousePos, new Float2(Window.Size.X, Window.Size.Y));

            // Raycast to find a rigidbody
            if (scene.Physics.Raycast(ray.Origin, ray.Direction, out RaycastHit hitInfo))
            {
                // Check if we hit a rigidbody
                if (hitInfo.Rigidbody.IsValid() && !hitInfo.Rigidbody.IsStatic)
                {
                    grabbedRigidbody = hitInfo.Rigidbody;
                    grabDistance = Float3.Distance(ray.Origin, hitInfo.Point);

                    // Calculate offset from rigidbody center to hit point
                    Float3 hitWorldPos = hitInfo.Point;
                    grabOffset = hitWorldPos - grabbedRigidbody.Transform.Position;

                    Debug.Log($"Grabbed rigidbody: {grabbedRigidbody.GameObject.Name}");
                }
            }
        }

        // While holding mouse, apply forces to move rigidbody to mouse position
        if (Input.GetMouseButton(0) && grabbedRigidbody.IsValid())
        {
            // Cast a ray to get the target position
            Float2 mousePos = (Float2)Input.MousePosition;
            Ray ray = camera.ScreenPointToRay(mousePos, new Float2(Window.Size.X, Window.Size.Y));

            // Calculate target position at the original grab distance
            Float3 targetPos = ray.Origin + ray.Direction * grabDistance;

            // Account for the grab offset
            targetPos -= grabOffset;

            // Calculate force to apply (spring-like force)
            Float3 currentPos = grabbedRigidbody.Transform.Position;
            Float3 force = (targetPos - currentPos) * 50.0f; // Spring constant

            // Apply force
            grabbedRigidbody.AddForce(force);

            // Add damping to prevent oscillation
            Float3 velocity = grabbedRigidbody.LinearVelocity;
            grabbedRigidbody.AddForce(-velocity * 5.0f); // Damping
        }

        // On mouse release, stop grabbing
        if (Input.GetMouseButtonUp(0))
        {
            if (grabbedRigidbody.IsValid())
            {
                Debug.Log($"Released rigidbody: {grabbedRigidbody.GameObject.Name}");
                grabbedRigidbody = null;
            }
        }
    }

    public override void EndUpdate()
    {
        timeSinceStart += Time.DeltaTime;

        // TEST CASE 6: Remove child rigidbody after 5 seconds
        if (timeSinceStart > 5.0 && testCase5Child.IsValid())
        {
            Debug.Log("TEST CASE 6: Removing child's Rigidbody3D - collider should attach to parent");

            Rigidbody3D childRb = testCase5Child.GetComponent<Rigidbody3D>();
            if (childRb.IsValid())
            {
                testCase5Child.RemoveComponent(childRb);
                Debug.Log("  ✓ Child Rigidbody3D removed - collider should now attach to parent Rigidbody3D");

                // Null out so we don't do this again
                testCase5Child = null;
            }
        }

        // Click-and-drag rigidbodies with left mouse button
        HandleRigidbodyDragging();

        // Camera movement
        Float2 movement = Float2.Zero;
        if (Input.GetKey(KeyCode.W)) movement += Float2.UnitY;
        if (Input.GetKey(KeyCode.S)) movement -= Float2.UnitY;
        if (Input.GetKey(KeyCode.A)) movement -= Float2.UnitX;
        if (Input.GetKey(KeyCode.D)) movement += Float2.UnitX;

        // forward/back
        cameraGO.Transform.Position += cameraGO.Transform.Forward * movement.Y * 10f * Time.DeltaTime;
        // left/right
        cameraGO.Transform.Position += cameraGO.Transform.Right * movement.X * 10f * Time.DeltaTime;

        // up/down
        float upDown = 0;
        if (Input.GetKey(KeyCode.E)) upDown += 1;
        if (Input.GetKey(KeyCode.Q)) upDown -= 1;
        cameraGO.Transform.Position += Float3.UnitY * upDown * 10f * Time.DeltaTime;

        // rotate with mouse
        if (Input.GetMouseButton(1))
        {
            Float2 delta = Input.MouseDelta;
            cameraGO.Transform.LocalEulerAngles += new Float3(delta.Y, delta.X, 0) * 0.1f;
        }

        // Reset scene with R key
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("=== RESETTING SCENE ===");
            timeSinceStart = 0;
            scene.Clear();
            Initialize();
        }

        // Print help with H key
        if (Input.GetKeyDown(KeyCode.H))
        {
            Debug.Log("=== CONTROLS ===");
            Debug.Log("Left Mouse - Click and drag rigidbodies");
            Debug.Log("Right Mouse - Look around");
            Debug.Log("WASD - Move camera");
            Debug.Log("E/Q - Move camera up/down");
            Debug.Log("R - Reset scene");
            Debug.Log("H - Show this help");
        }
    }
}
