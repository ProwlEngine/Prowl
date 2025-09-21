using System;
using System.Collections.Generic;

namespace Prowl.Drift
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

    }
}
