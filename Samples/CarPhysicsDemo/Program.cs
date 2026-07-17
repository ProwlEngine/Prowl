// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Dynamics;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace CarPhysicsDemo;

internal class Program
{
    static void Main(string[] args)
    {
        new CarPhysicsGame().Run("Car Physics Demo - WheelCollider", 1280, 720);
    }
}

public sealed class CarPhysicsGame : Game
{
    private Scene? scene;
    private Material? standardMaterial;
    private GameObject? carGO;
    private GameObject? cameraGO;
    private ThirdPersonCamera? thirdPersonCamera;
    private CarController? carController;

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
        Camera camera = cam.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;
        cameraGO = cam;


        scene.Add(cam);

        // Create shared material
        standardMaterial = new Material(Shader.LoadDefault(DefaultShader.Standard));

        // Create large ground with ramps and bumps
        CreateGround();
        CreateRampsAndObstacles();

        // Create car
        CreateCar();

        // Setup third-person camera
        thirdPersonCamera = cam.AddComponent<ThirdPersonCamera>();
        thirdPersonCamera.Target = carGO;
        thirdPersonCamera.Distance = 15f;
        thirdPersonCamera.Height = 10f;
        thirdPersonCamera.Smoothness = 5f;

        Scene.Load(scene);

        Debug.Log("Controls:");
        Debug.Log("  WASD - Drive car");
        Debug.Log("  Space - Brake");
        Debug.Log("  Right Mouse - Look around camera");
        Debug.Log("  R - Reset car position");
    }

    private void CreateGround()
    {
        GameObject ground = new("Ground");
        MeshRenderer groundRenderer = ground.AddComponent<MeshRenderer>();
        groundRenderer.Mesh = Mesh.CreateCube(new Float3(200, 1, 200));
        groundRenderer.Material = standardMaterial;
        ground.Transform.Position = new Float3(0, -0.5f, 0);

        Rigidbody3D groundRigidbody = ground.AddComponent<Rigidbody3D>();
        groundRigidbody.MotionType = MotionType.Static;
        BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
        groundCollider.Size = new Float3(200, 1, 200);

        scene.Add(ground);
    }

    private void CreateRampsAndObstacles()
    {
        // Create several ramps at different angles
        CreateRamp(new Float3(-20, 0, 30), new Float3(10, 0.5f, 15), 15); // Gentle ramp
        CreateRamp(new Float3(20, 0, 30), new Float3(10, 0.5f, 15), 30); // Steeper ramp
        CreateRamp(new Float3(0, 0, -40), new Float3(15, 0.5f, 10), -20); // Ramp going down

        // Create bumps scattered around
        CreateBump(new Float3(-30, 0, 0), 2f, new Color(0.7f, 0.5f, 0.3f, 1.0f));
        CreateBump(new Float3(-25, 0, -10), 1.5f, new Color(0.6f, 0.6f, 0.4f, 1.0f));
        CreateBump(new Float3(30, 0, 5), 2.5f, new Color(0.8f, 0.4f, 0.3f, 1.0f));
        CreateBump(new Float3(35, 0, -5), 1.8f, new Color(0.5f, 0.5f, 0.5f, 1.0f));
        CreateBump(new Float3(0, 0, 20), 2.2f, new Color(0.7f, 0.6f, 0.4f, 1.0f));

        // Create some walls/obstacles
        CreateWall(new Float3(-50, 1, 0), new Float3(2, 3, 20), new Color(0.6f, 0.3f, 0.2f, 1.0f));
        CreateWall(new Float3(50, 1, 0), new Float3(2, 3, 20), new Color(0.6f, 0.3f, 0.2f, 1.0f));
        CreateWall(new Float3(0, 1, -60), new Float3(40, 3, 2), new Color(0.6f, 0.3f, 0.2f, 1.0f));
    }

    private void CreateRamp(Float3 position, Float3 size, float angleDegrees)
    {
        GameObject ramp = new("Ramp");
        MeshRenderer rampRenderer = ramp.AddComponent<MeshRenderer>();
        rampRenderer.Mesh = Mesh.CreateCube(size);
        rampRenderer.Material = standardMaterial;
        //rampRenderer.MainColor = new Color(0.5f, 0.5f, 0.6f, 1.0f);

        ramp.Transform.Position = position + new Float3(0, size.Y * Maths.Sin(angleDegrees * Maths.PI / 180.0f) / 2, 0);
        ramp.Transform.LocalEulerAngles = new Float3(angleDegrees, 0, 0);

        Rigidbody3D rampRigidbody = ramp.AddComponent<Rigidbody3D>();
        rampRigidbody.MotionType = MotionType.Static;
        BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
        rampCollider.Size = size;

        scene.Add(ramp);
    }

    private void CreateBump(Float3 position, float radius, Color color)
    {
        GameObject bump = new("Bump");
        MeshRenderer bumpRenderer = bump.AddComponent<MeshRenderer>();
        bumpRenderer.Mesh = Mesh.CreateSphere((float)radius, 16, 16);
        bumpRenderer.Material = standardMaterial;
        //bumpRenderer.MainColor = color;

        bump.Transform.Position = position + new Float3(0, radius / 2, 0);

        Rigidbody3D bumpRigidbody = bump.AddComponent<Rigidbody3D>();
        bumpRigidbody.MotionType = MotionType.Static;
        SphereCollider bumpCollider = bump.AddComponent<SphereCollider>();
        bumpCollider.Radius = radius;

        scene.Add(bump);
    }

    private void CreateWall(Float3 position, Float3 size, Color color)
    {
        GameObject wall = new("Wall");
        MeshRenderer wallRenderer = wall.AddComponent<MeshRenderer>();
        wallRenderer.Mesh = Mesh.CreateCube(size);
        wallRenderer.Material = standardMaterial;
        //wallRenderer.MainColor = color;

        wall.Transform.Position = position;

        Rigidbody3D wallRigidbody = wall.AddComponent<Rigidbody3D>();
        wallRigidbody.MotionType = MotionType.Static;
        BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
        wallCollider.Size = size;

        scene.Add(wall);
    }

    private void CreateCar()
    {
        // Create car body
        carGO = new GameObject("Car");
        MeshRenderer carRenderer = carGO.AddComponent<MeshRenderer>();
        carRenderer.Mesh = Mesh.CreateCube(new Float3(2, 0.5f, 4));
        carRenderer.Material = standardMaterial;
        //carRenderer.MainColor = new Color(0.8f, 0.2f, 0.2f, 1.0f); // Red car

        carGO.Transform.Position = new Float3(0, 2, 0);

        // Add rigidbody
        Rigidbody3D carRigidbody = carGO.AddComponent<Rigidbody3D>();
        carRigidbody.Mass = 1000.0f; // Heavy car
        carRigidbody.LinearDamping = 0.0001f;
        carRigidbody.AngularDamping = 0.0001f;

        // Add collider for car body
        BoxCollider carCollider = carGO.AddComponent<BoxCollider>();
        carCollider.Size = new Float3(2, 0.5f, 4);

        // Add car controller
        carController = carGO.AddComponent<CarController>();

        scene.Add(carGO);

        // Create 4 wheels as children
        CreateWheel("FrontLeft", new Float3(-1.0f, -0.25f, 1.5f), carGO, true);
        CreateWheel("FrontRight", new Float3(1.0f, -0.25f, 1.5f), carGO, true);
        CreateWheel("RearLeft", new Float3(-1.0f, -0.25f, -1.5f), carGO, false);
        CreateWheel("RearRight", new Float3(1.0f, -0.25f, -1.5f), carGO, false);
    }

    private void CreateWheel(string name, Float3 localPosition, GameObject parent, bool isFront)
    {
        GameObject wheel = new(name);
        wheel.Transform.Parent = parent.Transform;
        wheel.Transform.LocalPosition = localPosition;

        // Visual representation of wheel
        GameObject visual = new("Visual");
        visual.Transform.Parent = wheel.Transform;
        visual.Transform.LocalPosition = new Float3(0, 0, 0);
        visual.Transform.LocalEulerAngles = new Float3(0, 0, 0);
        GameObject visualMesh = new("VisualMesh");
        visualMesh.Transform.Parent = visual.Transform;
        visualMesh.Transform.LocalPosition = new Float3(0, 0, 0);
        visualMesh.Transform.LocalEulerAngles = new Float3(0, 0, 90);

        MeshRenderer wheelRenderer = visualMesh.AddComponent<MeshRenderer>();
        wheelRenderer.Mesh = Mesh.CreateCylinder(0.5f, 0.3f, 16);
        wheelRenderer.Material = standardMaterial;

        // Add wheel collider. Suspension is tuned from a natural frequency + damping ratio (sprung mass
        // auto-divides the body mass across the wheels), and grip is the friction-ellipse limits.
        WheelCollider wheelCollider = wheel.AddComponent<WheelCollider>();
        wheelCollider.Radius = 0.5f;
        wheelCollider.Width = 0.3f;
        wheelCollider.SuspensionDistance = 0.25f;
        wheelCollider.SuspensionFrequency = 2.0f;
        wheelCollider.SuspensionDampingRatio = 0.7f;
        wheelCollider.ForwardFriction = 2.5f;
        wheelCollider.SidewaysFriction = 1.2f;
        wheelCollider.visualTransform = visual.Transform;

        scene.Add(wheel);

        // Register wheel with car controller
        if (carController != null)
        {
            if (isFront)
                carController.frontWheels.Add(wheelCollider);
            else
                carController.rearWheels.Add(wheelCollider);
        }
    }

    public override void EndUpdate()
    {
        //Time.FixedDeltaTime = Time.DeltaTime;

        //// Reset car if R is pressed
        //if (Input.GetKeyDown(KeyCode.R))
        //{
        //    carGO.Transform.Position = new Float3(0, 2, 0);
        //    carGO.Transform.Rotation = Quaternion.Identity;
        //
        //    var rb = carGO.GetComponent<Rigidbody3D>();
        //    if (rb != null)
        //    {
        //        rb.LinearVelocity = Float3.Zero;
        //        rb.AngularVelocity = Float3.Zero;
        //    }
        //
        //    Debug.Log("Car reset");
        //}
        // Shoot cube with left mouse button
        if (Input.GetMouseButton(0))
        {
            if(Time.FrameCount % 100 == 0) // Limit shooting rate
                ShootCube();
        }
    }

    Mesh cubeShootMesh = null;
    int shootCounter = 0;
    GameObject lastShot = null;
    private void ShootCube()
    {
        // Create a cube at camera position
        GameObject cube = new("Shot Cube");
        lastShot = cube;
        cube.Transform.Position = cameraGO.Transform.Position + cameraGO.Transform.Forward * 2.0f;

        MeshRenderer cubeRenderer = cube.AddComponent<MeshRenderer>();
        cubeShootMesh = cubeShootMesh.IsNotValid() ? Mesh.CreateCube(new Float3(0.5f, 0.5f, 0.5f)) : cubeShootMesh;
        cubeRenderer.Mesh = cubeShootMesh;
        cubeRenderer.Material = standardMaterial;

        Rigidbody3D cubeRb = cube.AddComponent<Rigidbody3D>();
        cubeRb.Mass = 250f;
        cubeRb.EnableSpeculativeContacts = true;

        BoxCollider cubeCollider = cube.AddComponent<BoxCollider>();
        cubeCollider.Size = new Float3(0.5f, 0.5f, 0.5f);

        //var light = cube.AddComponent<PointLight>();
        //light.ShadowQuality = ShadowQuality.Soft;
        //light.Intensity = 32;
        //light.Color = new Color(RNG.Shared.NextDouble(), RNG.Shared.NextDouble(), RNG.Shared.NextDouble(), 1f);
        //light.Transform.Rotation = cameraGO.Transform.Rotation;

        scene.Add(cube);

        // Add velocity in the direction the camera is facing
        cubeRb.LinearVelocity = cameraGO.Transform.Forward * 5.0f;


        shootCounter++;
        Debug.Log($"Shot cube #{shootCounter} with mass {25f}");
    }
}

