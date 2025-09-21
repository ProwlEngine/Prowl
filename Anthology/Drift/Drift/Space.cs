namespace Physics2D
{
    public class Space
    {
        private readonly List<Body> _bodies = new();

        private readonly List<Joint> _joints = new();

        private readonly List<ContactSolver> _contactSolvers = new();
        private readonly Dictionary<ulong, ContactSolver> _contactSolverMap = new ();

        public IReadOnlyList<Body> Bodies => _bodies.AsReadOnly();
        public IReadOnlyList<ContactSolver> Contacts => _contactSolvers.AsReadOnly();   
        public IReadOnlyList<Joint> Joints => _joints.AsReadOnly();

        public Vec2 Gravity { get; set; } = new Vec2(0, 0);
        public float Damping { get; set; }

        public void Clear()
        {
            List<Body> bodiesCopy = new(_bodies);
            foreach (var body in bodiesCopy)
            {
                if (body != null)
                    RemoveBody(body);
            }

            _bodies.Clear();
            _joints.Clear();
            _contactSolvers.Clear();
            _contactSolverMap.Clear();
        }

        public void AddBody(Body body)
        {
            if (body.Space != null) throw new InvalidOperationException("Body was already added to a space.");

            _bodies.Add(body);

            body.Space = this;
            body.CacheData();
        }

        public void RemoveBody(Body body)
        {
            if (body.Space != this) throw new InvalidOperationException("Body was not added to this space.");

            List<Joint> jointsCopy = new List<Joint>(body.Joints);
            foreach (var joint in jointsCopy)
            {
                if (joint != null)
                    RemoveJoint(joint);
            }

            body.Space = null;

            _bodies.Remove(body);
        }

        public void AddJoint(Joint joint)
        {
            if (joint.Space != null) throw new InvalidOperationException("joint was already added to a space.");

            joint.Space = this;

            _joints.Add(joint);

            joint.Body1.Joints.Add(joint);
            joint.Body2.Joints.Add(joint);
        }

        public void RemoveJoint(Joint joint)
        {
            if (joint.Space != this) throw new InvalidOperationException("Joint was not added to this space.");

            joint.Body1.Joints.Remove(joint);
            joint.Body2.Joints.Remove(joint);

            _joints.Remove(joint);

            joint.Space = null;
        }

        public void Step(float dt, int velIterations, int posIterations, bool warmStarting)
        {
            float dtInv = 1f / dt;

            GetOrCreateContactSolvers();

            InitSolver(dt, dtInv, warmStarting);

            foreach (var body in _bodies)
            {
                if (body.IsDynamic())
                    body.UpdateVelocity(Gravity, dt, Damping);
            }

            VelocitySolver(velIterations);

            foreach (var body in _bodies)
            {
                if (body.IsDynamic())
                    body.UpdatePosition(dt);
            }

            for (int i = 0; i < _joints.Count; i++)
            {
                var joint = _joints[i];
                if (!joint.Breakable) continue;

                if (joint.GetReactionForce(dtInv).LengthSquared() >= joint.MaxForce * joint.MaxForce)
                    RemoveJoint(joint);
            }

            bool positionSolved = PositionSolver(posIterations);

            foreach (var body in _bodies)
                body?.SyncTransform();

            foreach (var body in _bodies)
            {
                if (body.IsDynamic())
                    body.CacheData();
            }
        }

        private void GetOrCreateContactSolvers()
        {
            var validHashes = new HashSet<ulong>();

            _contactSolvers.Clear();

            for (int i = 0; i < _bodies.Count; i++)
            {
                var body1 = _bodies[i];

                for (int j = i + 1; j < _bodies.Count; j++)
                {
                    var body2 = _bodies[j];

                    // We have to do the bounds check regardless, and since most bodies are dynamic and can interact, doing this first is best
                    if (!body1.Bounds.Intersects(body2.Bounds)) continue;

                    if (body1.IsStatic() && body2.IsStatic()) continue;
                    if (!body1.IsCollidable(body2)) continue;

                    foreach (var shape1 in body1.Shapes)
                    {
                        foreach (var shape2 in body2.Shapes)
                        {
                            var shapeA = shape1;
                            var shapeB = shape2;
                            if (shapeA.Type > shapeB.Type)
                                (shapeB, shapeA) = (shapeA, shapeB);

                            List<Contact> contacts = new List<Contact>(); 
                            var contactCount = Collision.Collide(shapeA, shapeB, contacts);
                            if (contactCount == 0) continue;

                            int idA = shapeA.Id, idB = shapeB.Id;
                            if (idA > idB) (idA, idB) = (idB, idA); // Not sure if this is really needed?
                            ulong hash = ((ulong)idA << 32) | (uint)idB;

                            validHashes.Add(hash);

                            if (_contactSolverMap.TryGetValue(hash, out var existing))
                            {
                                existing.Update(contacts);
                                _contactSolvers.Add(existing);
                            }
                            else
                            {
                                float e = MathF.Max(shapeA.Elasticity, shapeB.Elasticity);
                                float f = MathF.Sqrt(shapeA.Friction * shapeB.Friction);
                                var solver = new ContactSolver(shapeA, shapeB, contacts, e, f);
                                _contactSolvers.Add(solver);
                                _contactSolverMap[hash] = solver;
                            }
                        }
                    }
                }
            }

            var allHashes = new List<ulong>(_contactSolverMap.Keys);
            foreach (var hash in allHashes)
            {
                if (!validHashes.Contains(hash))
                    _contactSolverMap.Remove(hash);
            }
        }

        private void InitSolver(float dt, float dtInv, bool warmStarting)
        {
            foreach (var solver in _contactSolvers)
                solver.InitSolver(dtInv);

            foreach (var joint in _joints)
                joint?.InitSolver(dt, warmStarting);

            if (warmStarting)
                foreach (var solver in _contactSolvers)
                    solver.WarmStart();
        }

        private void VelocitySolver(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                foreach (var joint in _joints)
                    joint?.SolveVelocityConstraints();

                foreach (var solver in _contactSolvers)
                    solver.SolveVelocityConstraints();
            }
        }

        private bool PositionSolver(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                bool contactsOk = true, jointsOk = true;

                foreach (var solver in _contactSolvers)
                    contactsOk &= solver.SolvePositionConstraints();

                foreach (var joint in _joints)
                    if (joint != null)
                        jointsOk &= joint.SolvePositionConstraints();

                if (contactsOk && jointsOk) return true;
            }
            return false;
        }

        public Body FindBodyByPoint(Vec2 point)
        {
            foreach (var body in _bodies)
            {
                if (!body.Bounds.ContainsPoint(point)) continue;

                foreach (var shape in body.Shapes)
                {
                    if (shape.PointQuery(point))
                        return body;
                }
            }
            return null;
        }
    }
}
