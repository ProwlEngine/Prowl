// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//
// VR Basics
//
// The least it takes to stand in a world with a headset on. A camera follows the headset, two hands follow
// the controllers, and squeezing the grip near a block picks it up. Everything else, walking, physics bodies,
// guns and the rest, lives in the VR Showcase.
//
//   Grip          Pick up and let go
//   Trigger       Brightens the hand, to show the trigger reading
//   Right stick   Snap turn
//

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace VRBasics;

internal class Program
{
    static void Main(string[] args)
    {
        new VRBasicsGame().Run("VR Basics", 1280, 720);
    }
}

public sealed class VRBasicsGame : Game
{
    public override void Initialize()
    {
        var scene = new Scene();

        var sun = new GameObject("Sun");
        sun.AddComponent<DirectionalLight>().Intensity = 0.6f;
        sun.Transform.LocalEulerAngles = new Float3(50f, 30f, 0f);
        scene.Add(sun);

        var floor = new GameObject("Floor");
        floor.Transform.Position = new Float3(0f, -0.5f, 0f);
        floor.AddComponent<MeshRenderer>().Mesh = Mesh.CreateCube(new Float3(20f, 1f, 20f));
        floor.GetComponent<MeshRenderer>()!.Material = Lit(new Color(0.12f, 0.13f, 0.15f, 1f));
        floor.AddComponent<BoxCollider>().Size = new Float3(20f, 1f, 20f);
        scene.Add(floor);

        // Blocks on a table in front of the player.
        var table = new GameObject("Table");
        table.Transform.Position = new Float3(0f, 0.4f, 0.8f);
        table.AddComponent<MeshRenderer>().Mesh = Mesh.CreateCube(new Float3(1.2f, 0.8f, 0.6f));
        table.GetComponent<MeshRenderer>()!.Material = Lit(new Color(0.16f, 0.08f, 0.035f, 1f));
        table.AddComponent<BoxCollider>().Size = new Float3(1.2f, 0.8f, 0.6f);
        scene.Add(table);

        for (int i = 0; i < 5; i++)
        {
            var block = new GameObject("Block");
            block.Transform.Position = new Float3(-0.4f + i * 0.2f, 0.9f, 0.8f);
            block.AddComponent<MeshRenderer>().Mesh = Mesh.CreateCube(new Float3(0.1f));
            block.GetComponent<MeshRenderer>()!.Material = Lit(new Color(0.6f - i * 0.1f, 0.2f, 0.05f + i * 0.1f, 1f));
            block.AddComponent<BoxCollider>().Size = new Float3(0.1f);
            var body = block.AddComponent<Rigidbody3D>();
            body.Mass = 0.3f;
            SimpleHand.Grabbables.Add(body);
            scene.Add(block);
        }

        // The player: a tracking space with the head and hands in it.
        var rig = new GameObject("Rig");
        rig.AddComponent<SnapTurn>();

        var head = new GameObject("Head");
        head.SetParent(rig);
        head.Transform.LocalPosition = new Float3(0f, 1.65f, 0f);
        var camera = head.AddComponent<Camera>();
        camera.NearClipPlane = 0.05f;
        camera.HDR = true;
        camera.Effects = [new TonemapperEffect()];
        head.AddComponent<TrackedPoseDriver>().Node = XRNode.Head;

        foreach (XRHand side in new[] { XRHand.Left, XRHand.Right })
        {
            var hand = new GameObject(side + " Hand");
            hand.SetParent(rig);
            hand.AddComponent<TrackedPoseDriver>().Node = side == XRHand.Left ? XRNode.LeftHand : XRNode.RightHand;
            hand.AddComponent<MeshRenderer>().Mesh = Mesh.CreateCube(new Float3(0.06f, 0.04f, 0.12f));
            hand.AddComponent<SimpleHand>().Hand = side;
        }
        scene.Add(rig);

        XR.Start();
        Scene.Load(scene);
    }

