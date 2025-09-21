namespace Physics2D
{
    public static class Collision
    {
        private static int CircleToCircleInternal(Vec2 c1, float r1, Vec2 c2, float r2, List<Contact> contacts)
        {
            float rmax = r1 + r2;
            Vec2 t = c2 - c1;
            float distSq = t.LengthSquared();

            if (distSq > rmax * rmax) return 0;

            float dist = MathF.Sqrt(distSq);
            Vec2 p = c1 + t * (0.5f + (r1 - r2) * 0.5f / (dist == 0 ? 0.01f : dist));
            //Vec2 p = Vec2.Mad(c1, t, 0.5f + (r1 - r2) * 0.5f / dist);
            Vec2 n = dist != 0 ? t / dist : Vec2.Zero;
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

            float dn = Vec2.Dot(circ.TransformedCenter, seg.TransformedNormal) - Vec2.Dot(seg.TransformedA, seg.TransformedNormal);
            float dist = MathF.Abs(dn) - rsum;
            if (dist > 0) return 0;

            float dt = Vec2.Cross(circ.TransformedCenter, seg.TransformedNormal);
            float dtMin = Vec2.Cross(seg.TransformedA, seg.TransformedNormal);
            float dtMax = Vec2.Cross(seg.TransformedB, seg.TransformedNormal);

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

            Vec2 n = dn > 0 ? seg.TransformedNormal : Vec2.Neg(seg.TransformedNormal);
            contacts.Add(new Contact(Vec2.Mad(circ.TransformedCenter, n, -(circ.Radius + dist * 0.5f)), Vec2.Neg(n), dist, 0));
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
                float dist = Vec2.Dot(circ.TransformedCenter, plane.Normal) - plane.Distance - circ.Radius;
                if (dist > 0) return 0;
                if (dist > minDist)
                {
                    minDist = dist;
                    minIdx = i;
                }
            }

            Vec2 n = poly.TransformedPlanes[minIdx].Normal;
            Vec2 aVert = poly.TransformedVerts[minIdx];
            Vec2 bVert = poly.TransformedVerts[(minIdx + 1) % poly.Verts.Count];
            float dta = Vec2.Cross(aVert, n);
            float dtb = Vec2.Cross(bVert, n);
            float dtc = Vec2.Cross(circ.TransformedCenter, n);

            if (dtc > dta)
                return CircleToCircleInternal(circ.TransformedCenter, circ.Radius, aVert, 0, contacts);
            else if (dtc < dtb)
                return CircleToCircleInternal(circ.TransformedCenter, circ.Radius, bVert, 0, contacts);

            contacts.Add(new Contact(Vec2.Mad(circ.TransformedCenter, n, -(circ.Radius + minDist * 0.5f)), Vec2.Neg(n), minDist, 0));
            //contacts.Add(new Contact(Vec2.Mad(circ.TransformedCenter, n, -(circ.Radius + minDist * 0.5f)), n, minDist, 0));
            return 1;
        }

        private static float SegmentPointDistanceSq(ShapeSegment seg, Vec2 p)
        {
            Vec2 w = p - seg.TransformedA;
            Vec2 d = seg.TransformedB - seg.TransformedA;
            float proj = Vec2.Dot(w, d);

            if (proj <= 0) return Vec2.Dot(w, w);

            float vsq = Vec2.Dot(d, d);
            if (proj >= vsq) return Vec2.Dot(w, w) - 2 * proj + vsq;

            return Vec2.Dot(w, w) - proj * proj / vsq;
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
            Vec2 u = seg1.TransformedB - seg1.TransformedA;
            Vec2 v = seg2.TransformedB - seg2.TransformedA;

            switch (idxm)
            {
                case 0:
                    s = Vec2.Dot(seg2.TransformedA - seg1.TransformedA, u) / Vec2.Dot(u, u);
                    s = Math.Clamp(s, 0, 1);
                    t = 0;
                    break;
                case 1:
                    s = Vec2.Dot(seg2.TransformedB - seg1.TransformedA, u) / Vec2.Dot(u, u);
                    s = Math.Clamp(s, 0, 1);
                    t = 1;
                    break;
                case 2:
                    s = 0;
                    t = Vec2.Dot(seg1.TransformedA - seg2.TransformedA, v) / Vec2.Dot(v, v);
                    t = Math.Clamp(t, 0, 1);
                    break;
                case 3:
                    s = 1;
                    t = Vec2.Dot(seg1.TransformedB - seg2.TransformedA, v) / Vec2.Dot(v, v);
                    t = Math.Clamp(t, 0, 1);
                    break;
                default:
                    s = t = 0;
                    break;
            }

            Vec2 minp1 = seg1.TransformedA + u * s;
            Vec2 minp2 = seg2.TransformedA + v * t;

            return CircleToCircleInternal(minp1, seg1.Radius, minp2, seg2.Radius, contacts);
        }

        private static void FindPointsBehindSeg(List<Contact> contacts, ShapeSegment seg, ShapePoly poly, float dist, float coef)
        {
            float dta = Vec2.Cross(seg.TransformedNormal, seg.TransformedA);
            float dtb = Vec2.Cross(seg.TransformedNormal, seg.TransformedB);
            Vec2 n = seg.TransformedNormal * coef;

            for (int i = 0; i < poly.Verts.Count; i++)
            {
                Vec2 v = poly.TransformedVerts[i];
                if (Vec2.Dot(v, n) < Vec2.Dot(seg.TransformedNormal, seg.TransformedA) * coef + seg.Radius)
                {
                    float dt = Vec2.Cross(seg.TransformedNormal, v);
                    if (dta >= dt && dt >= dtb)
                        contacts.Add(new Contact(v, n, dist, (poly.Id << 16) | i));
                }
            }
        }

        private static int SegmentToPoly(Shape a, Shape b, List<Contact> contacts)
        {
            var seg = (ShapeSegment)a;
            var poly = (ShapePoly)b;

            float segTd = Vec2.Dot(seg.TransformedNormal, seg.TransformedA);
            float segD1 = poly.DistanceOnPlane(seg.TransformedNormal, segTd) - seg.Radius;
            if (segD1 > 0) return 0;

            float segD2 = poly.DistanceOnPlane(Vec2.Neg(seg.TransformedNormal), -segTd) - seg.Radius;
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

            Vec2 polyN = Vec2.Neg(poly.TransformedPlanes[polyI].Normal);
            Vec2 va = seg.TransformedA + polyN * seg.Radius;
            Vec2 vb = seg.TransformedB + polyN * seg.Radius;

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
                Vec2 polyA = poly.TransformedVerts[polyI];
                Vec2 polyB = poly.TransformedVerts[(polyI + 1) % poly.Verts.Count];

                if (CircleToCircleInternal(seg.TransformedA, seg.Radius, polyA, 0, contacts) > 0) return 1;
                if (CircleToCircleInternal(seg.TransformedB, seg.Radius, polyA, 0, contacts) > 0) return 1;
                if (CircleToCircleInternal(seg.TransformedA, seg.Radius, polyB, 0, contacts) > 0) return 1;
                if (CircleToCircleInternal(seg.TransformedB, seg.Radius, polyB, 0, contacts) > 0) return 1;
            }

            return contacts.Count;
        }

        private static (float Dist, int Index) FindMSA(ShapePoly poly, List<Plane> planes, int num)
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

        private static int FindVertsFallback(List<Contact> contacts, ShapePoly poly1, ShapePoly poly2, Vec2 n, float dist)
        {
            int num = 0;
            for (int i = 0; i < poly1.Verts.Count; i++)
            {
                Vec2 v = poly1.TransformedVerts[i];
                if (poly2.ContainsPointPartial(v, n))
                {
                    contacts.Add(new Contact(v, n, dist, (poly1.Id << 16) | i));
                    num++;
                }
            }

            for (int i = 0; i < poly2.Verts.Count; i++)
            {
                Vec2 v = poly2.TransformedVerts[i];
                if (poly1.ContainsPointPartial(v, n))
                {
                    contacts.Add(new Contact(v, n, dist, (poly2.Id << 16) | i));
                    num++;
                }
            }
            return num;
        }

        private static int FindVerts(List<Contact> contacts, ShapePoly poly1, ShapePoly poly2, Vec2 n, float dist)
        {
            int num = 0;
            for (int i = 0; i < poly1.Verts.Count; i++)
            {
                Vec2 v = poly1.TransformedVerts[i];
                if (poly2.ContainsPoint(v))
                {
                    contacts.Add(new Contact(v, n, dist, (poly1.Id << 16) | i));
                    num++;
                }
            }

            for (int i = 0; i < poly2.Verts.Count; i++)
            {
                Vec2 v = poly2.TransformedVerts[i];
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

            return FindVerts(contacts, poly1, poly2, Vec2.Neg(poly2.TransformedPlanes[msa2.Index].Normal), msa2.Dist);
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
