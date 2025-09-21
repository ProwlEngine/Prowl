namespace Prowl.Drift
{
    public class ShapePoly : Shape
    {
        public struct Plane(Vec2 normal, float distance)
        {
            public Vec2 Normal = normal;
            public float Distance = distance;
        }

        public List<Vec2> Verts { get; } = new();
        public List<Plane> Planes { get; } = new();

        public List<Vec2> TransformedVerts { get; } = new();
        public List<Plane> TransformedPlanes { get; } = new();

        public bool Convex { get; private set; } = true;

        public ShapePoly(IEnumerable<Vec2> verts) : base(TypePoly)
        {
            foreach (var v in verts)
            {
                Verts.Add(v);
                TransformedVerts.Add(v);
                TransformedPlanes.Add(new Plane(Vec2.Zero, 0));
            }
            FinishVerts();
        }

        private void FinishVerts()
        {
            if (Verts.Count < 2)
            {
                Convex = false;
                Planes.Clear();
                return;
            }

            Planes.Clear();
            TransformedPlanes.Clear();

            for (int i = 0; i < Verts.Count; i++)
            {
                var a = Verts[i];
                var b = Verts[(i + 1) % Verts.Count];
                var n = Vec2.Normalize(Vec2.Perp(a - b));

                Planes.Add(new Plane(n, Vec2.Dot(n, a)));
                TransformedVerts[i] = Verts[i];
                TransformedPlanes.Add(new Plane(Vec2.Zero, 0));
            }

            for (int i = 0; i < Verts.Count; i++)
            {
                var b = Verts[(i + 2) % Verts.Count];
                var n = Planes[i].Normal;
                var d = Planes[i].Distance;
                if (Vec2.Dot(n, b) - d > 0) Convex = false;
            }
        }

        public override Shape Duplicate() => new ShapePoly(Verts);

        public override void Recenter(Vec2 c)
        {
            for (int i = 0; i < Verts.Count; i++)
                Verts[i] -= c;
        }

        public override void Transform(Transform xf)
        {
            for (int i = 0; i < Verts.Count; i++)
                Verts[i] = xf.TransformPoint(Verts[i]);
        }

        public override void Untransform(Transform xf)
        {
            for (int i = 0; i < Verts.Count; i++)
                Verts[i] = xf.UntransformPoint(Verts[i]);
        }

        public override float Area() => Geometry.AreaForPoly(Verts);

        public override Vec2 Centroid() => Geometry.CentroidForPoly(Verts);

        public override float Inertia(float mass) => Geometry.InertiaForPoly(mass, Verts, Vec2.Zero);

        public override void CacheData(Transform xf)
        {
            Bounds.Clear();
            int numVerts = Verts.Count;
            if (numVerts == 0) return;

            for (int i = 0; i < numVerts; i++)
                TransformedVerts[i] = xf.TransformPoint(Verts[i]);

            if (numVerts < 2)
            {
                Bounds.AddPoint(TransformedVerts[0]);
                return;
            }

            for (int i = 0; i < numVerts; i++)
            {
                var a = TransformedVerts[i];
                var b = TransformedVerts[(i + 1) % numVerts];
                var n = Vec2.Normalize(Vec2.Perp(a - b));
                TransformedPlanes[i] = new Plane(n, Vec2.Dot(n, a));
                Bounds.AddPoint(a);
            }
        }

        public override bool PointQuery(Vec2 p) =>
            Bounds.ContainsPoint(p) && ContainsPoint(p);

        public bool ContainsPoint(Vec2 p)
        {
            foreach (var plane in TransformedPlanes)
                if (Vec2.Dot(plane.Normal, p) - plane.Distance > 0)
                    return false;
            return true;
        }

        public bool ContainsPointPartial(Vec2 p, Vec2 n)
        {
            foreach (var plane in TransformedPlanes)
            {
                if (Vec2.Dot(plane.Normal, n) < 0.0001f) continue;
                if (Vec2.Dot(plane.Normal, p) - plane.Distance > 0) return false;
            }
            return true;
        }

        public override int FindVertexByPoint(Vec2 p, float minDist)
        {
            float dsq = minDist * minDist;
            for (int i = 0; i < TransformedVerts.Count; i++)
                if ((TransformedVerts[i] - p).LengthSquared() < dsq)
                    return i;
            return -1;
        }

        public int FindEdgeByPoint(Vec2 p, float minDist)
        {
            float dsq = minDist * minDist;
            int numVerts = TransformedVerts.Count;

            for (int i = 0; i < numVerts; i++)
            {
                var v1 = TransformedVerts[i];
                var v2 = TransformedVerts[(i + 1) % numVerts];
                var n = TransformedPlanes[i].Normal;

                float dtv1 = Vec2.Cross(v1, n);
                float dtv2 = Vec2.Cross(v2, n);
                float dt = Vec2.Cross(p, n);

                if (dt > dtv1)
                {
                    if ((v1 - p).LengthSquared() < dsq) return i;
                }
                else if (dt < dtv2)
                {
                    if ((v2 - p).LengthSquared() < dsq) return i;
                }
                else
                {
                    float dist = Vec2.Dot(n, p) - Vec2.Dot(n, v1);
                    if (dist * dist < dsq) return i;
                }
            }
            return -1;
        }

        public override float DistanceOnPlane(Vec2 n, float d)
        {
            float min = float.MaxValue;
            foreach (var v in TransformedVerts)
                min = MathF.Min(min, Vec2.Dot(n, v));
            return min - d;
        }

        public static ShapePoly CreateBox(float localX, float localY, float width, float height)
        {
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            var verts = new Vec2[]
            {
                new Vec2(-hw + localX, +hh + localY),
                new Vec2(-hw + localX, -hh + localY),
                new Vec2(+hw + localX, -hh + localY),
                new Vec2(+hw + localX, +hh + localY)
            };
            return new ShapePoly(verts);
        }
    }
}
