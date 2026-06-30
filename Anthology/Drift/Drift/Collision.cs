using System.Numerics;

namespace Prowl.Drift
{
    public static class Collision
    {
        private static int CircleToCircleInternal(Vector2 c1, float r1, Vector2 c2, float r2, List<Contact> contacts)
        {
            float rmax = r1 + r2;
            Vector2 t = c2 - c1;
            float distSq = t.LengthSquared();

            if (distSq > rmax * rmax) return 0;

            float dist = MathF.Sqrt(distSq);
            Vector2 p = c1 + t * (0.5f + (r1 - r2) * 0.5f / (dist == 0 ? 0.01f : dist));
            Vector2 n = dist != 0 ? t / dist : Vector2.Zero;
            float d = dist - rmax;

            contacts.Add(new Contact(p, n, d, 0));
            return 1;
        }

        private static int CircleToCircle(Shape a, Shape b, List<Contact> contacts)
        {
            var c1 = (ShapeCircle)a;
            var c2 = (ShapeCircle)b;
            return CircleToCircleInternal(c1.TransformedCenter, c1.Radius, c2.TransformedCenter, c2.Radius, contacts);
        }

        private static int CircleToSegment(Shape a, Shape b, List<Contact> contacts)
        {
            var circ = (ShapeCircle)a;
            var seg = (ShapeSegment)b;
            float rsum = circ.Radius + seg.Radius;

            float dn = Vector2.Dot(circ.TransformedCenter, seg.TransformedNormal) - Vector2.Dot(seg.TransformedA, seg.TransformedNormal);
            float dist = MathF.Abs(dn) - rsum;
            if (dist > 0) return 0;

            float dt = MathUtil.Cross(circ.TransformedCenter, seg.TransformedNormal);
            float dtMin = MathUtil.Cross(seg.TransformedA, seg.TransformedNormal);
            float dtMax = MathUtil.Cross(seg.TransformedB, seg.TransformedNormal);

            if (dt < dtMin)
            {
                if (dt < dtMin - rsum) return 0;
                return CircleToCircleInternal(circ.TransformedCenter, circ.Radius, seg.TransformedA, seg.Radius, contacts);
            }
            else if (dt > dtMax)
            {
                if (dt > dtMax + rsum) return 0;
                return CircleToCircleInternal(circ.TransformedCenter, circ.Radius, seg.TransformedB, seg.Radius, contacts);
            }

            Vector2 n = dn > 0 ? seg.TransformedNormal : -seg.TransformedNormal;
            contacts.Add(new Contact(MathUtil.Mad(circ.TransformedCenter, n, -(circ.Radius + dist * 0.5f)), -n, dist, 0));
            return 1;
        }

        private static int CircleToPoly(Shape a, Shape b, List<Contact> contacts)
        {
            var circ = (ShapeCircle)a;
            var poly = (ShapePoly)b;

            float minDist = float.NegativeInfinity;
            int minIdx = -1;

            for (int i = 0; i < poly.Verts.Count; i++)
            {
                var plane = poly.TransformedPlanes[i];
                float dist = Vector2.Dot(circ.TransformedCenter, plane.Normal) - plane.Distance - circ.Radius;
                if (dist > 0) return 0;
                if (dist > minDist)
                {
                    minDist = dist;
                    minIdx = i;
                }
            }

            Vector2 n = poly.TransformedPlanes[minIdx].Normal;
            Vector2 aVert = poly.TransformedVerts[minIdx];
            Vector2 bVert = poly.TransformedVerts[(minIdx + 1) % poly.Verts.Count];
            float dta = MathUtil.Cross(aVert, n);
            float dtb = MathUtil.Cross(bVert, n);
            float dtc = MathUtil.Cross(circ.TransformedCenter, n);

            if (dtc > dta)
                return CircleToCircleInternal(circ.TransformedCenter, circ.Radius, aVert, 0, contacts);
            else if (dtc < dtb)
                return CircleToCircleInternal(circ.TransformedCenter, circ.Radius, bVert, 0, contacts);

            contacts.Add(new Contact(MathUtil.Mad(circ.TransformedCenter, n, -(circ.Radius + minDist * 0.5f)), -n, minDist, 0));
            return 1;
        }

        private static float SegmentPointDistanceSq(ShapeSegment seg, Vector2 p)
        {
            Vector2 w = p - seg.TransformedA;
            Vector2 d = seg.TransformedB - seg.TransformedA;
            float proj = Vector2.Dot(w, d);

            if (proj <= 0) return Vector2.Dot(w, w);

            float vsq = Vector2.Dot(d, d);
            if (proj >= vsq) return Vector2.Dot(w, w) - 2 * proj + vsq;

            return Vector2.Dot(w, w) - proj * proj / vsq;
        }

