namespace Physics2D
{
    public class Body
    {
        public enum BodyType
        {
            Static = 0,
            Kinetic = 1,
            Dynamic = 2
        }

        private static int _idCounter = 0;

        public readonly int Id;
        public Space Space { get; internal set; }

        public BodyType Type { get; private set; }

        // Transform & state
        public Transform Transform;
        public Vec2 Centroid;           // local center of mass
        public Vec2 Position;           // world position of centroid
        public Vec2 LinearVelocity;     // linear velocity
        public Vec2 AccumulatedForce;   // accumulated force
        public float Angle;             // orientation (angle)
        public float AngularVelocity;   // angular velocity
        public float AccumulatedTorque; // accumulated torque

        // Damping
        public float LinearDamping;
        public float AngularDamping;

        // Shapes & joints
        public readonly List<Shape> Shapes = new();
        public readonly List<Joint> Joints = new();

        // Bounding box
        public Bounds Bounds = new();

        public bool FixedRotation { get; private set; }

        // Mass & inertia
        public float Mass { get; private set; }
        public float MassInv { get; private set; }
        public float Inertia { get; private set; }
        public float InertiaInv { get; private set; }

        public Body(BodyType type, Vec2 pos, float angle = 0)
        {
            Id = _idCounter++;

            Type = type;
            Transform = new Transform(pos, angle);

            Centroid = Vec2.Zero;
            Position = new Vec2(pos.X, pos.Y);
            LinearVelocity = Vec2.Zero;
            AccumulatedForce = Vec2.Zero;

            Angle = angle;
            AngularVelocity = 0;
            AccumulatedTorque = 0;

            LinearDamping = 0;
            AngularDamping = 0;
        }

        public Body Duplicate()
        {
            var body = new Body(Type, Transform.T, Angle);
            foreach (var shape in Shapes)
                body.AddShape(shape.Duplicate());
            body.ResetMassData();
            return body;
        }

        public bool IsStatic() => Type == BodyType.Static;
        public bool IsDynamic() => Type == BodyType.Dynamic;
        public bool IsKinetic() => Type == BodyType.Kinetic;

        public void SetType(BodyType type)
        {
            if (type == Type) return;

            AccumulatedForce = Vec2.Zero;
            LinearVelocity = Vec2.Zero;
            AccumulatedTorque = 0;
            AngularVelocity = 0;
            Type = type;
        }

        public void AddShape(Shape shape)
        {
            shape.Body = this;
            Shapes.Add(shape);
        }

        public void RemoveShape(Shape shape)
        {
            if (Shapes.Remove(shape))
                shape.Body = null;
        }

        private void SetMass(float mass)
        {
            Mass = mass;
            MassInv = mass > 0 ? 1 / mass : 0;
        }

        private void SetInertia(float inertia)
        {
            Inertia = inertia;
            InertiaInv = inertia > 0 ? 1 / inertia : 0;
        }

        public void SetTransform(Vec2 pos, float angle)
        {
            Transform.Set(pos, angle);
            Position = Transform.TransformPoint(Centroid);
            Angle = angle;
        }

        public void SyncTransform()
        {
            Transform.SetRotation(Angle);
            Transform.SetPosition(Position - Transform.Rotate(Centroid));
        }

        public Vec2 GetWorldPoint(Vec2 p) => Transform.TransformPoint(p);
        public Vec2 GetWorldVector(Vec2 v) => Transform.Rotate(v);
        public Vec2 GetLocalPoint(Vec2 p) => Transform.UntransformPoint(p);
        public Vec2 GetLocalVector(Vec2 v) => Transform.Unrotate(v);

        public void SetFixedRotation(bool flag)
        {
            FixedRotation = flag;
            ResetMassData();
        }

