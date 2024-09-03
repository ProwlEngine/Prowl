// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using BepuPhysics;
using BepuPhysics.Collidables;

using Prowl.Icons;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Cubes}  Rigidbody")]
    public class Rigidbody : PhysicsBody
    {
        [SerializeField, HideInInspector] private bool _kinematic = false;
        [SerializeField, HideInInspector] private ContinuousDetection _continuous = ContinuousDetection.Discrete;
        [SerializeField, HideInInspector] private float _sleepThreshold = 0.01f;
        [SerializeField, HideInInspector] private byte _minimumTimestepCountUnderThreshold = 32;
        [SerializeField, HideInInspector] private InterpolationMode _interpolationMode = InterpolationMode.None;

        /// <summary> Can be null when it isn't part of a simulation yet/anymore </summary>
        internal BodyReference? BodyReference { get; private set; }

        internal RigidPose PreviousPose, CurrentPose;

        private uint _transformVersion = 1;

        [ShowInInspector]
        public virtual bool Kinematic
        {
            get => _kinematic;
            set
            {
                if (_kinematic == value)
                    return;

                _kinematic = value;
                if (BodyReference is { } bRef)
                {
                    bRef.LocalInertia = Kinematic ? new BodyInertia() : _nativeIntertia;
                }
            }
        }

        public float SleepThreshold
        {
            get => _sleepThreshold;
            set
            {
                if (_sleepThreshold == value)
                    return;

                _sleepThreshold = value;
                if (BodyReference is { } bRef)
                {
                    bRef.Activity.SleepThreshold = value;
                }
            }
        }

        public byte MinimumTimestepCountUnderThreshold
        {
            get => _minimumTimestepCountUnderThreshold;
            set
            {
                if (_minimumTimestepCountUnderThreshold == value)
                    return;

                _minimumTimestepCountUnderThreshold = value;
                if (BodyReference is { } bRef)
                {
                    bRef.Activity.MinimumTimestepsUnderThreshold = value;
                }
            }
        }

        [ShowInInspector]
        public InterpolationMode InterpolationMode
        {
            get => _interpolationMode;
            set => _interpolationMode = value;
        }

        /// <summary>
        /// Shortcut to <see cref="ContinuousDetection"/>.<see cref="ContinuousDetection.Mode"/>
        /// </summary>
        [ShowInInspector]
        public ContinuousDetectionMode ContinuousDetectionMode
        {
            get => _continuous.Mode;
            set
            {
                if (_continuous.Mode == value)
                    return;

                _continuous = value switch
                {
                    ContinuousDetectionMode.Discrete => ContinuousDetection.Discrete,
                    ContinuousDetectionMode.Passive => ContinuousDetection.Passive,
                    ContinuousDetectionMode.Continuous => ContinuousDetection.Continuous(),
                    _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
                };
            }
        }

        public bool Awake
        {
            get => BodyReference?.Awake ?? false;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.Awake = value;
            }
        }

        public Vector3 LinearVelocity
        {
            get => BodyReference?.Velocity.Linear ?? default;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.Velocity.Linear = value;
            }
        }

        public Vector3 AngularVelocity
        {
            get => BodyReference?.Velocity.Angular ?? default;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.Velocity.Angular = value;
            }
        }

        public Vector3 Position
        {
            get => BodyReference?.Pose.Position ?? default;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.Pose.Position = value;
            }
        }

        public Quaternion Orientation
        {
            get => BodyReference?.Pose.Orientation ?? Quaternion.identity;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.Pose.Orientation = value;
            }
        }

        public BodyInertia BodyInertia
        {
            get => BodyReference?.LocalInertia ?? default;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.LocalInertia = value;
            }
        }

        [ShowInInspector]
        public float SpeculativeMargin
        {
            get => BodyReference?.Collidable.SpeculativeMargin ?? default;
            set
            {
                if (BodyReference is { } bodyRef)
                    bodyRef.Collidable.SpeculativeMargin = value;
            }
        }

        [ShowInInspector]
        public ContinuousDetection ContinuousDetection
        {
            get => _continuous;
            set
            {
                _continuous = value;
                if (BodyReference is { } bodyRef)
                    bodyRef.Collidable.Continuity = _continuous;
            }
        }

        public float Mass => _mass;

        public override void OnEnable()
        {
            base.OnEnable();
            _transformVersion = this.Transform.version;
        }

        public void ApplyImpulse(Vector3 impulse, Vector3 impulseOffset)
        {
            BodyReference?.ApplyImpulse(impulse, impulseOffset);
        }

        public void ApplyAngularImpulse(Vector3 impulse)
        {
            BodyReference?.ApplyAngularImpulse(impulse);
        }

        public void ApplyLinearImpulse(Vector3 impulse)
        {
            BodyReference?.ApplyLinearImpulse(impulse);
        }

        protected override ref PhysicsMaterial MaterialProperties => ref Physics.CollidableMaterials[BodyReference!.Value];
        protected internal override RigidPose? Pose => BodyReference?.Pose;

        private BodyInertia _nativeIntertia;
        private float _mass;

        protected override void AttachInner(RigidPose containerPose, BodyInertia shapeInertia, TypedIndex shapeIndex)
        {
            Debug.Assert(Physics.IsReady);

            _nativeIntertia = shapeInertia;
            _mass = 1f / shapeInertia.InverseMass;
            if (Kinematic)
                shapeInertia = new BodyInertia();

            var bDescription = BodyDescription.CreateDynamic(containerPose, shapeInertia, shapeIndex, new(SleepThreshold, MinimumTimestepCountUnderThreshold));

            if (BodyReference is { } bRef)
            {
                bRef.GetDescription(out var previousDesc);
                bDescription.Velocity = previousDesc.Velocity; //Keep velocity when updating
                bRef.ApplyDescription(bDescription);
            }
            else
            {
                var bHandle = Physics.Sim.Bodies.Add(bDescription);
                BodyReference = Physics.Sim.Bodies[bHandle];
                BodyReference.Value.Collidable.Continuity = ContinuousDetection;

                while (Physics.Bodies.Count <= bHandle.Value) // There may be more than one add if soft physics inserted a couple of bodies
                    Physics.Bodies.Add(null);
                Physics.Bodies[bHandle.Value] = this;

                Physics.CollidableMaterials.Allocate(bHandle) = new();
            }

            RigidbodyAttached();
        }

        protected virtual void RigidbodyAttached() { }

        protected override void DetachInner()
        {
            Debug.Assert(Physics.IsReady);

            if (BodyReference == null)
                return;

            Physics.Sim.Bodies.Remove(BodyReference.Value.Handle);
            Physics.Bodies[BodyReference.Value.Handle.Value] = null;

            RigidbodyDetached();

            BodyReference = null;

        }

        protected virtual void RigidbodyDetached() { }

        public void SyncTransform()
        {
            if (this.Transform.version != _transformVersion)
            {
                Position = this.Transform.position;
                Orientation = this.Transform.rotation;
                LinearVelocity = Vector3.zero;
                AngularVelocity = Vector3.zero;
                Awake = true;
                _transformVersion = this.Transform.version;
            }
        }

        protected override void RegisterContactHandler()
        {
            if (Physics.IsReady && ContactEventHandler is not null && BodyReference is { } bRef)
                Physics.ContactEvents.Register(bRef.Handle, ContactEventHandler);
        }

        protected override void UnregisterContactHandler()
        {
            if (Physics.IsReady && BodyReference is { } bRef)
                Physics.ContactEvents.Unregister(bRef.Handle);
        }

        protected override bool IsContactHandlerRegistered()
        {
            if (Physics.IsReady && BodyReference is { } bRef)
                return Physics.ContactEvents.IsListener(bRef.Handle);
            return false;
        }
    }
}
