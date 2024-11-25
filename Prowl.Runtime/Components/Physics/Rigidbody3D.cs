// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using Prowl.Icons;
using Prowl.Runtime.Cloning;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Cubes}  Rigidbody3D")]
public sealed class Rigidbody3D : MonoBehaviour
{
    public class RigidBodyUserData
    {
        public Rigidbody3D Rigidbody { get; set; }
        public int Layer { get; set; }
        //public bool HasTransformConstraints { get; set; }
        //public JVector RotationConstraint { get; set; }
        //public JVector TranslationConstraint { get; set; }
    }

   [SerializeField] private bool isStatic;
   [SerializeField] private bool isSpeculative;
   [SerializeField] private bool useGravity = true;
   [SerializeField] private double mass = 1;
   [SerializeField, Range(0, 1)] private double friction = 0.2f;
   [SerializeField, Range(0, 1)] private double restitution = 0;
    //public Vector3Int translationConstraints = Vector3Int.one;
    //public Vector3Int rotationConstraints = Vector3Int.one;

    /// <summary>
    /// Gets or sets a value indicating whether this Rigidbody3D is static.
    /// </summary>
    public bool IsStatic
    {
        get => isStatic;
        set
        {
            isStatic = value;
            if (_body != null) _body.IsStatic = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether speculative contacts are enabled.
    /// </summary>
    public bool EnableSpeculativeContacts
    {
        get => isSpeculative;
        set
        {
            isSpeculative = value;
            if (_body != null) _body.EnableSpeculativeContacts = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this Rigidbody3D is affected by gravity.
    /// </summary>
    public bool AffectedByGravity
    {
        get => useGravity;
        set
        {
            useGravity = value;
            if (_body != null) _body.AffectedByGravity = value;
        }
    }

    /// <summary>
    /// Gets or sets the mass of this Rigidbody3D.
    /// </summary>
    public double Mass
    {
        get => mass;
        set
        {
            if (mass <= 0.0)
                throw new ArgumentException("Mass can not be zero or negative.", nameof(mass));

            mass = value;
            if (_body != null) _body.SetMassInertia(value);
        }
    }

    /// <summary>
    /// Gets or sets the friction of this Rigidbody3D.
    /// </summary>
    public double Friction
    {
        get => friction;
        set
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Restitution must be between 0 and 1.");

            friction = value;
            if (_body != null) _body.Friction = value;
        }
    }

    /// <summary>
    /// Gets or sets the restitution of this Rigidbody3D.
    /// </summary>
    public double Restitution
    {
        get => restitution;
        set
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Restitution must be between 0 and 1.");

            restitution = value;
            if (_body != null) _body.Restitution = value;
        }
    }

    [SerializeIgnore, CloneField(CloneFieldFlags.Skip)]
    internal RigidBody _body;

    public RigidBody CreateBody(World world)
    {
        _body = world.CreateRigidBody();
        UpdateProperties(_body);
        UpdateShapes(_body);
        UpdateTransform(_body);
        _body.Tag = new RigidBodyUserData()
        {
            Rigidbody = this,
            Layer = GameObject.layerIndex,
            //HasTransformConstraints = rotationConstraints != Vector3Int.one || translationConstraints != Vector3Int.one,
            //RotationConstraint = new JVector(rotationConstraints.x, rotationConstraints.y, rotationConstraints.z),
            //TranslationConstraint = new JVector(translationConstraints.x, translationConstraints.y, translationConstraints.z)
        };
        return _body;
    }

    public override void OnValidate()
    {
        if (_body == null || _body.Handle.IsZero)
            _body = Physics.World.CreateRigidBody();

        UpdateProperties(_body);
        UpdateShapes(_body);
        UpdateTransform(_body);
    }

    public override void Update()
    {
        if (_body == null || _body.Handle.IsZero) return;

        Transform.position = new Vector3(_body.Position.X, _body.Position.Y, _body.Position.Z);
        Transform.rotation = new Quaternion(_body.Orientation.X, _body.Orientation.Y, _body.Orientation.Z, _body.Orientation.W);
    }

    public override void DrawGizmos()
    {
        if (_body == null || _body.Handle.IsZero || !Application.IsEditor) return;

        _body.DebugDraw(JitterGizmosDrawer.Instance);
    }

    public override void OnEnable()
    {
        if (_body == null || _body.Handle.IsZero)
        {
            CreateBody(Physics.World);
        }
    }

    public override void OnDisable()
    {
        if (_body != null && !_body.Handle.IsZero) Physics.World?.Remove(_body);
    }

    internal void UpdateProperties(RigidBody rb)
    {
        rb.IsStatic = isStatic;
        rb.EnableSpeculativeContacts = isSpeculative;
        rb.Friction = friction;
        rb.AffectedByGravity = useGravity;
        rb.Restitution = restitution;
        rb.Tag = new RigidBodyUserData()
        {
            Layer = GameObject.layerIndex,
            //HasTransformConstraints = rotationConstraints != Vector3Int.one || translationConstraints != Vector3Int.one,
            //RotationConstraint = new JVector(rotationConstraints.x, rotationConstraints.y, rotationConstraints.z),
            //TranslationConstraint = new JVector(translationConstraints.x, translationConstraints.y, translationConstraints.z)
        };
        rb.SetMassInertia(mass);
    }

    internal void UpdateShapes(RigidBody rb)
    {
        rb.RemoveShape(rb.Shapes, false);
        foreach (var shape in GetComponents<Collider>())
        {
            var result = shape.CreateTransformedShape();
            if (result == null) continue;
            rb.AddShape(result, false);
        }
    }

    internal void UpdateTransform(RigidBody rb)
    {
        if (PhysicsSetting.Instance.AutoSyncTransforms)
        {
            rb.Position = new JVector(Transform.position.x, Transform.position.y, Transform.position.z);
            rb.Orientation = new JQuaternion(Transform.rotation.x, Transform.rotation.y, Transform.rotation.z, Transform.rotation.w);
        }
    }
}
