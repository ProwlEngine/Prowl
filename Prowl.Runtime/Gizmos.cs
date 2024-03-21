using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public static class Gizmos
    {
        private readonly static PrimitiveBatch LineBatch = new(Silk.NET.OpenGL.PrimitiveType.Lines);
        private readonly static List<(Gizmo, Matrix4x4)> gizmos = new(100);
        private static Material mat;

        public static Matrix4x4 Matrix = Matrix4x4.Identity;

        public static void Line(Vector3 pointA, Vector3 pointB, Color color) => Add(new LineGizmo(pointA, pointB, color));

        public static void Cube(Color color) => Add(new CubeGizmo(color));

        public static void Triangle(Color color) => Add(new TriangleGizmo(color));

        public static void Quad(Color color) => Add(new QuadGizmo(color));

        public static void Polygon(Vector3[] points, Color color, bool closed = false) => Add(new PolygonGizmo(points, color, closed));


        public static void Circle(Color color) => Add(new CircleGizmo(color));
        public static void DirectionalLight(Color color) => Add(new DirectionalLightGizmo(color));
        public static void Sphere(Color color) => Add(new SphereGizmo(color));
        public static void Spotlight(float distance, float angle, Color color) => Add(new SpotlightGizmo(distance, angle, color));

        private static void Add(Gizmo gizmo)
        {
            gizmos.Add((gizmo, Matrix));
            Matrix = Matrix4x4.Identity;
        }

        public static void Render()
        {
            try {
                mat ??= new Material(Shader.Find("Defaults\\Gizmos.shader"));
            } catch {
                return; // Happens when no project is loaded (Or no Gizmos shader was found)
            }

            if (LineBatch.IsUploaded == false) {
                foreach (var gizmo in gizmos)
                    try {
                        gizmo.Item1.Render(LineBatch, gizmo.Item2);
                    } catch {
                        // Nothing, errors are normal here
                    }
                LineBatch.Upload();
            }

            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatProjection);
            mat.SetMatrix("mvp", mvp);
            mat.SetPass(0, true);
            using (Graphics.UseBlendMode(BlendMode.Additive))
            LineBatch.Draw();
        }

        public static void Clear()
        {
            gizmos.Clear();
            LineBatch.Reset();
        }
    }

    public abstract class Gizmo
    {
        public Matrix4x4 matrix;
        public Vector3 Pos(Vector3 worldPos)
        {
            Vector3 transformedPos = Vector3.Transform(worldPos, matrix);
            return transformedPos;
        }

        public abstract void Render(PrimitiveBatch batch, Matrix4x4 worldMatrix);
    }

    public class LineGizmo(Vector3 start, Vector3 end, Color color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;
            batch.Line(Pos(start), Pos(end), color, color);
        }
    }

    public class TriangleGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;
            batch.Line(Pos(new(-0.5, -0.5, 0.0)), Pos(new(0.0, 0.5, 0.0)), color, color);
            batch.Line(Pos(new(0.0, 0.5, 0.0)), Pos(new(0.5, -0.5, 0.0)), color, color);
            batch.Line(Pos(new(0.5, -0.5, 0.0)), Pos(new(-0.5, -0.5, 0.0)), color, color);
        }
    }

    public class QuadGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;
            batch.Line(Pos(new(-0.5, -0.5, 0.0)), Pos(new(-0.5, 0.5, 0.0)), color, color);
            batch.Line(Pos(new(-0.5, 0.5, 0.0)), Pos(new(0.5, 0.5, 0.0)), color, color);
            batch.Line(Pos(new(0.5, 0.5, 0.0)), Pos(new(0.5, -0.5, 0.0)), color, color);
            batch.Line(Pos(new(0.5, -0.5, 0.0)), Pos(new(-0.5, -0.5, 0.0)), color, color);
        }
    }

    public class PolygonGizmo(Vector3[] points, Vector4 color, bool closed = false) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;
            for (int i = 0; i < points.Length - 1; i++)
                batch.Line(Pos(points[i]), Pos(points[i + 1]), color, color);
            if (closed)
                batch.Line(Pos(points[points.Length - 1]), Pos(points[0]), color, color);
        }
    }


    public class CircleGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            int numSegments = 12;

            for (int i = 0; i < numSegments; i++) {
                float angle = (float)i / numSegments * 2f * MathF.PI;
                float angle2 = (float)(i + 1) / numSegments * 2f * MathF.PI;

                Vector3 point1 = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
                Vector3 point2 = new Vector3(MathF.Cos(angle2), MathF.Sin(angle2), 0f);

                batch.Line(Pos(point1), Pos(point2), color, color);
            }
        }
    }

    public class DirectionalLightGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            int numSegments = 6;  // Adjust for smoother or more segmented circle

            for (int i = 0; i < numSegments; i++) {
                float angle = (float)i / numSegments * 2f * MathF.PI;
                float angle2 = (float)(i + 1) / numSegments * 2f * MathF.PI;

                Vector3 point1 = 0.5f * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
                Vector3 point2 = 0.5f * new Vector3(MathF.Cos(angle2), MathF.Sin(angle2), 0f);

                batch.Line(Pos(point1), Pos(point1 + Vector3.forward), color, color);
                batch.Line(Pos(point1), Pos(point2), color, color);
            }
        }
    }

    public class SphereGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            // Use 3 Circle3D gizmo's
            new CircleGizmo(color).Render(batch, m);
            m = Matrix4x4.CreateRotationX(MathF.PI / 2f) * m;
            new CircleGizmo(color).Render(batch, m);
            m = Matrix4x4.CreateRotationY(MathF.PI / 2f) * m;
            new CircleGizmo(color).Render(batch, m);
        }
    }

    public class CubeGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            // Draw cube lines
            Vector3[] points = new Vector3[8];
            points[0] = new Vector3(-0.5f, -0.5f, -0.5f);
            points[1] = new Vector3(0.5f, -0.5f, -0.5f);
            points[2] = new Vector3(0.5f, 0.5f, -0.5f);
            points[3] = new Vector3(-0.5f, 0.5f, -0.5f);
            points[4] = new Vector3(-0.5f, -0.5f, 0.5f);
            points[5] = new Vector3(0.5f, -0.5f, 0.5f);
            points[6] = new Vector3(0.5f, 0.5f, 0.5f);
            points[7] = new Vector3(-0.5f, 0.5f, 0.5f);

            batch.Line(Pos(points[0]), Pos(points[1]), color, color);
            batch.Line(Pos(points[1]), Pos(points[2]), color, color);
            batch.Line(Pos(points[2]), Pos(points[3]), color, color);
            batch.Line(Pos(points[3]), Pos(points[0]), color, color);
            batch.Line(Pos(points[4]), Pos(points[5]), color, color);
            batch.Line(Pos(points[5]), Pos(points[6]), color, color);
            batch.Line(Pos(points[6]), Pos(points[7]), color, color);
            batch.Line(Pos(points[7]), Pos(points[4]), color, color);
            batch.Line(Pos(points[0]), Pos(points[4]), color, color);
            batch.Line(Pos(points[1]), Pos(points[5]), color, color);
            batch.Line(Pos(points[2]), Pos(points[6]), color, color);
            batch.Line(Pos(points[3]), Pos(points[7]), color, color);
        }
    }

    public class SpotlightGizmo(float distance, float angle, Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            // Calculate the cone vertices
            //Vector3 coneTip = Vector3.UnitZ * Distance;
            Vector3 coneBaseLeft = Vector3.Transform(Vector3.forward * distance, Matrix4x4.CreateRotationY(-(angle / 2)));
            Vector3 coneBaseRight = Vector3.Transform(Vector3.forward * distance, Matrix4x4.CreateRotationY((angle / 2)));
            Vector3 coneBaseTop = Vector3.Transform(Vector3.forward * distance, Matrix4x4.CreateRotationX(-(angle / 2)));
            Vector3 coneBaseBottom = Vector3.Transform(Vector3.forward * distance, Matrix4x4.CreateRotationX((angle / 2)));
            float coneBaseRadius = MathF.Tan(angle / 2) * distance;
            float coneBaseDistance = MathF.Sqrt((coneBaseRadius * coneBaseRadius) + (distance * distance));

            // Draw cone lines
            //batch.Line(Pos(Vector3.Zero), Pos(coneTip), ImGui.GetColorU32(Color));
            batch.Line(Pos(Vector3.zero), Pos(coneBaseLeft), color, color);
            batch.Line(Pos(Vector3.zero), Pos(coneBaseRight), color, color);
            batch.Line(Pos(Vector3.zero), Pos(coneBaseTop), color, color);
            batch.Line(Pos(Vector3.zero), Pos(coneBaseBottom), color, color);

            // Use 3 Circle3D gizmo's
            m = Matrix4x4.CreateTranslation(Vector3.forward * coneBaseDistance) * m;
            m = Matrix4x4.CreateScale(coneBaseRadius) * m;
            new CircleGizmo(color).Render(batch, m);
        }
    }

}