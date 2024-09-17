// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using BepuPhysics;

using Prowl.Icons;

namespace Prowl.Runtime;

[RequireComponent(typeof(CapsuleCollider))]
[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Person}  Character Controller")]
public sealed class CharacterController : Rigidbody
{
    public float speed = 4f;
    public readonly float jumpVelocity = 6f;
    public readonly float maxSlope = (MathF.PI * 0.25f).ToDeg();
    public readonly float maxVerticalForce = 100;
    public readonly float maxHoriztonalForce = 20;
    public readonly float supportDepth = -0.05f;
    public readonly float supportContinuationDepth = -0.1f;

    [ShowInInspector]
    public Vector2 TargetVelocity { get; set; } = Vector2.zero;
    public bool IsGrounded { get; private set; } = false;

    public override bool Kinematic
    {
        get => false;
        set => Debug.LogWarning("CharacterController cannot be kinematic");
    }

    protected override void RigidbodyAttached()
    {
        ref var character = ref Physics.Characters.AllocateCharacter(BodyReference!.Value.Handle);
        character.LocalUp = new Vector3(0, 1, 0);
        character.JumpVelocity = jumpVelocity;
        character.MaximumVerticalForce = maxVerticalForce;
        character.MaximumHorizontalForce = maxHoriztonalForce;
        character.MinimumSupportDepth = supportDepth;
        character.MinimumSupportContinuationDepth = supportContinuationDepth;
        character.CosMaximumSlope = MathF.Cos(maxSlope.ToRad());

        character.TargetVelocity = TargetVelocity;

        BodyReference.Value.SetLocalInertia(new BodyInertia { InverseMass = 1f / Mass });
    }

    protected override void RigidbodyDetached()
    {
        Physics.Characters.RemoveCharacterByBodyHandle(BodyReference!.Value.Handle);
    }

    public override void Update()
    {
        ref var character = ref Physics.Characters.GetCharacterByBodyHandle(BodyReference!.Value.Handle);

        character.CosMaximumSlope = MathF.Cos(maxSlope.ToRad());
        character.JumpVelocity = jumpVelocity;

        if (!BodyReference.Value.Awake &&
            ((character.TryJump && character.Supported) ||
            TargetVelocity.ToFloat() != character.TargetVelocity ||
            (TargetVelocity != Vector2.zero && character.ViewDirection != Transform.forward.ToFloat())))
        {
            Physics.Sim.Awakener.AwakenBody(character.BodyHandle);
        }

        character.ViewDirection = Transform.forward;
        character.TargetVelocity = TargetVelocity;
        IsGrounded = character.Supported;

        if (!base.Kinematic)
            BodyReference.Value.LocalInertia = new BodyInertia { InverseMass = 1f / Mass };
    }

    public void TryJump() => Physics.Characters.GetCharacterByBodyHandle(BodyReference!.Value.Handle).TryJump = true;
}
