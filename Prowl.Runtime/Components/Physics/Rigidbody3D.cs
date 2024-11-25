// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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

    public bool isStatic;
    public bool isSpeculative;
    public bool useGravity = true;
    public float mass = 1;
    public float friction = 0.2f;
    public float restitution = 0;
    //public Vector3Int translationConstraints = Vector3Int.one;
    //public Vector3Int rotationConstraints = Vector3Int.one;

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

        this.Transform.position = new Vector3(_body.Position.X, _body.Position.Y, _body.Position.Z);
        this.Transform.rotation = new Quaternion(_body.Orientation.X, _body.Orientation.Y, _body.Orientation.Z, _body.Orientation.W);
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
            HasTransformConstraints = rotationConstraints != Vector3Int.one || translationConstraints != Vector3Int.one,
            RotationConstraint = new JVector(rotationConstraints.x, rotationConstraints.y, rotationConstraints.z),
            TranslationConstraint = new JVector(translationConstraints.x, translationConstraints.y, translationConstraints.z)
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
