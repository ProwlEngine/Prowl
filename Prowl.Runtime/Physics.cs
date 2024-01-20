using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using Prowl.Runtime.SceneManagement;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public class PhysicalSpace
    {
        public readonly World world;

        private static double timer = 0;

        public PhysicalSpace(int iterations = 2, int substeps = 8)
        {
            world = new();
            world.SolverIterations = iterations;
            world.NumberSubsteps = substeps;
            world.ThreadModel = World.ThreadModelType.Regular;

            Physics.PhysicalSpaces.Add(this);
        }

        internal void Update(double delta)
        {
            timer += delta;
            int count = 0;
            while (timer >= Time.fixedDeltaTime && count < 10) {
                count++;
                world.Step((float)Time.fixedDeltaTime);
                SceneManager.PhysicsUpdate();
                timer -= Time.fixedDeltaTime;
            }
        }

        public void Dispose(bool clear = true)
        {
            // TODO: Causes a crash, seems to be a bug in Jitter2 where Shapes cannot be removed because they still have rigidbodies attatched to them
            //world.Clear();
            if (clear)
                Physics.PhysicalSpaces.Remove(this);
        }
    }

    public static class Physics
    {
        public static PhysicalSpace DefaultSpace => PhysicalSpaces[0];

        public readonly static List<PhysicalSpace> PhysicalSpaces = new();

        public static void Initialize()
        {
            PhysicalSpaces.Add(new PhysicalSpace()); // Default Space
        }

        public static void Update()
        {
            for(int i = 0; i < PhysicalSpaces.Count; i++) {
                PhysicalSpaces[i].Update(Time.deltaTime);
            }
        }

        public static RaycastHit? Raycast(Ray ray, double maxDistance = 10.0, World.RaycastFilterPre? preFilter = null, World.RaycastFilterPost? postFilter = null)
        {
            ray.direction *= maxDistance;
            if (DefaultSpace.world.Raycast(ray.origin, ray.direction, preFilter, postFilter, out Shape hitShape, out JVector hitNormal, out float hitFraction)) {
                if (hitFraction <= maxDistance)
                    return new RaycastHit(hitShape.RigidBody.Tag as Rigidbody, hitNormal, ray.origin, ray.direction, hitFraction);
            }
            return null;
        }


        public static void Dispose()
        {
            for (int i = 0; i < PhysicalSpaces.Count; i++) {
                PhysicalSpaces[i].Dispose(false);
            }
            PhysicalSpaces.Clear();
        }
    }

    public class Collision
    {
        public Rigidbody body;
        public GameObject gameObject;

        public Arbiter arbiter;

        // index returns the contact point arbiter.Handle.Data.Contact0, Contact1, Contact2
        public ContactData.Contact this[int index] {
            get {
                return index switch {
                    0 => arbiter.Handle.Data.Contact0,
                    1 => arbiter.Handle.Data.Contact1,
                    2 => arbiter.Handle.Data.Contact2,
                    3 => arbiter.Handle.Data.Contact3,
                    _ => throw new System.NotImplementedException(),
                };
            }
        }

        public Collision(Rigidbody from, Arbiter arbiter)
        {
            var other = object.ReferenceEquals(arbiter.Body1.Tag, from) ? arbiter.Body2 : arbiter.Body1;
            this.body = other.Tag as Rigidbody;
            this.gameObject = this.body.GameObject;

            this.arbiter = arbiter;
        }
    }

    public class RaycastHit
    {
        public Rigidbody Rigidbody { get; private set; }
        public Vector3 Point { get; private set; }
        public Vector3 Normal { get; private set; }
        public double Distance { get; private set; }

        public RaycastHit(Rigidbody rigidbody, Vector3 normal, Vector3 origin, Vector3 direction, double fraction)
        {
            Rigidbody = rigidbody;
            Normal = normal;
            Point = origin + direction * fraction;
            Distance = fraction * direction.magnitude;
        }
    }
}