        public void ResetMassData()
        {
            Centroid = Vec2.Zero;
            Mass = 0;
            MassInv = 0;
            Inertia = 0;
            InertiaInv = 0;

            if (!IsDynamic())
            {
                Position = Transform.TransformPoint(Centroid);
                return;
            }

            Vec2 totalMassCentroid = Vec2.Zero;
            float totalMass = 0;
            float totalInertia = 0;

            foreach (var shape in Shapes)
            {
                var centroid = shape.Centroid();
                float mass = shape.Area() * shape.Density;
                float inertia = shape.Inertia(mass);

                totalMassCentroid.Mad(centroid, mass);
                totalMass += mass;
                totalInertia += inertia;
            }

            Centroid = totalMassCentroid / totalMass;
            SetMass(totalMass);

            if (!FixedRotation)
                SetInertia(totalInertia - totalMass * Vec2.Dot(Centroid, Centroid));

            var oldP = Position;
            Position = Transform.TransformPoint(Centroid);
            LinearVelocity.Mad(Vec2.Perp(Position - oldP), AngularVelocity);
        }

        public void ResetJointAnchors()
        {
            foreach (var joint in Joints)
            {
                if (joint == null) continue;
                var anchor1 = joint.GetWorldAnchor1();
                var anchor2 = joint.GetWorldAnchor2();
                joint.SetWorldAnchor1(anchor1);
                joint.SetWorldAnchor2(anchor2);
            }
        }

        public void CacheData()
        {
            Bounds.Clear();
            foreach (var shape in Shapes)
            {
                shape.CacheData(Transform);
                Bounds.AddBounds(shape.Bounds);
            }
        }

        public void UpdateVelocity(Vec2 gravity, float dt, float damping)
        {
            LinearVelocity += (gravity + AccumulatedForce * MassInv) * dt;
            AngularVelocity += AccumulatedTorque * InertiaInv * dt;

            float linFactor = MathUtil.Clamp(1 - dt * (damping + LinearDamping), 0, 1);
            float angFactor = MathUtil.Clamp(1 - dt * (damping + AngularDamping), 0, 1);

            LinearVelocity *= linFactor;
            AngularVelocity *= angFactor;

            AccumulatedForce = Vec2.Zero;
            AccumulatedTorque = 0;
        }

        public void UpdatePosition(float dt)
        {
            Position += LinearVelocity * dt;
            Angle += AngularVelocity * dt;
        }

        public void ResetForce()
        {
            AccumulatedForce = Vec2.Zero;
            AccumulatedTorque = 0;
        }

        public void ApplyForce(Vec2 force, Vec2 p)
        {
            if (!IsDynamic()) return;

            AccumulatedForce += force;
            AccumulatedTorque += Vec2.Cross(p - Position, force);
        }

        public void ApplyForceToCenter(Vec2 force)
        {
            if (!IsDynamic()) return;

            AccumulatedForce += force;
        }

        public void ApplyTorque(float torque)
        {
            if (!IsDynamic()) return;

            AccumulatedTorque += torque;
        }

        public void ApplyLinearImpulse(Vec2 impulse, Vec2 p)
        {
            if (!IsDynamic()) return;

            LinearVelocity += impulse * MassInv;
            AngularVelocity += Vec2.Cross(p - Position, impulse) * InertiaInv;
        }

        public void ApplyAngularImpulse(float impulse)
        {
            if (!IsDynamic()) return;

            AngularVelocity += impulse * InertiaInv;
        }

        public float KineticEnergy()
        {
            float vsq = Vec2.Dot(LinearVelocity, LinearVelocity);
            float wsq = AngularVelocity * AngularVelocity;
            return 0.5f * (Mass * vsq + Inertia * wsq);
        }

        public bool IsCollidable(Body other)
        {
            if (this == other) return false;
            if (!IsDynamic() && !other.IsDynamic()) return false;

            foreach (var joint in Joints)
            {
                if (joint == null) continue;
                if (!joint.CollideConnected && other.Joints.Contains(joint))
                    return false;
            }
            return true;
        }
    }
}
