using Prowl.Runtime.GUI.Graphics;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GUI
{

    // Based on ShapeBuilder from: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

    public struct Stroke
    {
        public double Thickness;
        public Color32 Color;
        public bool AntiAliased;
    }

    public partial class Gui
    {
        private GuiShapeBuilder _draw3D;
        public GuiShapeBuilder Draw3D => _draw3D ??= new GuiShapeBuilder(this);

        //TODO: Target API
        //public void test()
        //{
        //    using (Draw3D.Viewport(Rect.Zero))
        //    {
        //        using (Draw3D.Matrix(Matrix4x4.Identity))
        //        {
        //
        //        }
        //
        //        using (Draw3D.Matrix(Matrix4x4.Identity))
        //        {
        //
        //        }
        //
        //        using (Draw3D.Matrix(Matrix4x4.Identity))
        //        {
        //
        //        }
        //    }
        //}

    }

    public class GuiShapeBuilder(Gui g)
    {
        private Matrix4x4 mvp;
        private Rect viewport;
        private readonly Gui gui = g;

        public void Setup3DObject(Matrix4x4 mvp, Rect viewport)
        {
            this.mvp = mvp;
            this.viewport = viewport;
        }

        public void Arc(double radius, double startAngle, double endAngle, Stroke stroke)
        {
            var startRad = startAngle * Mathf.Deg2Rad;
            var endRad = endAngle * Mathf.Deg2Rad;
            var points = ArcPoints(radius, startRad, endRad);
            if (points.Count <= 0) return;

            bool closed = points.Count > 0 && Vector2.Distance(points[0], points[points.Count - 1]) < 1e-2;

            gui.Draw2D.DrawList.AddPolyline(points, closed ? points.Count - 1 : points.Count, stroke.Color.GetUInt(), closed, (float)stroke.Thickness, stroke.AntiAliased);
        }

        public void Circle(double radius, Stroke stroke)
        {
            Arc(radius, 0.0, 360, stroke);
        }

        public void FilledCircle(double radius, Stroke stroke)
        {
            var points = ArcPoints(radius, 0.0, Math.PI * 2);
            if (points.Count <= 0) return;
            gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count - 1, stroke.Color.GetUInt(), stroke.AntiAliased);
        }

        public void LineSegment(Vector3 from, Vector3 to, Stroke stroke)
        {
            var points = new Vector2[2];

            for (int i = 0; i < 2; i++)
            {
                Vector3 point = i == 0 ? from : to;
                if (WorldToScreen(viewport, mvp, point, out Vector2 screenPos))
                    points[i] = screenPos;
                else
                    return;
            }

            gui.Draw2D.DrawList.AddLine(points[0], points[1], stroke.Color.GetUInt(), (float)stroke.Thickness);
        }

        public void Arrow(Vector3 from, Vector3 to, Stroke stroke)
        {
            if (WorldToScreen(viewport, mvp, from, out Vector2 arrowStart) &&
                WorldToScreen(viewport, mvp, to, out Vector2 arrowEnd))
            {
                Vector2 direction = (arrowEnd - arrowStart).normalized;
                Vector2 cross = new Vector2(-direction.y, direction.x) * stroke.Thickness / 2.0f;

                UIBuffer<Vector2> points = new UIBuffer<Vector2>();
                points.reserve(3);
                points.Add(arrowStart - cross);
                points.Add(arrowStart + cross);
                points.Add(arrowEnd);

                gui.Draw2D.DrawList.AddConvexPolyFilled(points, 3, stroke.Color.GetUInt(), stroke.AntiAliased);
            }
        }

        public void Polygon(IEnumerable<Vector3> points, Stroke stroke)
        {
            var screenPoints = new UIBuffer<Vector2>();
            foreach (Vector3 pos in points)
                if (WorldToScreen(viewport, mvp, pos, out Vector2 screenPos))
                    screenPoints.Add(screenPos);

            if (screenPoints.Count > 2)
                gui.Draw2D.DrawList.AddConvexPolyFilled(screenPoints, screenPoints.Count, stroke.Color.GetUInt(), stroke.AntiAliased);
        }

        public void Polyline(IEnumerable<Vector3> points, Stroke stroke)
        {
            var screenPoints = new UIBuffer<Vector2>();
            foreach (Vector3 pos in points)
                if (WorldToScreen(viewport, mvp, pos, out Vector2 screenPos))
                    screenPoints.Add(screenPos);

            if (screenPoints.Count > 1)
                gui.Draw2D.DrawList.AddPolyline(screenPoints, screenPoints.Count, stroke.Color.GetUInt(), false, (float)stroke.Thickness, stroke.AntiAliased);
        }

        public void Sector(double radius, double startAngle, double endAngle, Stroke stroke)
        {
            var startRad = startAngle * Mathf.Deg2Rad;
            var endRad = endAngle * Mathf.Deg2Rad;

            double angleDelta = endRad - startRad;
            int stepCount = Steps(Math.Abs(angleDelta));

            if (stepCount < 2)
                return;

            var points = new UIBuffer<Vector2>();
            points.reserve(stepCount + 1);

            double stepSize = angleDelta / (stepCount - 1);

            if (Math.Abs(Math.Abs(startRad - endRad) - Math.PI * 2) < Math.Abs(stepSize))
            {
                FilledCircle(radius, stroke);
                return;
            }

            Vec3ToPos2(Vector3.zero, out var center);
            points.Add(center);

            (double sinStep, double cosStep) = SinCos(stepSize);
            (double sinAngle, double cosAngle) = SinCos(startRad);

            for (int i = 0; i < stepCount; i++)
            {
                double x = cosAngle * radius;
                double y = sinAngle * radius;

                if (Vec3ToPos2(new Vector3((float)x, 0.0f, (float)y), out Vector2 pos))
                {
                    points.Add(pos);
                }

                double newSin = sinAngle * cosStep + cosAngle * sinStep;
                double newCos = cosAngle * cosStep - sinAngle * sinStep;

                sinAngle = newSin;
                cosAngle = newCos;
            }

            if (points.Count <= 0) return;

            gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count, stroke.Color.GetUInt(), stroke.AntiAliased);
        }

        private UIBuffer<Vector2> ArcPoints(double radius, double startRad, double endRad)
        {
            double angle = Math.Clamp(endRad - startRad, -Math.PI * 2, Math.PI * 2);

            int stepCount = Steps(angle);
            var points = new UIBuffer<Vector2>();
            points.reserve(stepCount);

            double stepSize = angle / (stepCount - 1);

            for (int i = 0; i < stepCount; i++)
            {
                double step = stepSize * i;
                double x = Math.Cos(startRad + step) * radius;
                double z = Math.Sin(startRad + step) * radius;

                if (Vec3ToPos2(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
                {
                    points.Add(pos);
                }
            }

            return points;
        }

        private bool Vec3ToPos2(Vector3 vec, out Vector2 pos)
        {
            return WorldToScreen(viewport, mvp, vec, out pos);
        }

        private static int Steps(double angle)
        {
            return Math.Max(1, (int)Math.Ceiling(20.0 * Math.Abs(angle)));
        }

        private static (double sin, double cos) SinCos(double angle)
        {
            return (Math.Sin(angle), Math.Cos(angle));
        }

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
