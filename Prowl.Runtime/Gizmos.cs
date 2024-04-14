using System;
using System.Collections.Generic;
using Prowl.Runtime.Rendering.Primitives;

namespace Prowl.Runtime
{
    public static class Gizmos
    {
        private static PrimitiveBatch LineBatch;
        private readonly static List<(Gizmo, Matrix4x4)> gizmos = new(100);
        private static Material mat;

        public static Matrix4x4 Matrix = Matrix4x4.Identity;
        public static Color Color;

        public static void DrawLine(Vector3 from, Vector3 to)
        {
            from -= Camera.Current.GameObject.Transform.position;
            to -= Camera.Current.GameObject.Transform.position;
            Add(new LineGizmo(from, to, Color));
        }

        public static void DrawCube(Vector3 center, Vector3 size)
        {
            center -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix4x4.CreateScale(size) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new CubeGizmo(Color));
        }

        public static void DrawPolygon(Vector3[] points, bool closed = false)
        {
            for (int i = 0; i < points.Length; i++)
                points[i] -= Camera.Current.GameObject.Transform.position;
            Add(new PolygonGizmo(points, Color, closed));
        }

        public static void DrawCylinder(Vector3 center, float radius, float height)
        {
            center -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix4x4.CreateScale(new Vector3(radius, height, radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new CylinderGizmo(Color));
        }

        public static void DrawCapsule(Vector3 center, float radius, float height)
        {
            center -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix4x4.CreateScale(new Vector3(radius, height, radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new CapsuleGizmo(Color));
        }

        public static void DrawCircle(Vector3 center, float radius)
        {
            center -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix4x4.CreateScale(new Vector3(radius, radius, radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new CircleGizmo(Color));
        }

        public static void DrawSphere(Vector3 center, float radius)
        {
            center -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix4x4.CreateScale(new Vector3(radius, radius, radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new SphereGizmo(Color));
        }

        public static void DrawDirectionalLight(Vector3 center)
        {
            center -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix * Matrix4x4.CreateTranslation(center);
            Add(new DirectionalLightGizmo(Color));
        }

        public static void DrawSpotlight(Vector3 position, float distance, float spotAngle)
        {
            position -= Camera.Current.GameObject.Transform.position;
            Matrix = Matrix * Matrix4x4.CreateTranslation(position);
            Add(new SpotlightGizmo(distance, spotAngle, Color));
        }

        public static void Add(Gizmo gizmo)
        {
            gizmos.Add((gizmo, Matrix));
            Matrix = Matrix4x4.Identity;
        }

        public static void Render()
        {
            try
            {
                mat ??= new Material(Shader.Find("Defaults/Gizmos.shader"));
            }
            catch
            {
                return; // Happens when no project is loaded (Or no Gizmos shader was found)
            }

            LineBatch ??= new PrimitiveBatch(Topology.Lines);

            if (LineBatch.IsUploaded == false)
            {
                foreach (var gizmo in gizmos)
                    try
                    {
                        gizmo.Item1.Render(LineBatch, gizmo.Item2);
                    }
                    catch
                    {
                        // Nothing, errors are normal here
                    }
                LineBatch.Upload();
            }

            var mvp = Matrix4x4.Identity;
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatView);
            mvp = Matrix4x4.Multiply(mvp, Graphics.MatProjection);
            mat.SetMatrix("mvp", mvp);
            mat.SetPass(0, true);
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

            for (int i = 0; i < numSegments; i++)
            {
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

            for (int i = 0; i < numSegments; i++)
            {
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

    public class CylinderGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            int numSegments = 12;  // Adjust for smoother or more segmented circle

            for (int i = 0; i < numSegments; i++)
            {
                float angle = (float)i / numSegments * 2f * MathF.PI;
                float angle2 = (float)(i + 1) / numSegments * 2f * MathF.PI;

                Vector3 point1 = new Vector3(MathF.Cos(angle), -1f, MathF.Sin(angle)) * 0.5;
                Vector3 point2 = new Vector3(MathF.Cos(angle2), -1f, MathF.Sin(angle2)) * 0.5;
                Vector3 point3 = new Vector3(MathF.Cos(angle), 1f, MathF.Sin(angle)) * 0.5;
                Vector3 point4 = new Vector3(MathF.Cos(angle2), 1f, MathF.Sin(angle2)) * 0.5;

                batch.Line(Pos(point1), Pos(point2), color, color);
                batch.Line(Pos(point3), Pos(point4), color, color);
                batch.Line(Pos(point1), Pos(point3), color, color);
            }
        }
    }

    public class CapsuleGizmo(Vector4 color) : Gizmo
    {
        public override void Render(PrimitiveBatch batch, Matrix4x4 m)
        {
            base.matrix = m;

            int numSegments = 12;  // Adjust for smoother or more segmented circle

            // Draw the cylinder part
            for (int i = 0; i < numSegments; i++)
            {
                float angle = (float)i / numSegments * 2f * MathF.PI;
                float angle2 = (float)(i + 1) / numSegments * 2f * MathF.PI;

                Vector3 point1 = new Vector3(MathF.Cos(angle), -1f, MathF.Sin(angle)) * 0.5;
                Vector3 point2 = new Vector3(MathF.Cos(angle2), -1f, MathF.Sin(angle2)) * 0.5;
                Vector3 point3 = new Vector3(MathF.Cos(angle), 1f, MathF.Sin(angle)) * 0.5;
                Vector3 point4 = new Vector3(MathF.Cos(angle2), 1f, MathF.Sin(angle2)) * 0.5;

                batch.Line(Pos(point1), Pos(point2), color, color);
                batch.Line(Pos(point3), Pos(point4), color, color);
                batch.Line(Pos(point1), Pos(point3), color, color);
            }

            // Draw the Top Half Sphere
            Matrix4x4 topMatrix = Matrix4x4.CreateTranslation(0f, 0.5f, 0f) * m;
            base.matrix = topMatrix;
            DrawHalfSphere(batch, color, true);

            // Draw the Bottom Half Sphere
            Matrix4x4 bottomMatrix = Matrix4x4.CreateTranslation(0f, -0.5f, 0f) * m;
            base.matrix = bottomMatrix;
            DrawHalfSphere(batch, color, false);
        }

        private void DrawHalfSphere(PrimitiveBatch batch, Vector4 color, bool isTop)
        {
            int numSegments = 12; // Adjust for smoother or more segmented circle

            float angleStart = isTop ? 0f : (float)Math.PI;
            float angleEnd = isTop ? (float)Math.PI / 2f : (float)Math.PI * 3f / 2f;

            for (int i = 0; i < numSegments / 4; i++)
            {
                float angle1 = angleStart + (float)i / (numSegments / 4) * (angleEnd - angleStart);
                float angle2 = angleStart + (float)(i + 1) / (numSegments / 4) * (angleEnd - angleStart);

                for (int j = 0; j < numSegments; j++)
                {
                    float longitude = (float)j / numSegments * 2f * MathF.PI;
                    float longitude2 = (float)(j + 1) / numSegments * 2f * MathF.PI;

                    Vector3 point1 = new Vector3(
                        MathF.Sin(angle1) * MathF.Cos(longitude),
                        MathF.Cos(angle1),
                        MathF.Sin(angle1) * MathF.Sin(longitude)) * 0.5f;

                    Vector3 point2 = new Vector3(
                        MathF.Sin(angle1) * MathF.Cos(longitude2),
                        MathF.Cos(angle1),
                        MathF.Sin(angle1) * MathF.Sin(longitude2)) * 0.5f;

                    Vector3 point3 = new Vector3(
                        MathF.Sin(angle2) * MathF.Cos(longitude),
                        MathF.Cos(angle2),
                        MathF.Sin(angle2) * MathF.Sin(longitude)) * 0.5f;

                    Vector3 point4 = new Vector3(
                        MathF.Sin(angle2) * MathF.Cos(longitude2),
                        MathF.Cos(angle2),
                        MathF.Sin(angle2) * MathF.Sin(longitude2)) * 0.5f;

                    batch.Line(Pos(point1), Pos(point2), color, color);
                    batch.Line(Pos(point1), Pos(point3), color, color);

                    // Connect top and bottom rings
                    if (isTop)
                    {
                        batch.Line(Pos(point2), Pos(point4), color, color);
                    }
                    else
                    {
                        batch.Line(Pos(point3), Pos(point4), color, color);
                    }
                }
            }
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