        private static int SegmentToSegment(Shape a, Shape b, List<Contact> contacts)
        {
            var seg1 = (ShapeSegment)a;
            var seg2 = (ShapeSegment)b;

            float[] d =
            {
                SegmentPointDistanceSq(seg1, seg2.TransformedA),
                SegmentPointDistanceSq(seg1, seg2.TransformedB),
                SegmentPointDistanceSq(seg2, seg1.TransformedA),
                SegmentPointDistanceSq(seg2, seg1.TransformedB)
            };

            int idx1 = d[0] < d[1] ? 0 : 1;
            int idx2 = d[2] < d[3] ? 2 : 3;
            int idxm = d[idx1] < d[idx2] ? idx1 : idx2;

            float s, t;
            Vector2 u = seg1.TransformedB - seg1.TransformedA;
            Vector2 v = seg2.TransformedB - seg2.TransformedA;

            switch (idxm)
            {
                case 0:
                    s = Vector2.Dot(seg2.TransformedA - seg1.TransformedA, u) / Vector2.Dot(u, u);
                    s = Math.Clamp(s, 0, 1);
                    t = 0;
                    break;
                case 1:
                    s = Vector2.Dot(seg2.TransformedB - seg1.TransformedA, u) / Vector2.Dot(u, u);
                    s = Math.Clamp(s, 0, 1);
                    t = 1;
                    break;
                case 2:
                    s = 0;
                    t = Vector2.Dot(seg1.TransformedA - seg2.TransformedA, v) / Vector2.Dot(v, v);
                    t = Math.Clamp(t, 0, 1);
                    break;
                case 3:
                    s = 1;
                    t = Vector2.Dot(seg1.TransformedB - seg2.TransformedA, v) / Vector2.Dot(v, v);
                    t = Math.Clamp(t, 0, 1);
                    break;
                default:
                    s = t = 0;
                    break;
            }

            Vector2 minp1 = seg1.TransformedA + u * s;
            Vector2 minp2 = seg2.TransformedA + v * t;

            return CircleToCircleInternal(minp1, seg1.Radius, minp2, seg2.Radius, contacts);
        }

        private static void FindPointsBehindSeg(List<Contact> contacts, ShapeSegment seg, ShapePoly poly, float dist, float coef)
        {
            float dta = MathUtil.Cross(seg.TransformedNormal, seg.TransformedA);
            float dtb = MathUtil.Cross(seg.TransformedNormal, seg.TransformedB);
            Vector2 n = seg.TransformedNormal * coef;

            for (int i = 0; i < poly.Verts.Count; i++)
            {
                Vector2 v = poly.TransformedVerts[i];
                if (Vector2.Dot(v, n) < Vector2.Dot(seg.TransformedNormal, seg.TransformedA) * coef + seg.Radius)
                {
                    float dt = MathUtil.Cross(seg.TransformedNormal, v);
                    if (dta >= dt && dt >= dtb)
                        contacts.Add(new Contact(v, n, dist, (poly.Id << 16) | i));
                }
            }
        }

        private static int SegmentToPoly(Shape a, Shape b, List<Contact> contacts)
        {
            var seg = (ShapeSegment)a;
            var poly = (ShapePoly)b;

            float segTd = Vector2.Dot(seg.TransformedNormal, seg.TransformedA);
            float segD1 = poly.DistanceOnPlane(seg.TransformedNormal, segTd) - seg.Radius;
            if (segD1 > 0) return 0;

            float segD2 = poly.DistanceOnPlane(-seg.TransformedNormal, -segTd) - seg.Radius;
            if (segD2 > 0) return 0;

            float polyD = float.NegativeInfinity;
            int polyI = -1;

            for (int i = 0; i < poly.Verts.Count; i++)
            {
                var plane = poly.TransformedPlanes[i];
                float dist = seg.DistanceOnPlane(plane.Normal, plane.Distance);
                if (dist > 0) return 0;

                if (dist > polyD)
                {
                    polyD = dist;
                    polyI = i;
                }
            }

            Vector2 polyN = -poly.TransformedPlanes[polyI].Normal;
            Vector2 va = seg.TransformedA + polyN * seg.Radius;
            Vector2 vb = seg.TransformedB + polyN * seg.Radius;

            if (poly.ContainsPoint(va))
                contacts.Add(new Contact(va, polyN, polyD, (seg.Id << 16) | 0));
            if (poly.ContainsPoint(vb))
                contacts.Add(new Contact(vb, polyN, polyD, (seg.Id << 16) | 1));

            polyD -= 0.1f;
            if (segD1 >= polyD || segD2 >= polyD)
            {
                if (segD1 > segD2)
                    FindPointsBehindSeg(contacts, seg, poly, segD1, 1);
                else
                    FindPointsBehindSeg(contacts, seg, poly, segD2, -1);
            }

            if (contacts.Count == 0)
            {
                Vector2 polyA = poly.TransformedVerts[polyI];
                Vector2 polyB = poly.TransformedVerts[(polyI + 1) % poly.Verts.Count];

                if (CircleToCircleInternal(seg.TransformedA, seg.Radius, polyA, 0, contacts) > 0) return 1;
                if (CircleToCircleInternal(seg.TransformedB, seg.Radius, polyA, 0, contacts) > 0) return 1;
                if (CircleToCircleInternal(seg.TransformedA, seg.Radius, polyB, 0, contacts) > 0) return 1;
                if (CircleToCircleInternal(seg.TransformedB, seg.Radius, polyB, 0, contacts) > 0) return 1;
            }

            return contacts.Count;
        }