    public static Material Lit(Color color)
    {
        var material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        material.SetColor("_MainColor", color);
        return material;
    }
}

/// <summary>Turns the rig 45 degrees about the head each time the right stick is pushed sideways.</summary>
public sealed class SnapTurn : Component
{
    private bool _armed = true;

    public override void Update()
    {
        float x = XRInput.GetThumbstick(XRHand.Right).X;
        XRPose head = XR.GetPose(XRNode.Head);
        if (_armed && MathF.Abs(x) > 0.75f && head.HasPosition)
        {
            Float3 pivot = Transform.TransformPoint(head.Position);
            Transform.Rotation = Quaternion.AxisAngle(Float3.UnitY, MathF.Sign(x) * 45f * Maths.Deg2Rad) * Transform.Rotation;
            Transform.Position += pivot - Transform.TransformPoint(head.Position);
            _armed = false;
        }
        else if (MathF.Abs(x) < 0.3f)
        {
            _armed = true;
        }
    }
}

/// <summary>A box on a controller that lights up with the trigger and carries a block while the grip is held.</summary>
public sealed class SimpleHand : Component
{
    public static readonly List<Rigidbody3D> Grabbables = new();

    public XRHand Hand;
    public float Reach = 0.12f;

    private Rigidbody3D? _held;
    private Float3 _offset;
    private Quaternion _rotation;
    private Material _material = null!;

    public override void OnEnable()
    {
        _material = VRBasicsGame.Lit(new Color(0.5f, 0.35f, 0.25f, 1f));
        GetComponent<MeshRenderer>()!.Material = _material;
    }

    public override void Update()
    {
        float trigger = XRInput.GetTrigger(Hand);
        _material.SetColor("_MainColor", new Color(0.5f + trigger * 0.5f, 0.35f + trigger * 0.4f, 0.25f, 1f));

        // Without a running headset the grip can never be seen letting go, so the block is let go here.
        if (!XR.IsRunning || !XRInput.IsTracked(Hand)) Release();
        else if (XRInput.GetButtonDown(Hand, XRButton.Grip)) Grab();
        else if (XRInput.GetButtonUp(Hand, XRButton.Grip)) Release();
    }

    private void Grab()
    {
        float best = Reach;
        foreach (Rigidbody3D body in Grabbables)
        {
            float distance = Float3.Distance(Transform.Position, body.Position);
            if (body.IsValid() && distance < best)
            {
                best = distance;
                _held = body;
            }
        }
        if (_held.IsNotValid()) return;

        _offset = Transform.InverseTransformPoint(_held.Position);
        _rotation = Quaternion.Inverse(Transform.Rotation) * _held.Rotation;
        _held.AffectedByGravity = false;
    }

    private void Release()
    {
        if (_held.IsValid()) _held.AffectedByGravity = true;
        _held = null;
    }

    /// <summary>
    /// Gives the held block whatever velocity and spin carry it to the hand this step, capped so a block caught
    /// behind something cannot be flung, so it still collides and leaves the hand spinning when thrown.
    /// </summary>
    public override void FixedUpdate()
    {
        if (_held.IsNotValid()) return;
        float dt = Time.FixedDeltaTime;

        _held.LinearVelocity = Limit((Transform.TransformPoint(_offset) - _held.Position) / dt, 20f);

        Quaternion delta = Quaternion.Normalize(Transform.Rotation * _rotation * Quaternion.Inverse(_held.Rotation));
        if (delta.W < 0f) delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        float angle = 2f * MathF.Acos(Maths.Clamp(delta.W, -1f, 1f));
        float sine = MathF.Sqrt(MathF.Max(0f, 1f - delta.W * delta.W));
        Float3 axis = sine > 1e-4f ? new Float3(delta.X, delta.Y, delta.Z) / sine : Float3.Zero;
        _held.AngularVelocity = Limit(axis * (angle / dt), 30f);
    }

    private static Float3 Limit(Float3 v, float max)
    {
        float length = Float3.Length(v);
        return length > max ? v * (max / length) : v;
    }
}
