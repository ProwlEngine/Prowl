namespace Prowl.Drift
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

            Centroid = Vec2.Zero;
            // pos is world origin; centroid starts at 0 so this equals pos
            Position = pos;// + RotatePoint(Centroid);
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
            var body = new Body(Type, Position - RotatePoint(Centroid), Angle);
            foreach (var shape in Shapes)
                body.AddShape(shape.Duplicate(), false);
            body.RecalculateMass();
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

        public void AddShape(Shape shape, bool recalculateMass = true)
        {
            shape.Body = this;
            Shapes.Add(shape);

            if (recalculateMass)
                RecalculateMass();
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

        /// <summary>
        /// Transforms a point by applying both rotation and translation.
        /// Equivalent to rotating the point and then translating by T.
        /// </summary>
        /// <param name="v">The point to transform.</param>
        /// <returns>The transformed point.</returns>
        public Vec2 TransformPoint(Vec2 localPoint)
        {
            float cos = MathF.Cos(Angle);
            float sin = MathF.Sin(Angle);

            float lx = localPoint.X - Centroid.X;
            float ly = localPoint.Y - Centroid.Y;

            return new Vec2(
                Position.X + (lx * cos - ly * sin),
                Position.Y + (lx * sin + ly * cos)
            );
        }
        
        /// <summary>
        /// Rotates a vector by this transform's rotation (without translation).
        /// </summary>
        /// <param name="v">The vector to rotate.</param>
        /// <returns>The rotated vector.</returns>
        public Vec2 RotatePoint(Vec2 localPoint)
        {
            float cos = MathF.Cos(Angle);
            float sin = MathF.Sin(Angle);
            return new Vec2(localPoint.X * cos - localPoint.Y * sin, localPoint.X * sin + localPoint.Y * cos);
        }

        /// <summary>
        /// Applies the inverse transformation to a point.
        /// Equivalent to translating by -T and then applying inverse rotation.
        /// </summary>
        /// <param name="v">The point to untransform.</param>
        /// <returns>The untransformed point.</returns>

        public Vec2 InverseTransformPoint(Vec2 worldPoint)
        {
            float cos = MathF.Cos(Angle);
            float sin = MathF.Sin(Angle);

            float dx = worldPoint.X - Position.X;
            float dy = worldPoint.Y - Position.Y;

            return new Vec2(
                dx * cos + dy * sin + Centroid.X,
               -dx * sin + dy * cos + Centroid.Y
            );
        }

        /// <summary>
        /// Applies the inverse rotation of this transform to a vector (without translation).
        /// </summary>
        /// <param name="v">The vector to unrotate.</param>
        /// <returns>The unrotated vector.</returns>
        public Vec2 InverseRotatePoint(Vec2 worldVector)
        {
            float cos = MathF.Cos(Angle);
            float sin = MathF.Sin(Angle);
            return new Vec2(worldVector.X * cos + worldVector.Y * sin, -worldVector.X * sin + worldVector.Y * cos);
        }

        public void SetFixedRotation(bool flag, bool recalculateMass = true)
        {
            FixedRotation = flag;
            if(true)
                RecalculateMass();
        }

        public void RecalculateMass()
        {
            // Save current origin so we can keep it fixed through the centroid change.
            Vec2 origin = Position - RotatePoint(Centroid);

            Centroid = Vec2.Zero;
            Mass = 0;
            MassInv = 0;
            Inertia = 0;
            InertiaInv = 0;

            if (!IsDynamic())
            {
                // For static/kinetic bodies we don't recompute mass/inertia,
                // but keep internal consistency: Position stays as world centroid.
                Position = origin + RotatePoint(Centroid);
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

            Centroid = totalMass > 0 ? totalMassCentroid / totalMass : Vec2.Zero;
            SetMass(totalMass);

            if (!FixedRotation)
                SetInertia(totalInertia - totalMass * Vec2.Dot(Centroid, Centroid));

            var oldP = Position;

            // Keep world origin fixed, update Position to new world centroid
            Position = origin + RotatePoint(Centroid);

            // v_com' = v_com + ω × (Δr), with Δr = (Position - oldP) in world
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
                shape.CacheData(this);
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