        private static (float Dist, int Index) FindMSA(ShapePoly poly, List<ShapePoly.Plane> planes, int num)
        {
            float minDist = float.NegativeInfinity;
            int minIdx = -1;

            for (int i = 0; i < num; i++)
            {
                float dist = poly.DistanceOnPlane(planes[i].Normal, planes[i].Distance);
                if (dist > 0) return (0, -1);
                if (dist > minDist)
                {
                    minDist = dist;
                    minIdx = i;
                }
            }
            return (minDist, minIdx);
        }

        private static int FindVertsFallback(List<Contact> contacts, ShapePoly poly1, ShapePoly poly2, Vector2 n, float dist)
        {
            int num = 0;
            for (int i = 0; i < poly1.Verts.Count; i++)
            {
                Vector2 v = poly1.TransformedVerts[i];
                if (poly2.ContainsPointPartial(v, n))
                {
                    contacts.Add(new Contact(v, n, dist, (poly1.Id << 16) | i));
                    num++;
                }
            }

            for (int i = 0; i < poly2.Verts.Count; i++)
            {
                Vector2 v = poly2.TransformedVerts[i];
                if (poly1.ContainsPointPartial(v, n))
                {
                    contacts.Add(new Contact(v, n, dist, (poly2.Id << 16) | i));
                    num++;
                }
            }
            return num;
        }

        private static int FindVerts(List<Contact> contacts, ShapePoly poly1, ShapePoly poly2, Vector2 n, float dist)
        {
            int num = 0;
            for (int i = 0; i < poly1.Verts.Count; i++)
            {
                Vector2 v = poly1.TransformedVerts[i];
                if (poly2.ContainsPoint(v))
                {
                    contacts.Add(new Contact(v, n, dist, (poly1.Id << 16) | i));
                    num++;
                }
            }

            for (int i = 0; i < poly2.Verts.Count; i++)
            {
                Vector2 v = poly2.TransformedVerts[i];
                if (poly1.ContainsPoint(v))
                {
                    contacts.Add(new Contact(v, n, dist, (poly2.Id << 16) | i));
                    num++;
                }
            }

            return num > 0 ? num : FindVertsFallback(contacts, poly1, poly2, n, dist);
        }

        private static int PolyToPoly(Shape a, Shape b, List<Contact> contacts)
        {
            var poly1 = (ShapePoly)a;
            var poly2 = (ShapePoly)b;

            var msa1 = FindMSA(poly2, poly1.TransformedPlanes, poly1.Verts.Count);
            if (msa1.Index == -1) return 0;

            var msa2 = FindMSA(poly1, poly2.TransformedPlanes, poly2.Verts.Count);
            if (msa2.Index == -1) return 0;

            if (msa1.Dist > msa2.Dist)
                return FindVerts(contacts, poly1, poly2, poly1.TransformedPlanes[msa1.Index].Normal, msa1.Dist);

            return FindVerts(contacts, poly1, poly2, -poly2.TransformedPlanes[msa2.Index].Normal, msa2.Dist);
        }

        public static int Collide(Shape a, Shape b, List<Contact> contacts)
        {
            if(a is ShapeCircle)
            {
                if (b is ShapeCircle) return CircleToCircle(a, b, contacts);
                if (b is ShapeSegment) return CircleToSegment(a, b, contacts);
                if (b is ShapePoly) return CircleToPoly(a, b, contacts);
            } 
            else if(a is ShapeSegment)
            {
                if (b is ShapeCircle) return CircleToSegment(b, a, contacts);
                if (b is ShapeSegment) return SegmentToSegment(a, b, contacts);
                if (b is ShapePoly) return SegmentToPoly(a, b, contacts);
            }
            else if(a is ShapePoly)
            {
                if (b is ShapeCircle) return CircleToPoly(b, a, contacts);
                if (b is ShapeSegment) return SegmentToPoly(b, a, contacts);
                if (b is ShapePoly) return PolyToPoly(a, b, contacts);
            }

            throw new NotSupportedException($"Collision not supported between shapes of type {a.GetType()} and {b.GetType()}");
        }
    }
}
