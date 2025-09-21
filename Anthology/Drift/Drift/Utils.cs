using System;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.Drift
{
    public static class Geometry
    {
        public static float AreaForCircle(float radiusOuter, float radiusInner) =>
            MathF.PI * (radiusOuter * radiusOuter - radiusInner * radiusInner);

        public static float InertiaForCircle(float mass, Vector2 center, float radiusOuter, float radiusInner) =>
            mass * ((radiusOuter * radiusOuter + radiusInner * radiusInner) * 0.5f + center.LengthSquared());

        public static float AreaForSegment(Vector2 a, Vector2 b, float radius) =>
            radius * (MathF.PI * radius + 2 * Vector2.Distance(a, b));

        public static Vector2 CentroidForSegment(Vector2 a, Vector2 b) =>
            (a + b) * 0.5f;

        public static float InertiaForSegment(float mass, Vector2 a, Vector2 b)
        {
            float distSq = (b - a).LengthSquared();
            Vector2 offset = (a + b) * 0.5f;
            return mass * (distSq / 12f + offset.LengthSquared());
        }

        public static float AreaForPoly(IReadOnlyList<Vector2> verts)
        {
            float area = 0;
            for (int i = 0; i < verts.Count; i++)
                area += MathUtil.Cross(verts[i], verts[(i + 1) % verts.Count]);
            return area / 2f;
        }

        public static Vector2 CentroidForPoly(IReadOnlyList<Vector2> verts)
        {
            float area = 0;
            Vector2 vsum = Vector2.Zero;

            for (int i = 0; i < verts.Count; i++)
            {
                var v1 = verts[i];
                var v2 = verts[(i + 1) % verts.Count];
                float cross = MathUtil.Cross(v1, v2);

                area += cross;
                vsum += (v1 + v2) * cross;
            }

            return vsum * (1f / (3f * area));
        }

        public static float InertiaForPoly(float mass, IReadOnlyList<Vector2> verts, Vector2 offset)
        {
            float sum1 = 0;
            float sum2 = 0;

            for (int i = 0; i < verts.Count; i++)
            {
                Vector2 v1 = verts[i] + offset;
                Vector2 v2 = verts[(i + 1) % verts.Count] + offset;

                float a = MathUtil.Cross(v2, v1);
                float b = Vector2.Dot(v1, v1) + Vector2.Dot(v1, v2) + Vector2.Dot(v2, v2);

                sum1 += a * b;
                sum2 += a;
            }

            return (mass * sum1) / (6 * sum2);
        }

    }
}
