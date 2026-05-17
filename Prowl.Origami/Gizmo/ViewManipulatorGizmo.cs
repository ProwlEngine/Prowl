// Based on: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

/// <summary>
/// Orientation cube gizmo drawn in the top-right corner of the scene view.
/// Click a face to snap the camera to that axis. Click the circle to toggle ortho/perspective.
/// </summary>
public class ViewManipulatorGizmo
{
    private Rect _rect;
    private Float3 _camForward;
    private Float3 _camUp;
    private bool _isHovering;

    public bool IsOver => _isHovering;

    public void SetRect(Rect rect) => _rect = rect;
    public void SetCamera(Float3 camForward, Float3 camUp) { _camForward = camForward; _camUp = camUp; }

    /// <summary>
    /// Update and draw the view manipulator. Returns true if a face was clicked.
    /// newCamForward is the direction to snap the camera to.
    /// </summary>
    public bool Update(Prowl.Quill.Canvas canvas, Float2 mousePos, bool mouseClicked, bool blockPicking,
        out Float3 newCamForward)
    {
        _isHovering = false;
        newCamForward = _camForward;

        float size = (float)_rect.Size.X;
        Float2 center = new Float2((float)(_rect.Min.X + _rect.Size.X / 2), (float)(_rect.Min.Y + _rect.Size.Y / 2));
        float radius = size / 2f;

        // Background circle
        canvas.CircleFilled((float)center.X, (float)center.Y, radius, Color32.FromArgb(128, 25, 25, 30), 48);

        // View-projection for the cube: look at origin from camera direction
        var view = Float4x4.CreateLookTo(Float3.Zero, _camForward, Float3.UnitY);
        var proj = Float4x4.CreateOrthoOffCenter(-2.25f, 2.25f, -2.25f, 2.25f, 0.1f, 100f);
        var vp = proj * view;

        var (hoveringCube, cubeFace) = DrawCube(canvas, vp, mousePos);

        if (hoveringCube && !blockPicking)
        {
            _isHovering = true;
            if (mouseClicked)
            {
                // Snap camera to face invert forward for Z faces
                if (cubeFace == Float3.UnitZ || cubeFace == -Float3.UnitZ)
                    cubeFace = -cubeFace;
                newCamForward = -cubeFace;
                return true;
            }
        }
        else
        {
            // Check if mouse is inside circle (for ortho toggle)
            float distToCenter = Float2.Length(mousePos - center);
            if (distToCenter < radius)
            {
                _isHovering = true;
                if (!blockPicking)
                {
                    // Subtle hover highlight
                    canvas.CircleFilled((float)center.X, (float)center.Y, radius, Color32.FromArgb(30, 255, 255, 255), 48);
                }
            }
        }

        return false;
    }

    private (bool hovering, Float3 axis) DrawCube(Prowl.Quill.Canvas canvas, Float4x4 vp, Float2 mousePos)
    {
        Float3[] cubeVerts =
        [
            new(-1, -1,  1), new( 1, -1,  1), new( 1,  1,  1), new(-1,  1,  1),
            new(-1, -1, -1), new( 1, -1, -1), new( 1,  1, -1), new(-1,  1, -1),
        ];

        int[][] faces = [ [0,1,2,3], [1,5,6,2], [5,4,7,6], [4,0,3,7], [3,2,6,7], [4,5,1,0] ];

        Color32[] faceColors =
        [
            Color32.FromArgb(255, 39, 117, 255),  // Front (Z+)
            Color32.FromArgb(255, 226, 55, 56),   // Right (X+)
            Color32.FromArgb(255, 39, 117, 255),  // Back (Z-)
            Color32.FromArgb(255, 226, 55, 56),   // Left (X-)
            Color32.FromArgb(255, 94, 234, 141),  // Top (Y+)
            Color32.FromArgb(255, 94, 234, 141),  // Bottom (Y-)
        ];

        Float3[] faceNormals =
        [
            Float3.UnitZ, Float3.UnitX, -Float3.UnitZ, -Float3.UnitX, Float3.UnitY, -Float3.UnitY,
        ];

        bool hovering = false;
        Float3 axis = Float3.Zero;

        for (int i = 0; i < faces.Length; i++)
        {
            var normal = faceNormals[i];
            float dot = Float3.Dot(normal, -_camForward);
            if (dot <= 0.01f) continue; // Back-face cull

            var screenPts = new List<Float2>();
            foreach (int vi in faces[i])
            {
                var sp = GizmoUtils.WorldToScreen(_rect, vp, cubeVerts[vi]);
                if (sp != null) screenPts.Add(sp.Value);
            }

            if (screenPts.Count < 3) continue;

            // Darken back-facing faces slightly
            byte alpha = (byte)(180 + (int)(dot * 75));
            var col = Color32.FromArgb(alpha, faceColors[i].R, faceColors[i].G, faceColors[i].B);

            // Draw filled quad
            canvas.BeginPath();
            canvas.MoveTo((float)screenPts[0].X, (float)screenPts[0].Y);
            for (int j = 1; j < screenPts.Count; j++)
                canvas.LineTo((float)screenPts[j].X, (float)screenPts[j].Y);
            canvas.ClosePath();
            canvas.SetFillColor(col);
            canvas.Fill();

            // Hover test
            if (GizmoUtils.IsPointInPolygon(mousePos, screenPts))
            {
                canvas.SetFillColor(Color32.FromArgb(60, 255, 255, 255));
                canvas.Fill();
                hovering = true;
                axis = normal;
            }
        }

        return (hovering, axis);
    }
}
