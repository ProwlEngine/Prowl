// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

/*
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.PaperUI.GUI;

// Based on ShapeBuilder from: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

public struct Stroke3D
{
    public float Thickness;
    public Color Color;
}

public class GuiDraw3D(Paper gui)
{
    public class GUI3DViewScope : IDisposable
    {
        public GUI3DViewScope(Rect viewport) => Paper.ActiveGUI.Draw3D._viewports.Push(viewport);
        public void Dispose() => Paper.ActiveGUI.Draw3D._viewports.Pop();
    }

    public class GUI3DMVPScope : IDisposable
    {
        public GUI3DMVPScope(Matrix4x4 mvp) => Paper.ActiveGUI.Draw3D._mvps.Push(mvp);
        public void Dispose() => Paper.ActiveGUI.Draw3D._mvps.Pop();
    }

    private readonly Paper _gui = gui;

    private readonly Stack<Rect> _viewports = new Stack<Rect>();
    private Rect _viewport => _viewports.Peek();

    private readonly Stack<Matrix4x4> _mvps = new Stack<Matrix4x4>();
    private Matrix4x4 _mvp => _mvps.Peek();

    private bool hasViewport => _viewports.Count > 0;
    private bool hasMVP => _mvps.Count > 0;

    public GUI3DViewScope Viewport(Rect viewport) => new GUI3DViewScope(viewport);
    public GUI3DMVPScope Matrix(Matrix4x4 mvp) => new GUI3DMVPScope(mvp);

    public void Arc(float radius, float startAngle, float endAngle, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var startRad = startAngle * MathUtilities.Deg2Rad;
        var endRad = endAngle * MathUtilities.Deg2Rad;
        var points = ArcPoints(radius, startRad, endRad);
        if (points.Count <= 0) return;

        bool closed = points.Count > 0 && Vector2.Distance(points[0], points[points.Count - 1]) < 1e-2;

        _gui.Draw2D.DrawList.AddPolyline(points, closed ? points.Count - 1 : points.Count, stroke.Color, closed, (float)stroke.Thickness);
    }

    public void Circle(float radius, Stroke3D stroke) => Arc(radius, 0.0f, 360, stroke);
    public void Quad(float radius, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var points = QuadPoints(radius * 2.0f);
        if (points.Count <= 0) return;
        _gui.Draw2D.DrawList.AddPolyline(points, points.Count, stroke.Color, true, (float)stroke.Thickness);
    }

    public void FilledCircle(float radius, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var points = ArcPoints(radius, 0.0f, MathF.PI * 2f);
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
            Vector2 direction = Vector2.Normalize(arrowEnd - arrowStart);
            Vector2 cross = new Vector2(-direction.Y, direction.X) * stroke.Thickness / 2.0f;

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

    public void Sector(float radius, float startAngle, float endAngle, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var startRad = startAngle * MathUtilities.Deg2Rad;
        var endRad = endAngle * MathUtilities.Deg2Rad;

        float angleDelta = endRad - startRad;
        int stepCount = Steps(Math.Abs(angleDelta));

        if (stepCount < 2)
            return;

        var points = new List<Vector2>();

        float stepSize = angleDelta / (stepCount - 1);

        if (Math.Abs(Math.Abs(startRad - endRad) - Math.PI * 2) < Math.Abs(stepSize))
        {
            FilledCircle(radius, stroke);
            return;
        }

        WorldToScreen(Vector3.Zero, out var center);
        points.Add(center);

        (float sinStep, float cosStep) = SinCos(stepSize);
        (float sinAngle, float cosAngle) = SinCos(startRad);

        for (int i = 0; i < stepCount; i++)
        {
            float x = cosAngle * radius;
            float y = sinAngle * radius;

            if (WorldToScreen(new Vector3((float)x, 0.0f, (float)y), out Vector2 pos))
            {
                points.Add(pos);
            }

            float newSin = sinAngle * cosStep + cosAngle * sinStep;
            float newCos = cosAngle * cosStep - sinAngle * sinStep;

            sinAngle = newSin;
            cosAngle = newCos;
        }

        if (points.Count <= 0) return;

        _gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count, stroke.Color);
    }

    public void Sphere(float radius, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var points = new List<Vector2>();

        for (int i = 0; i < 64; i++)
        {
            float angle = MathUtilities.Deg2Rad * 360.0f * i / 64.0f;
            float x = MathF.Cos(angle) * radius;
            float z = MathF.Sin(angle) * radius;

            if (WorldToScreen(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
                points.Add(pos);
        }

        if (points.Count <= 0) return;

        _gui.Draw2D.DrawList.AddPolyline(points, points.Count, stroke.Color, true, (float)stroke.Thickness);
    }

    public void FilledSphere(float radius, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var points = new List<Vector2>();

        for (int i = 0; i < 64; i++)
        {
            float angle = MathUtilities.Deg2Rad * 360.0f * i / 64.0f;
            float x = MathF.Cos(angle) * radius;
            float z = MathF.Sin(angle) * radius;

            if (WorldToScreen(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
                points.Add(pos);
        }

        if (points.Count <= 0) return;

        _gui.Draw2D.DrawList.AddConvexPolyFilled(points, points.Count, stroke.Color);
    }

    // Define the eight corners of the cube
    static readonly Vector3[] cubeCorners =
    [
        new Vector3(-0.5f, -0.5f, -0.5f), // Bottom-back-left
        new Vector3(0.5f,  -0.5f, -0.5f), // Bottom-back-right
        new Vector3(0.5f,  -0.5f, 0.5f),  // Bottom-front-right
        new Vector3(-0.5f, -0.5f, 0.5f),  // Bottom-front-left
        new Vector3(-0.5f, 0.5f,  -0.5f), // Top-back-left
        new Vector3(0.5f,  0.5f,  -0.5f), // Top-back-right
        new Vector3(0.5f,  0.5f,  0.5f),  // Top-front-right
        new Vector3(-0.5f, 0.5f,  0.5f)   // Top-front-left
    ];

    // Define the cube edges
    readonly int[] cubeEdges =
    [
        0, 1, 1, 2, 2, 3, 3, 0, // Bottom face
        4, 5, 5, 6, 6, 7, 7, 4, // Top face
        0, 4, 1, 5, 2, 6, 3, 7  // Vertical edges
    ];

    public void Cube(float size, Stroke3D stroke)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var points = new List<Vector2>();

        float halfSize = size / 2.0f;

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

    public void QuadImage(Vector3 size, uint texture)
    {
        if (!hasViewport) throw new InvalidOperationException("No viewport set.");
        if (!hasMVP) throw new InvalidOperationException("No MVP set.");

        var points = QuadPoints(size.X);
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


    private List<Vector2> ArcPoints(float radius, float startRad, float endRad)
    {
        float angle = (float)Math.Clamp(endRad - startRad, -Math.PI * 2f, Math.PI * 2f);

        int stepCount = Steps(angle);
        var points = new List<Vector2>();

        float stepSize = angle / (stepCount - 1);

        for (int i = 0; i < stepCount; i++)
        {
            float step = stepSize * i;
            float x = MathF.Cos(startRad + step) * radius;
            float z = MathF.Sin(startRad + step) * radius;

            if (WorldToScreen(new Vector3((float)x, 0.0f, (float)z), out Vector2 pos))
            {
                points.Add(pos);
            }
        }

        return points;
    }

    private List<Vector2> QuadPoints(float size)
    {
        var points = new List<Vector2>();

        float halfSize = size / 2.0f;

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


    private static int Steps(float angle) => Math.Max(1, (int)Math.Ceiling(20.0 * Math.Abs(angle)));
    private static (float sin, float cos) SinCos(float angle) => (MathF.Sin(angle), MathF.Cos(angle));

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
            screenPos = Vector2.Zero;
            return false;
        }
    }
}

*/