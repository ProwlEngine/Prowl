using HexaEngine.ImGuiNET;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    public static class Gizmos
    {
        private readonly static List<(Gizmo, Matrix4x4)> gizmos = new(100);

        public static Matrix4x4 Matrix = Matrix4x4.Identity;

        public static void Line(Vector3 pointA, Vector3 pointB, Color color, float thickness = 1f) => gizmos.Add((new LineGizmo(pointA, pointB, thickness, color), Matrix));

        public static void Text(Vector3 position, string text, Color color) => gizmos.Add((new TextGizmo(position, text, color), Matrix));

        public static void Circle(Vector3 center, float radiusInPixels, Color color, float thickness = 1f) => gizmos.Add((new CircleGizmo(center, radiusInPixels, color, thickness), Matrix));

        public static void CircleFilled(Vector3 center, float radiusInPixels, Color color) => gizmos.Add((new CircleFilledGizmo(center, radiusInPixels, color), Matrix));
        public static void Cube(Color color, float thickness = 1f) => gizmos.Add((new CubeGizmo(color, thickness), Matrix));

        public static void Triangle(Vector3 pointA, Vector3 pointB, Vector3 pointC, Color color, float thickness = 1f) => gizmos.Add((new TriangleGizmo(pointA, pointB, pointC, color, thickness), Matrix));

        public static void TriangleFilled(Vector3 pointA, Vector3 pointB, Vector3 pointC, Color color) => gizmos.Add((new TriangleFilledGizmo(pointA, pointB, pointC, color), Matrix));

        public static void Quad(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 pointD, Color color, float thickness = 1f) => gizmos.Add((new QuadGizmo(pointA, pointB, pointC, pointD, color, thickness), Matrix));

        public static void QuadFilled(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 pointD, Color color) => gizmos.Add((new QuadFilledGizmo(pointA, pointB, pointC, pointD, color), Matrix));

        public static void Polygon(Vector3[] points, Color color, bool closed = false, float thickness = 1f) => gizmos.Add((new PolygonGizmo(points, color, closed, thickness), Matrix));

        public static void Image(Vector3 position, Vector2 size, Texture2D texture, Color color) => gizmos.Add((new ImageGizmo(position, size, texture, color), Matrix));


        public static void Circle3D(Color color, float thickness = 1f) => gizmos.Add((new Circle3DGizmo(color, thickness), Matrix));
        public static void DirectionalLight(Color color, float thickness = 1f) => gizmos.Add((new DirectionalLightGizmo(color, thickness), Matrix));
        public static void Sphere(Color color, float thickness = 1f) => gizmos.Add((new SphereGizmo(color, thickness), Matrix));
        public static void Spotlight(float distance, float angle, Color color, float thickness = 1f) => gizmos.Add((new SpotlightGizmo(distance, angle, color, thickness), Matrix));


        public static void Render(ImDrawListPtr drawList, Matrix4x4 mvp)
        {
            foreach (var gizmo in gizmos)
                try
                {
                    gizmo.Item1.Render(drawList, mvp, gizmo.Item2);
                }
                catch
                {
                    // Nothing, errors are normal here
                }
        }

        public static void Clear()
        {
            gizmos.Clear();
        }
    }

    public abstract class Gizmo
    {
        public Matrix4x4 matrix;
        public Matrix4x4 mvp;
        public Vector2 Pos(Vector3 worldPos)
        {
            Vector3 transformedPos = Vector3.Transform(worldPos, matrix);
            Vector4 trans = Vector4.Transform(new Vector4(transformedPos.X, transformedPos.Y, transformedPos.Z, 1.0f), mvp);
            //Debug.Log(trans.Z.ToString());
            if(trans.Z < 0)
            {
#warning Nothing we can do here is there? ImGUI has no way to handle lines behind the camera that i can find.
                throw new Exception("Line Behind Camera, This is intended and should be caught");
            }
            trans *= 0.5f / trans.W;
            trans += new Vector4(0.5f, 0.5f, 0f, 0f);
            trans.Y = 1f - trans.Y;
            var windowSize = ImGui.GetWindowSize();
            trans.X *= windowSize.X;
            trans.Y *= windowSize.Y;
            var position = ImGui.GetWindowPos();
            trans.X += position.X;
            trans.Y += position.Y;
            return new Vector2(trans.X, trans.Y);
        }

        public abstract void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 worldMatrix);
    }

    public class LineGizmo(Vector3 start, Vector3 end, float thickness, Vector4 color) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddLine(Pos(start), Pos(end), ImGui.GetColorU32(color), thickness);
        }
    }

    public class CircleGizmo(Vector3 center, float radius, Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddCircle(Pos(center), radius, ImGui.GetColorU32(color), thickness); 
        }
    }

    public class CircleFilledGizmo(Vector3 center, float radius, Vector4 color) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddCircleFilled(Pos(center), radius, ImGui.GetColorU32(color));
        }
    }

    public class TextGizmo(Vector3 position, string text, Vector4 color) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddText(Pos(position), ImGui.GetColorU32(color), text);
        }
    }

    public class TriangleGizmo(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddTriangle(Pos(pointA), Pos(pointB), Pos(pointC), ImGui.GetColorU32(color), thickness);
        }
    }

    public class TriangleFilledGizmo(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector4 color) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddTriangleFilled(Pos(pointA), Pos(pointB), Pos(pointC), ImGui.GetColorU32(color));
        }
    }

    public class QuadGizmo(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 pointD, Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddQuad(Pos(pointA), Pos(pointB), Pos(pointC), Pos(pointD), ImGui.GetColorU32(color), thickness);
        }
    }

    public class QuadFilledGizmo(Vector3 pointA, Vector3 pointB, Vector3 pointC, Vector3 pointD, Vector4 color) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddQuadFilled(Pos(pointA), Pos(pointB), Pos(pointC), Pos(pointD), ImGui.GetColorU32(color));
        }
    }

    public class PolygonGizmo(Vector3[] points, Vector4 color, bool closed = false, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            var p = new System.Numerics.Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
                p[i] = Pos(points[i]);
            drawList.AddPolyline(ref p[0], p.Length, ImGui.GetColorU32(color), closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);
        }
    }

    public class ImageGizmo(Vector3 position, Vector2 size, Texture2D texture, Vector4 color) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;
            drawList.AddImage(new ImTextureID((nint)texture.Handle), Pos(position), Pos(position + new Vector3(size.X, size.Y, 0f)), Vector2.Zero, Vector2.One, ImGui.GetColorU32(color));
        }
    }


    public class Circle3DGizmo(Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;

            int numSegments = 12;

            for (int i = 0; i < numSegments; i++)
            {
                float angle = (float)i / numSegments * 2f * MathF.PI;
                float angle2 = (float)(i + 1) / numSegments * 2f * MathF.PI;

                Vector3 point1 = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
                Vector3 point2 = new Vector3(MathF.Cos(angle2), MathF.Sin(angle2), 0f);

                drawList.AddLine(Pos(point1), Pos(point2), ImGui.GetColorU32(color), thickness);
            }
        }
    }

    public class DirectionalLightGizmo(Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;

            int numSegments = 6;  // Adjust for smoother or more segmented circle

            for (int i = 0; i < numSegments; i++)
            {
                float angle = (float)i / numSegments * 2f * MathF.PI;
                float angle2 = (float)(i + 1) / numSegments * 2f * MathF.PI;

                Vector3 point1 = 0.5f * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
                Vector3 point2 = 0.5f * new Vector3(MathF.Cos(angle2), MathF.Sin(angle2), 0f);

                drawList.AddLine(Pos(point1), Pos(point1 + Vector3.UnitZ), ImGui.GetColorU32(color), thickness);
                drawList.AddLine(Pos(point1), Pos(point2), ImGui.GetColorU32(color), thickness);
            }
        }
    }

    public class SphereGizmo(Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;

            // Use 3 Circle3D gizmo's
            new Circle3DGizmo(color, thickness).Render(drawList, mvp, m);
            m = Matrix4x4.CreateRotationX(MathF.PI / 2f) * m;
            new Circle3DGizmo(color, thickness).Render(drawList, mvp, m);
            m = Matrix4x4.CreateRotationY(MathF.PI / 2f) * m;
            new Circle3DGizmo(color, thickness).Render(drawList, mvp, m);
        }
    }

    public class CubeGizmo(Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;

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

            drawList.AddLine(Pos(points[0]), Pos(points[1]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[1]), Pos(points[2]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[2]), Pos(points[3]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[3]), Pos(points[0]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[4]), Pos(points[5]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[5]), Pos(points[6]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[6]), Pos(points[7]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[7]), Pos(points[4]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[0]), Pos(points[4]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[1]), Pos(points[5]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[2]), Pos(points[6]), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(points[3]), Pos(points[7]), ImGui.GetColorU32(color), thickness);
        }
    }

    public class SpotlightGizmo(float distance, float angle, Vector4 color, float thickness = 1f) : Gizmo
    {
        public override void Render(ImDrawListPtr drawList, Matrix4x4 mvp, Matrix4x4 m)
        {
            base.matrix = m;
            base.mvp = mvp;

            // Calculate the cone vertices
            //Vector3 coneTip = Vector3.UnitZ * Distance;
            Vector3 coneBaseLeft = Vector3.Transform(Vector3.UnitZ * distance, Matrix4x4.CreateRotationY(-(angle / 2)));
            Vector3 coneBaseRight = Vector3.Transform(Vector3.UnitZ * distance, Matrix4x4.CreateRotationY((angle / 2)));
            Vector3 coneBaseTop = Vector3.Transform(Vector3.UnitZ * distance, Matrix4x4.CreateRotationX(-(angle / 2)));
            Vector3 coneBaseBottom = Vector3.Transform(Vector3.UnitZ * distance, Matrix4x4.CreateRotationX((angle / 2)));
            float coneBaseRadius = MathF.Tan(angle / 2) * distance;
            float coneBaseDistance = MathF.Sqrt((coneBaseRadius * coneBaseRadius) + (distance * distance));

            // Draw cone lines
            //drawList.AddLine(Pos(Vector3.Zero), Pos(coneTip), ImGui.GetColorU32(Color), thickness);
            drawList.AddLine(Pos(Vector3.Zero), Pos(coneBaseLeft), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(Vector3.Zero), Pos(coneBaseRight), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(Vector3.Zero), Pos(coneBaseTop), ImGui.GetColorU32(color), thickness);
            drawList.AddLine(Pos(Vector3.Zero), Pos(coneBaseBottom), ImGui.GetColorU32(color), thickness);

            // Use 3 Circle3D gizmo's
            m = Matrix4x4.CreateTranslation(Vector3.UnitZ * coneBaseDistance) * m;
            m = Matrix4x4.CreateScale(coneBaseRadius) * m;
            new Circle3DGizmo(color, thickness).Render(drawList, mvp, m);
        }
    }

}