// Third-person camera controller that follows the car
public class ThirdPersonCamera : MonoBehaviour
{
    public GameObject? Target;
    public float Distance = 10.0f;
    public float Height = 3.0f;
    public float Smoothness = 5.0f;
    public float MouseSensitivity = 0.1f;

    private float currentYaw = 0;
    private float currentPitch = 20;

    public override void Update()
    {
        if (Target == null || !Target.IsValid()) return;

        // Mouse look
        if (Input.GetMouseButton(1))
        {
            Float2 mouseDelta = Input.MouseDelta;
            currentYaw += mouseDelta.X * MouseSensitivity;
            currentPitch -= mouseDelta.Y * MouseSensitivity;
            currentPitch = Maths.Clamp(currentPitch, -30, 60); // Limit pitch
        }

        // Calculate desired camera position
        float yawRad = currentYaw * Maths.PI / 180.0f;
        float pitchRad = currentPitch * Maths.PI / 180.0f;

        float x = Maths.Sin(yawRad) * Maths.Cos(pitchRad);
        float y = Maths.Sin(pitchRad);
        float z = Maths.Cos(yawRad) * Maths.Cos(pitchRad);

        Float3 direction = new Float3(x, y, z);
        Float3 offset = -direction * Distance + new Float3(0, Height, 0);
        Float3 desiredPosition = Target.Transform.Position + offset;

        // Smooth camera movement (manual lerp)
        float lerpFactor = Maths.Clamp(Smoothness * Time.DeltaTime, 0, 1);
        Transform.Position = Transform.Position + (desiredPosition - Transform.Position) * lerpFactor;
        
        // Look at target
        Float3 lookDirection = Target.Transform.Position + new Float3(0, 1, 0) - Transform.Position;
        if (Float3.LengthSquared(lookDirection) > 0.001)
        {
            Transform.Rotation = Quaternion.LookRotation(lookDirection, Float3.UnitY);
        }
    }
}

