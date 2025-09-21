using System.Numerics;

namespace Prowl.Drift
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

        public Vector2 Gravity { get; set; } = new Vector2(0, 0);
        public float Damping { get; set; }

        private const float BroadPhaseCellSize = 1f; // You can tweak this value
        private readonly SpatialHash<Body> _spatialHash = new(BroadPhaseCellSize);

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
            {
                if (body.IsDynamic())
                    body.CacheData();
            }
        }

        private void GetOrCreateContactSolvers()
        {
            // BroadPhase: Build spatial hash
            _spatialHash.Clear();
            foreach (var body in _bodies)
            {
                _spatialHash.Insert(body, body.Bounds);
            }

            var validHashes = new HashSet<ulong>();
            _contactSolvers.Clear();

            // Use spatial hash broadphase
            var checkedPairs = new HashSet<(int, int)>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var body1 = _bodies[i];

                foreach (var cell in _spatialHash.GetPotentialPairs(body1.Bounds))
                {
                    foreach (var body2 in cell)
                    {
                        if (body1 == body2) continue;
                        int idA = body1.Id, idB = body2.Id;
                        if (idA > idB) (idA, idB) = (idB, idA);
                        var pair = (idA, idB);
                        if (checkedPairs.Contains(pair)) continue;
                        checkedPairs.Add(pair);

                        // Narrow Phase
                        if (!body1.Bounds.Intersects(body2.Bounds)) continue;

                        if (body1.IsStatic() && body2.IsStatic()) continue;
                        if (!body1.CanCollideWith(body2)) continue;

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

                                int sidA = shapeA.Id, sidB = shapeB.Id;
                                if (sidA > sidB) (sidA, sidB) = (sidB, sidA);
                                ulong hash = ((ulong)sidA << 32) | (uint)sidB;

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

        public Body? FindBodyByPoint(Vector2 point)
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
