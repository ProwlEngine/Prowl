using System;
using System.Collections.Generic;

namespace Physics2D
{
    public static class Geometry
    {
        public static float AreaForCircle(float radiusOuter, float radiusInner) =>
            MathF.PI * (radiusOuter * radiusOuter - radiusInner * radiusInner);

        public static float InertiaForCircle(float mass, Vec2 center, float radiusOuter, float radiusInner) =>
            mass * ((radiusOuter * radiusOuter + radiusInner * radiusInner) * 0.5f + center.LengthSquared());

        public static float AreaForSegment(Vec2 a, Vec2 b, float radius) =>
            radius * (MathF.PI * radius + 2 * Vec2.Distance(a, b));

        public static Vec2 CentroidForSegment(Vec2 a, Vec2 b) =>
            (a + b) * 0.5f;

        public static float InertiaForSegment(float mass, Vec2 a, Vec2 b)
        {
            float distSq = (b - a).LengthSquared();
            Vec2 offset = (a + b) * 0.5f;
            return mass * (distSq / 12f + offset.LengthSquared());
        }

        public static float AreaForPoly(IReadOnlyList<Vec2> verts)
        {
            float area = 0;
            for (int i = 0; i < verts.Count; i++)
                area += Vec2.Cross(verts[i], verts[(i + 1) % verts.Count]);
            return area / 2f;
        }

        public static Vec2 CentroidForPoly(IReadOnlyList<Vec2> verts)
        {
            float area = 0;
            Vec2 vsum = Vec2.Zero;

            for (int i = 0; i < verts.Count; i++)
            {
                var v1 = verts[i];
                var v2 = verts[(i + 1) % verts.Count];
                float cross = Vec2.Cross(v1, v2);

                area += cross;
                vsum += (v1 + v2) * cross;
            }

            return vsum * (1f / (3f * area));
        }

        public static float InertiaForPoly(float mass, IReadOnlyList<Vec2> verts, Vec2 offset)
        {
            float sum1 = 0;
            float sum2 = 0;

            for (int i = 0; i < verts.Count; i++)
            {
                Vec2 v1 = verts[i] + offset;
                Vec2 v2 = verts[(i + 1) % verts.Count] + offset;

                float a = Vec2.Cross(v2, v1);
                float b = Vec2.Dot(v1, v1) + Vec2.Dot(v1, v2) + Vec2.Dot(v2, v2);

                sum1 += a * b;
                sum2 += a;
            }

            return (mass * sum1) / (6 * sum2);
        }

        public static float InertiaForBox(float mass, float w, float h) =>
            mass * (w * w + h * h) / 12f;

        // Convex hull using Gift Wrapping algorithm
        public static List<Vec2> CreateConvexHull(List<Vec2> points)
        {
            int i0 = 0;
            float x0 = points[0].X;
            for (int i = 1; i < points.Count; i++)
            {
                float x = points[i].X;
                if (x > x0 || (x == x0 && points[i].Y < points[i0].Y))
                {
                    i0 = i;
                    x0 = x;
                }
            }

            int n = points.Count;
            var hull = new List<int>();
            int ih = i0;

            while (true)
            {
                hull.Add(ih);

                int ie = 0;
                for (int j = 1; j < n; j++)
                {
                    if (ie == ih)
                    {
                        ie = j;
                        continue;
                    }

                    Vec2 r = points[ie] - points[hull[^1]];
                    Vec2 v = points[j] - points[hull[^1]];
                    float c = Vec2.Cross(r, v);

                    if (c < 0 || (c == 0 && v.LengthSquared() > r.LengthSquared()))
                        ie = j;
                }

                ih = ie;
                if (ie == i0) break;
            }

            var newPoints = new List<Vec2>();
            foreach (int idx in hull)
                newPoints.Add(points[idx]);
            return newPoints;
        }
    }
}
