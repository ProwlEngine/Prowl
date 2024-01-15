using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;
using System.Collections.Generic;
using System.Linq;


// Partially based on https://github.com/suzuke/JitterPhysicsForUnity

namespace Prowl.Runtime
{
    public abstract class Collider : MonoBehaviour
    {
        private List<Shape> shape;
        public List<Shape> Shape => shape ??= CreateShapes();
        public void OnEnable() => GetComponentInParent<Rigidbody>()?.RefreshShape();
        public void OnDisable() => GetComponentInParent<Rigidbody>()?.RefreshShape();
        public abstract List<Shape> CreateShapes();
        public virtual List<TransformedShape> CreateTransformedShape(Rigidbody body)
        {
            // Transform is guranteed to exist, since a Collider must be On or under a Rigidbody which requires a Transform
            // COllider is on a child without a transform the Parent transform of the Rigidbody is used via Inheritance
            // This is fine since we use Global positions here, since the Body and Collider share a transform their global positions are identical
            // Results in no offsets being applied to the shape
            var position = GameObject.Position - body.GameObject.Position;
            var rotation = Quaternion.RotateTowards(body.GameObject.Rotation, GameObject.Rotation, 360);

            var invRotation = Quaternion.Inverse(body.GameObject.Rotation);
            rotation = invRotation * rotation;
            position = Vector3.Transform(position, invRotation);
            var jRot = JMatrix.CreateFromQuaternion(rotation);

            return Shape.Select(x => new TransformedShape(x, position, jRot)).ToList();
        }
    }

}