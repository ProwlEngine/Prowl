using BepuPhysics;
using BepuPhysics.Collidables;
using System;

namespace Prowl.Runtime;

[RequireComponent(typeof(CapsuleCollider))]
public sealed class CharacterController : MonoBehaviour
{
    public float speed = 4f;
    public float jumpVelocity = 6f;
    public float maxSlope = (MathF.PI * 0.25f).ToDeg();

    public BodyHandle BodyHandle { get; private set; }

    public Vector2 TargetVelocity { get; set; } = Vector2.zero;
    public bool IsGrounded { get; private set; } = false;
    
    public override void OnEnable()
    {
        CapsuleCollider collider = GetComponent<CapsuleCollider>()!;
        if (collider.shape == null)
            collider.CreateShape();

        BodyHandle = Physics.Sim.Bodies.Add(
            BodyDescription.CreateDynamic(this.Transform.position.ToFloat(), new BodyInertia { InverseMass = 1f / collider.mass },
            new(collider.shapeIndex!.Value, 0.1f, float.MaxValue, ContinuousDetection.Passive), collider.radius * 0.02f));
        ref var character = ref Physics.Characters.AllocateCharacter(BodyHandle);
        character.LocalUp = new Vector3(0, 1, 0);
        character.JumpVelocity = jumpVelocity;
        character.MaximumVerticalForce = 100;
        character.MaximumHorizontalForce = 20;
        character.MinimumSupportDepth = collider.radius * -0.01f;
        character.MinimumSupportContinuationDepth = -0.1f;
        character.CosMaximumSlope = MathF.Cos(maxSlope.ToRad());

        character.TargetVelocity = TargetVelocity;
    }

    public override void OnDisable()
    {
        Physics.Sim.Shapes.Remove(new BodyReference(BodyHandle, Physics.Sim.Bodies).Collidable.Shape);
        Physics.Sim.Bodies.Remove(BodyHandle);
        Physics.Characters.RemoveCharacterByBodyHandle(BodyHandle);
    }

    public override void Update()
    {
        ref var character = ref Physics.Characters.GetCharacterByBodyHandle(BodyHandle);
        var characterBody = new BodyReference(BodyHandle, Physics.Sim.Bodies);

        character.CosMaximumSlope = MathF.Cos(maxSlope.ToRad());
        character.JumpVelocity = jumpVelocity;

        if (!characterBody.Awake &&
            ((character.TryJump && character.Supported) ||
            TargetVelocity.ToFloat() != character.TargetVelocity ||
            (TargetVelocity != Vector2.zero && character.ViewDirection != this.Transform.forward.ToFloat())))
        {
            Physics.Sim.Awakener.AwakenBody(character.BodyHandle);
        }

        character.ViewDirection = this.Transform.forward;
        character.TargetVelocity = TargetVelocity;
        IsGrounded = character.Supported;
    }

    private uint lastVersion = 0;
    public override void LateUpdate()
    {
        var body = new BodyReference(BodyHandle, Physics.Sim.Bodies);

        if (lastVersion != this.GameObject.Transform.version)
        {
            body.Pose.Position = this.GameObject.Transform.position;
            body.Pose.Orientation = this.GameObject.Transform.rotation;
            body.Velocity.Linear = Vector3.zero;
            body.Velocity.Angular = Vector3.zero;
            body.Awake = true;
            lastVersion = this.GameObject.Transform.version;
        }

        this.GameObject.Transform.position = body.Pose.Position;
        this.GameObject.Transform.rotation = body.Pose.Orientation;
        lastVersion = this.GameObject.Transform.version;
    }

    public void TryJump() => Physics.Characters.GetCharacterByBodyHandle(BodyHandle).TryJump = true;
}
