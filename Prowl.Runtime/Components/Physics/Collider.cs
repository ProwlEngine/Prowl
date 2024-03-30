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
        public override void OnEnable() => GetComponentInParent<Rigidbody>()?.RefreshShape();
        public override void OnDisable() => GetComponentInParent<Rigidbody>()?.RefreshShape();
        public abstract List<Shape> CreateShapes();
        public virtual List<TransformedShape> CreateTransformedShape(Rigidbody body)
        {
            // Get Position and Rotation of this collider relative to the Rigidbody
            if(GameObject.InstanceID == body.GameObject.InstanceID)
            {
                return Shape.Select(x => new TransformedShape(x, Vector3.zero, JMatrix.CreateScale(GameObject.transform.localScale))).ToList();
            }
            else
            {
                var position = GameObject.transform.position - body.GameObject.transform.position;
                var rotation = GameObject.transform.rotation * body.GameObject.transform.rotation;

                var jMat = JMatrix.CreateFromQuaternion(rotation) * JMatrix.CreateScale(GameObject.transform.localScale);

                return Shape.Select(x => new TransformedShape(x, position, jMat)).ToList();
            }
        }
    }

}