// Car controller using WheelColliders
public class CarController : MonoBehaviour
{
    public List<WheelCollider> frontWheels = new();
    public List<WheelCollider> rearWheels = new();

    public float MaxSteerAngle = 30.0f; // degrees

    // Engine
    public float MaxEngineTorque = 320.0f; // N*m at the peak of the curve
    public float IdleRPM = 900.0f;
    public float MaxRPM = 6500.0f;

    // Gearbox. Wheel torque = engineTorque * gearRatio * finalDrive. High ratio (1st) = lots of torque,
    // low top speed (engine redlines early); low ratio (top) = less torque, high speed.
    public float[] GearRatios = { 3.6f, 2.2f, 1.5f, 1.15f, 0.9f };
    public float ReverseRatio = 3.6f;
    public float FinalDrive = 3.7f;
    public float ShiftUpRPM = 6000.0f;
    public float ShiftDownRPM = 2600.0f;
    public float DrivetrainEfficiency = 0.9f;

    public float BrakeTorque = 2000.0f;

    private int currentGear = 1; // -1 reverse, 0 neutral, 1..N forward
    private float engineRPM;
    private Rigidbody3D? rigidbody;

    public override void OnEnable()
    {
        rigidbody = GetComponent<Rigidbody3D>();
    }


    public override void Update()
    {
        if (rigidbody == null) return;

        float steering = 0;
        if (Input.GetKey(KeyCode.A)) steering = -1;
        if (Input.GetKey(KeyCode.D)) steering = 1;
        bool accel = Input.GetKey(KeyCode.W);
        bool decel = Input.GetKey(KeyCode.S);
        bool handbrake = Input.GetKey(KeyCode.Space);

        // Steering on the front wheels.
        float steerAngle = (float)(steering * MaxSteerAngle * Maths.PI / 180.0);
        foreach (var wheel in frontWheels)
            if (wheel != null && wheel.IsValid())
                wheel.SteerAngle = steerAngle;

        float forwardSpeed = Float3.Dot(rigidbody.LinearVelocity, Transform.Forward);

        // Throttle / brake / reverse. S brakes while rolling forward, then selects reverse once stopped.
        float throttle = 0.0f;
        bool braking = false;
        if (accel)
        {
            if (currentGear < 1) SetGear(1);
            throttle = 1.0f;
        }
        else if (decel)
        {
            if (forwardSpeed > 0.5f) braking = true;
            else { if (currentGear >= 0) SetGear(-1); throttle = 1.0f; }
        }
        else if (currentGear < 0 && Maths.Abs(forwardSpeed) < 0.5f) SetGear(1);

        // Average driven-wheel spin -> engine RPM through the current gear.
        float avgAV = 0.0f; int driven = 0;
        foreach (var wheel in rearWheels)
            if (wheel != null && wheel.IsValid()) { avgAV += wheel.AngularVelocity; driven++; }
        if (driven > 0) avgAV /= driven;

        float ratio = GearRatio(currentGear) * FinalDrive;
        engineRPM = EngineRPMFor(avgAV, ratio);

        // Automatic gearbox for the forward gears, then refresh ratio/RPM after any shift.
        if (currentGear >= 1)
        {
            if (engineRPM > ShiftUpRPM && currentGear < GearRatios.Length) SetGear(currentGear + 1);
            else if (engineRPM < ShiftDownRPM && currentGear > 1) SetGear(currentGear - 1);
            ratio = GearRatio(currentGear) * FinalDrive;
            engineRPM = EngineRPMFor(avgAV, ratio);
        }

        // Engine torque (from the curve) through the gearbox to the driven wheels.
        float engineTorque = MaxEngineTorque * EngineTorqueFactor(engineRPM) * throttle;
        float driveTorque = engineTorque * ratio * DrivetrainEfficiency;
        float perWheel = driven > 0 ? driveTorque / driven : 0.0f;

        float brakeT = braking ? BrakeTorque : 0.0f;
        float handbrakeT = handbrake ? BrakeTorque : 0.0f;

        foreach (var wheel in frontWheels)
        {
            if (wheel == null || !wheel.IsValid()) continue;
            wheel.MotorTorque = 0.0f;
            wheel.BrakeTorque = brakeT;
        }
        foreach (var wheel in rearWheels)
        {
            if (wheel == null || !wheel.IsValid()) continue;
            wheel.MotorTorque = perWheel;
            wheel.BrakeTorque = brakeT + handbrakeT;
        }

        if (Time.FrameCount % 30 == 0)
            Debug.Log($"Gear {(currentGear < 0 ? "R" : currentGear.ToString())}  {engineRPM:F0} rpm  {forwardSpeed * 3.6f:F0} km/h");
    }

    private float GearRatio(int gear) => gear == 0 ? 0.0f : gear < 0 ? -ReverseRatio : GearRatios[gear - 1];

    private float EngineRPMFor(float wheelAngularVelocity, float ratio)
    {
        float wheelRPM = wheelAngularVelocity * 60.0f / (2.0f * Maths.PI);
        return Maths.Clamp(Maths.Abs(wheelRPM * ratio), IdleRPM, MaxRPM);
    }

    private void SetGear(int gear)
    {
        if (gear == currentGear) return;
        currentGear = gear;
        Debug.Log($"Shift -> {(currentGear < 0 ? "Reverse" : currentGear == 0 ? "Neutral" : "Gear " + currentGear)}");
    }

    private float EngineTorqueFactor(float rpm)
    {
        float t = Maths.Clamp(rpm / MaxRPM, 0.0f, 1.0f);
        // Smooth curve peaking near 60% of redline, tapering toward idle and redline.
        float f = 1.0f - 1.25f * (t - 0.6f) * (t - 0.6f);
        return Maths.Clamp(f, 0.2f, 1.0f);
    }
}
