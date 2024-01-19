using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;
using Prowl.Icons;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime
{
    [AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Capsules}  Mesh Collider")]
    public class MeshCollider : Collider
    {
        public AssetRef<Mesh> mesh;

        public bool convex = false;

        public enum Approximation
        {
            Level1 = 6,
            Level2 = 7,
            Level3 = 8,
            Level4 = 9,
            Level5 = 10,
            Level6 = 11,
            Level7 = 12,
            Level8 = 13,
            Level9 = 15,
            Level10 = 20,
            Level15 = 25,
            Level20 = 30
        }

        public Approximation convexApprox = Approximation.Level5;

        public override List<Shape> CreateShapes()
        {
            if (mesh.IsAvailable == false) return [new SphereShape(0.001f)]; // Mesh is missing so we create a sphere with a tiny radius to prevent errors

            if (!convex) {
                var indices = mesh.Res.triangles;
                var vertices = mesh.Res.vertices;

                List<JTriangle> triangles = new();

                for (int i = 0; i < mesh.Res.triangleCount; i += 3) {
                    JVector v1 = vertices[i + 0].Position.ToDouble();
                    JVector v2 = vertices[i + 1].Position.ToDouble();
                    JVector v3 = vertices[i + 2].Position.ToDouble();
                    triangles.Add(new JTriangle(v1, v2, v3));
                }

                var jtm = new TriangleMesh(triangles);
                List<Shape> shapesToAdd = new();

                for (int i = 0; i < jtm.Indices.Length; i++) {
                    TriangleShape ts = new TriangleShape(jtm, i);
                    shapesToAdd.Add(ts);
                }
                return shapesToAdd;
            } else {
                var points = mesh.Res.vertices.Select(x => (JVector)x.Position.ToDouble());
                return [new PointCloudShape(BuildConvexCloud(points.ToList()))];
            }

        }

        private List<JVector> BuildConvexCloud(List<JVector> pointCloud)
        {
            List<JVector> allIndices = new();

            int steps = (int)convexApprox;

            for (int thetaIndex = 0; thetaIndex < steps; thetaIndex++) {
                // [0,PI]
                float theta = MathF.PI / (steps - 1) * thetaIndex;
                float sinTheta = (float)Math.Sin(theta);
                float cosTheta = (float)Math.Cos(theta);

                for (int phiIndex = 0; phiIndex < steps; phiIndex++) {
                    // [-PI,PI]
                    float phi = (2.0f * MathF.PI) / (steps - 0) * phiIndex - MathF.PI;
                    float sinPhi = (float)Math.Sin(phi);
                    float cosPhi = (float)Math.Cos(phi);

                    JVector dir = new JVector(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);

                    int index = FindExtremePoint(pointCloud, ref dir);
                    allIndices.Add(pointCloud[index]);
                }
            }

            return allIndices.Distinct().ToList();
        }

        private static int FindExtremePoint(List<JVector> points, ref JVector dir)
        {
            int index = 0;
            float current = float.MinValue;

            JVector point; float value;

            for (int i = 1; i < points.Count; i++) {
                point = points[i];

                value = JVector.Dot(ref point, ref dir);
                if (value > current) { current = value; index = i; }
            }

            return index;
        }

    }

}