// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI
{

    // Based on ShapeBuilder from: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

    public struct Stroke3D
    {
        public double Thickness;
        public Color32 Color;
    }

    public class GuiDraw3D(Gui gui)
    {
        public class GUI3DViewScope : IDisposable
        {
            public GUI3DViewScope(Rect viewport) => Gui.ActiveGUI.Draw3D._viewports.Push(viewport);
            public void Dispose() => Gui.ActiveGUI.Draw3D._viewports.Pop();
        }

        public class GUI3DMVPScope : IDisposable
        {
            public GUI3DMVPScope(Matrix4x4 mvp) => Gui.ActiveGUI.Draw3D._mvps.Push(mvp);
            public void Dispose() => Gui.ActiveGUI.Draw3D._mvps.Pop();
        }

        private readonly Gui _gui = gui;

        private Stack<Rect> _viewports = new Stack<Rect>();
        private Rect _viewport => _viewports.Peek();

        private Stack<Matrix4x4> _mvps = new Stack<Matrix4x4>();
        private Matrix4x4 _mvp => _mvps.Peek();

        private bool hasViewport => _viewports.Count > 0;
        private bool hasMVP => _mvps.Count > 0;

        public GUI3DViewScope Viewport(Rect viewport) => new GUI3DViewScope(viewport);
        public GUI3DMVPScope Matrix(Matrix4x4 mvp) => new GUI3DMVPScope(mvp);

        public void Arc(double radius, double startAngle, double endAngle, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var startRad = startAngle * MathD.Deg2Rad;
            var endRad = endAngle * MathD.Deg2Rad;
            var points = ArcPoints(radius, startRad, endRad);
            if (points.Count <= 0) return;

            bool closed = points.Count > 0 && Vector2.Distance(points[0], points[points.Count - 1]) < 1e-2;

            _gui.Draw2D.DrawList.AddPolyline(points, closed ? points.Count - 1 : points.Count, stroke.Color, closed, (float)stroke.Thickness);
        }

        public void Circle(double radius, Stroke3D stroke) => Arc(radius, 0.0, 360, stroke);
        public void Quad(double radius, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = QuadPoints(radius * 2.0);
            if (points.Count <= 0) return;
            _gui.Draw2D.DrawList.AddPolyline(points, points.Count, stroke.Color, true, (float)stroke.Thickness);
        }

        public void FilledCircle(double radius, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = ArcPoints(radius, 0.0, Math.PI * 2);
            if (points.Count <= 0) return;
            _gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count - 1, stroke.Color);
        }

        public void LineSegment(Vector3 from, Vector3 to, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = new Vector2[2];

            for (int i = 0; i < 2; i++)
            {
                Vector3 point = i == 0 ? from : to;
                if (WorldToScreen(_viewport, _mvp, point, out Vector2 screenPos))
                    points[i] = screenPos;
                else
                    return;
            }

            _gui.Draw2D.DrawList.AddLine(points[0], points[1], stroke.Color, (float)stroke.Thickness);
        }

        public void Arrow(Vector3 from, Vector3 to, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            if (WorldToScreen(_viewport, _mvp, from, out Vector2 arrowStart) &&
                WorldToScreen(_viewport, _mvp, to, out Vector2 arrowEnd))
            {
                Vector2 direction = (arrowEnd - arrowStart).normalized;
                Vector2 cross = new Vector2(-direction.y, direction.x) * stroke.Thickness / 2.0f;

                List<Vector2> points =
                [
                    arrowStart - cross,
                    arrowStart + cross,
                    arrowEnd
                ];

                _gui.Draw2D.DrawList.AddConvexPolyFilled(points, 3, stroke.Color);
            }
        }

        public void Polygon(IEnumerable<Vector3> points, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var screenPoints = new List<Vector2>();
            foreach (Vector3 pos in points)
                if (WorldToScreen(_viewport, _mvp, pos, out Vector2 screenPos))
                    screenPoints.Add(screenPos);

            if (screenPoints.Count > 2)
                _gui.Draw2D.DrawList.AddConvexPolyFilled(screenPoints, screenPoints.Count, stroke.Color);
        }

        public void Polyline(IEnumerable<Vector3> points, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var screenPoints = new List<Vector2>();
            foreach (Vector3 pos in points)
                if (WorldToScreen(_viewport, _mvp, pos, out Vector2 screenPos))
                    screenPoints.Add(screenPos);

            if (screenPoints.Count > 1)
                _gui.Draw2D.DrawList.AddPolyline(screenPoints, screenPoints.Count, stroke.Color, false, (float)stroke.Thickness);
        }

        public void Sector(double radius, double startAngle, double endAngle, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var startRad = startAngle * MathD.Deg2Rad;
            var endRad = endAngle * MathD.Deg2Rad;

            double angleDelta = endRad - startRad;
            int stepCount = Steps(Math.Abs(angleDelta));

            if (stepCount < 2)
                return;

            var points = new List<Vector2>();

            double stepSize = angleDelta / (stepCount - 1);

            if (Math.Abs(Math.Abs(startRad - endRad) - Math.PI * 2) < Math.Abs(stepSize))
            {
                FilledCircle(radius, stroke);
                return;
            }

            WorldToScreen(Vector3.zero, out var center);
            points.Add(center);

            (double sinStep, double cosStep) = SinCos(stepSize);
            (double sinAngle, double cosAngle) = SinCos(startRad);

            for (int i = 0; i < stepCount; i++)
            {
                double x = cosAngle * radius;
                double y = sinAngle * radius;

                if (WorldToScreen(new Vector3((float)x, 0.0f, (float)y), out Vector2 pos))
                {
                    points.Add(pos);
                }

                double newSin = sinAngle * cosStep + cosAngle * sinStep;
                double newCos = cosAngle * cosStep - sinAngle * sinStep;

                sinAngle = newSin;
                cosAngle = newCos;
            }

            if (points.Count <= 0) return;

            _gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count, stroke.Color);
        }

        public void Sphere(double radius, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = new List<Vector2>();

            for (int i = 0; i < 64; i++)
            {
                double angle = MathD.Deg2Rad * 360.0 * i / 64.0;
                double x = Math.Cos(angle) * radius;
                double z = Math.Sin(angle) * radius;

                if (WorldToScreen(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
                    points.Add(pos);
            }

            if (points.Count <= 0) return;

            _gui.Draw2D.DrawList.AddPolyline(points, points.Count, stroke.Color, true, (float)stroke.Thickness);
        }

        public void FilledSphere(double radius, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = new List<Vector2>();

            for (int i = 0; i < 64; i++)
            {
                double angle = MathD.Deg2Rad * 360.0 * i / 64.0;
                double x = Math.Cos(angle) * radius;
                double z = Math.Sin(angle) * radius;

                if (WorldToScreen(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
                    points.Add(pos);
            }

            if (points.Count <= 0) return;

            _gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count, stroke.Color);
        }

        // Define the eight corners of the cube
        static readonly Vector3[] cubeCorners =
        [
            new Vector3(-0.5, -0.5, -0.5), // Bottom-back-left
                new Vector3(0.5,  -0.5, -0.5),  // Bottom-back-right
                new Vector3(0.5,  -0.5, 0.5),   // Bottom-front-right
                new Vector3(-0.5, -0.5, 0.5),  // Bottom-front-left
                new Vector3(-0.5, 0.5,  -0.5),  // Top-back-left
                new Vector3(0.5,  0.5,  -0.5),   // Top-back-right
                new Vector3(0.5,  0.5,  0.5),    // Top-front-right
                new Vector3(-0.5, 0.5,  0.5)    // Top-front-left
        ];

        // Define the cube edges
        int[] cubeEdges =
        [
            0, 1, 1, 2, 2, 3, 3, 0, // Bottom face
                4, 5, 5, 6, 6, 7, 7, 4, // Top face
                0, 4, 1, 5, 2, 6, 3, 7  // Vertical edges
        ];

        public void Cube(double size, Stroke3D stroke)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = new List<Vector2>();

            double halfSize = size / 2.0;

            // Convert to screen coordinates and add to the points buffer
            foreach (var corner in cubeCorners)
                if (WorldToScreen(corner, out Vector2 pos))
                    points.Add(pos);

            if (points.Count <= 0) return;

            // Draw the cube edges
            for (int i = 0; i < cubeEdges.Length; i += 2)
            {
                _gui.Draw2D.DrawList.AddLine(points[cubeEdges[i]], points[cubeEdges[i + 1]], stroke.Color, (float)stroke.Thickness);
            }
        }

        public void QuadImage(Vector3 size, Texture2D texture)
        {
            if (!hasViewport) throw new InvalidOperationException("No viewport set.");
            if (!hasMVP) throw new InvalidOperationException("No MVP set.");

            var points = QuadPoints(size.x);
            if (points.Count <= 0) return;

            var min = points[0];
            var max = points[0];

            min = Vector2.Min(min, points[1]);
            max = Vector2.Max(max, points[1]);

            min = Vector2.Min(min, points[2]);
            max = Vector2.Max(max, points[2]);

            min = Vector2.Min(min, points[3]);
            max = Vector2.Max(max, points[3]);

            _gui.Draw2D.DrawList.AddImage(texture, min, max);
        }


        private List<Vector2> ArcPoints(double radius, double startRad, double endRad)
        {
            double angle = Math.Clamp(endRad - startRad, -Math.PI * 2, Math.PI * 2);

            int stepCount = Steps(angle);
            var points = new List<Vector2>();

            double stepSize = angle / (stepCount - 1);

            for (int i = 0; i < stepCount; i++)
            {
                double step = stepSize * i;
                double x = Math.Cos(startRad + step) * radius;
                double z = Math.Sin(startRad + step) * radius;

                if (WorldToScreen(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
                {
                    points.Add(pos);
                }
            }

            return points;
        }

        private List<Vector2> QuadPoints(double size)
        {
            var points = new List<Vector2>();

            double halfSize = size / 2.0;

            // Define the four corners of the quad
            Vector3[] quadCorners =
            [
                new Vector3((float)-halfSize, 0.0f, (float)-halfSize), // Bottom-left
                new Vector3((float)halfSize, 0.0f, (float)-halfSize),  // Bottom-right
                new Vector3((float)halfSize, 0.0f, (float)halfSize),   // Top-right
                new Vector3((float)-halfSize, 0.0f, (float)halfSize)   // Top-left
            ];

            // Convert to screen coordinates and add to the points buffer
            foreach (var corner in quadCorners)
            {
                if (WorldToScreen(corner, out Vector2 pos))
                {
                    points.Add(pos);
                }
            }

            return points;
        }


        private static int Steps(double angle) => Math.Max(1, (int)Math.Ceiling(20.0 * Math.Abs(angle)));
        private static (double sin, double cos) SinCos(double angle) => (Math.Sin(angle), Math.Cos(angle));

        private bool WorldToScreen(Vector3 vec, out Vector2 pos) => WorldToScreen(_viewport, _mvp, vec, out pos);
        private static bool WorldToScreen(Rect viewport, Matrix4x4 mvp, Vector3 pos, out Vector2 screenPos)
        {
            var res = GizmoUtils.WorldToScreen(viewport, mvp, pos);
            if (res.HasValue)
            {
                screenPos = res.Value;
                return true;
            }
            else
            {
                screenPos = Vector2.zero;
                return false;
            }
        }
    }
}
