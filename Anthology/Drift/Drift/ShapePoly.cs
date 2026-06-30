using System.Numerics;

namespace Prowl.Drift
{
    public class ShapePoly : Shape
    {
        public struct Plane
        {
            public Vector2 Normal;
            public float Distance;

            public Plane(Vector2 normal, float distance)
            {
                Normal = normal;
                Distance = distance;
            }
        }

        public List<Vector2> Verts { get; } = new();
        public List<Plane> Planes { get; } = new();

        public List<Vector2> TransformedVerts { get; } = new();
        public List<Plane> TransformedPlanes { get; } = new();

        public bool Convex { get; private set; } = true;

        public ShapePoly(IEnumerable<Vector2> verts) : base(TypePoly)
        {
            foreach (var v in verts)
            {
                Verts.Add(v);
                TransformedVerts.Add(v);
                TransformedPlanes.Add(new Plane(Vector2.Zero, 0));
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
                var n = Vector2.Normalize(MathUtil.Perp(a - b));

                Planes.Add(new Plane(n, Vector2.Dot(n, a)));
                TransformedVerts[i] = Verts[i];
                TransformedPlanes.Add(new Plane(Vector2.Zero, 0));
            }

            for (int i = 0; i < Verts.Count; i++)
            {
                var b = Verts[(i + 2) % Verts.Count];
                var n = Planes[i].Normal;
                var d = Planes[i].Distance;
                if (Vector2.Dot(n, b) - d > 0) Convex = false;
            }
        }

        public override Shape Duplicate() => new ShapePoly(Verts);

        public override void Recenter(Vector2 c)
        {
            for (int i = 0; i < Verts.Count; i++)
                Verts[i] -= c;
        }

        public override float Area() => Geometry.AreaForPoly(Verts);

        public override Vector2 Centroid() => Geometry.CentroidForPoly(Verts);

        public override float Inertia(float mass) => Geometry.InertiaForPoly(mass, Verts, Vector2.Zero);

        public override void CacheData(Body body)
        {
            Bounds.Clear();
            int numVerts = Verts.Count;
            if (numVerts == 0) return;

            for (int i = 0; i < numVerts; i++)
                TransformedVerts[i] = body.TransformPoint(Verts[i]);

            if (numVerts < 2)
            {
                Bounds.AddPoint(TransformedVerts[0]);
                return;
            }

            for (int i = 0; i < numVerts; i++)
            {
                var a = TransformedVerts[i];
                var b = TransformedVerts[(i + 1) % numVerts];
                var n = Vector2.Normalize(MathUtil.Perp(a - b));
                TransformedPlanes[i] = new Plane(n, Vector2.Dot(n, a));
                Bounds.AddPoint(a);
            }
        }

        public override bool PointQuery(Vector2 p) =>
            Bounds.ContainsPoint(p) && ContainsPoint(p);

        public bool ContainsPoint(Vector2 p)
        {
            foreach (var plane in TransformedPlanes)
                if (Vector2.Dot(plane.Normal, p) - plane.Distance > 0)
                    return false;
            return true;
        }

        public bool ContainsPointPartial(Vector2 p, Vector2 n)
        {
            foreach (var plane in TransformedPlanes)
            {
                if (Vector2.Dot(plane.Normal, n) < 0.0001f) continue;
                if (Vector2.Dot(plane.Normal, p) - plane.Distance > 0) return false;
            }
            return true;
        }

        public override int FindVertexByPoint(Vector2 p, float minDist)
        {
            float dsq = minDist * minDist;
            for (int i = 0; i < TransformedVerts.Count; i++)
                if ((TransformedVerts[i] - p).LengthSquared() < dsq)
                    return i;
            return -1;
        }

        public int FindEdgeByPoint(Vector2 p, float minDist)
        {
            float dsq = minDist * minDist;
            int numVerts = TransformedVerts.Count;

            for (int i = 0; i < numVerts; i++)
            {
                var v1 = TransformedVerts[i];
                var v2 = TransformedVerts[(i + 1) % numVerts];
                var n = TransformedPlanes[i].Normal;

                float dtv1 = MathUtil.Cross(v1, n);
                float dtv2 = MathUtil.Cross(v2, n);
                float dt = MathUtil.Cross(p, n);

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
                    float dist = Vector2.Dot(n, p) - Vector2.Dot(n, v1);
                    if (dist * dist < dsq) return i;
                }
            }
            return -1;
        }

        public override float DistanceOnPlane(Vector2 n, float d)
        {
            float min = float.MaxValue;
            foreach (var v in TransformedVerts)
                min = MathF.Min(min, Vector2.Dot(n, v));
            return min - d;
        }

        public override RaycastHit Raycast(Ray ray)
        {
            if (!Bounds.IntersectsRay(ray.Origin, ray.Direction, ray.MaxDistance))
                return RaycastHit.Miss;

            float nearestDistance = float.MaxValue;
            Vector2 nearestNormal = Vector2.Zero;
            bool hit = false;

            for (int i = 0; i < TransformedVerts.Count; i++)
            {
                Vector2 v1 = TransformedVerts[i];
                Vector2 v2 = TransformedVerts[(i + 1) % TransformedVerts.Count];

                Vector2 edge = v2 - v1;
                if (edge.LengthSquared() < 0.0001f) continue;

                // Use the precomputed plane normal from TransformedPlanes
                Vector2 normal = TransformedPlanes[i].Normal;

                float denominator = Vector2.Dot(ray.Direction, normal);
                if (MathF.Abs(denominator) < 0.0001f) continue;

                // Only consider faces facing towards the ray (dot product < 0)
                if (denominator > 0) continue;

                float t = (TransformedPlanes[i].Distance - Vector2.Dot(ray.Origin, normal)) / denominator;
                if (t < 0 || t > ray.MaxDistance) continue;

                Vector2 intersection = ray.Origin + ray.Direction * t;

                // Check if intersection point is on the edge
                float edgeParam = Vector2.Dot(intersection - v1, edge) / Vector2.Dot(edge, edge);
                if (edgeParam < 0 || edgeParam > 1) continue;

                if (t < nearestDistance)
                {
                    nearestDistance = t;
                    nearestNormal = normal;
                    hit = true;
                }
            }

            if (!hit) return RaycastHit.Miss;

            Vector2 hitPoint = ray.GetPoint(nearestDistance);
            return new RaycastHit(true, nearestDistance, hitPoint, nearestNormal, Body, this);
        }

        public static ShapePoly CreateBox(float localX, float localY, float width, float height)
        {
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            var verts = new Vector2[]
            {
                new Vector2(-hw + localX, +hh + localY),
                new Vector2(-hw + localX, -hh + localY),
                new Vector2(+hw + localX, -hh + localY),
                new Vector2(+hw + localX, +hh + localY)
            };
            return new ShapePoly(verts);
        }
    }
}
