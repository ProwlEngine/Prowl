﻿using Prowl.Runtime.GUI.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.GUI.Widgets.Gizmo
{
    public class ViewManipulatorGizmo
    {

        private Gui _gui;
        private Rect _gizmoRect;

        private Matrix4x4 _view;
        private Vector3 camForward;
        private Vector3 camUp;
        private bool _orthographic;

        private bool isHovering = false;

        public bool IsOver => isHovering;

        public ViewManipulatorGizmo(Gui gui)
        {
            _gui = gui;
        }

        public void SetRect(Rect rect)
        {
            _gizmoRect = rect;
        }

        public void SetCamera(Vector3 camForward, Vector3 camUp, bool orthographic)
        {
            this.camForward = camForward;
            this.camUp = camUp;
            _orthographic = orthographic;
        }

        public bool Update(out Vector3 newCamForward, out bool isOrthographic)
        {
            isHovering = false;
            newCamForward = camForward;
            isOrthographic = _orthographic;
            if (_gui == null) return false;
            
            var width = _gizmoRect.width;
            var height = _gizmoRect.height;
            using (_gui.Node("ViewManipulator").TopLeft(Offset.Percentage(1f, -width - 5), 5).Scale(width, height).Enter())
            {
                // Draw Circle
                var rect = _gui.CurrentNode.LayoutData.Rect;
                _gui.DrawCircleFilled(rect.Center, (float)rect.width / 2, new Color(0.1f, 0.1f, 0.1f, 0.5f), 48);

                // Create a View matrix looking at 0,0,0
                Matrix4x4 view = Matrix4x4.CreateLookToLeftHanded(Vector3.zero, camForward, Vector3.up);
                //Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(Mathf.PI / 4, 1, 0.1f, 1000f);
                Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(-2.25, 2.25, -2.25, 2.25, 0.1f, 1000f);

                var viewProjection = view * projection;

                (bool hoveringCube, Vector3 cubeFace) = DrawCube(rect, viewProjection);

                if (hoveringCube)
                {
                    isHovering = true;
                    if (_gui.IsPointerClick(Silk.NET.Input.MouseButton.Left))
                    {
                        // Rotate Camera
                        // FIXME: Why is forward vector inverted?
                        if (cubeFace == Vector3.forward || cubeFace == Vector3.backward)
                        {
                            cubeFace = -cubeFace;
                        }

                        newCamForward = -cubeFace;
                        return true;
                    }
                }
                else
                {
                    // No hovering cube, maybe hovering circle?

                    // If mouse inside circle
                    var mouse = _gui.PointerPos;
                    if (Vector2.Distance(mouse, rect.Center) < rect.width / 2)
                    {
                        // TODO: Switching between perspective and orthographic, Its super janky right now, not really usable

                        isHovering = true;
                        //var hovCol = GuiStyle.Base11;
                        //hovCol.a = 0.25f;
                        //_gui.DrawCircleFilled(rect.Center, (float)rect.width / 2, hovCol, 48);
                        //
                        //if (_gui.IsPointerClick(Silk.NET.Input.MouseButton.Left))
                        //{
                        //    // Toggle Perspective / Orthographic
                        //    isOrthographic = !isOrthographic;
                        //    return true;
                        //}
                    }
                }
            }

            return false;
        }

        private (bool, Vector3) DrawCube(Rect rect, Matrix4x4 viewProjection)
        {
            Vector3[] cubeVertices =
            [
                new Vector3(-1f, -1f, 1f),  // 0
                new Vector3(1f, -1f, 1f),   // 1
                new Vector3(1f, 1f, 1f),    // 2
                new Vector3(-1f, 1f, 1f),   // 3
                new Vector3(-1f, -1f, -1f), // 4
                new Vector3(1f, -1f, -1f),  // 5
                new Vector3(1f, 1f, -1f),   // 6
                new Vector3(-1f, 1f, -1f)   // 7
            ];

            int[][] cubeFaces =
            [
                [0, 1, 2, 3], // Front face
                [1, 5, 6, 2], // Right face
                [5, 4, 7, 6], // Back face
                [4, 0, 3, 7], // Left face
                [3, 2, 6, 7], // Top face
                [4, 5, 1, 0]  // Bottom face
            ];

            Color32[] faceColors =
            [
                GuiStyle.Blue,   // Front face
                GuiStyle.Red,    // Right face
                GuiStyle.Blue, // Back face
                GuiStyle.Red,  // Left face
                GuiStyle.Emerald,   // Top face
                GuiStyle.Emerald // Bottom face
            ];

            Vector3[] faceNormals =
            [
                Vector3.forward,
                Vector3.right,
                Vector3.backward,
                Vector3.left,
                Vector3.up,
                Vector3.down,
            ];

            bool hovering = false;
            Vector3 axis = Vector3.zero;
            for (int i = 0; i < cubeFaces.Length; i++)
            {
                int[] face = cubeFaces[i];
                Vector3 faceNormal = faceNormals[i].normalized;

                double dotProduct = Vector3.Dot(faceNormal, -camForward);
                if (dotProduct > 0.01)
                {
                    UIBuffer<Vector2> screenPoints = new UIBuffer<Vector2>();
                    for (int j = 0; j < face.Length; j++)
                    {
                        Vector3 vertex = cubeVertices[face[j]];
                        Vector2? screenPoint = GizmoUtils.WorldToScreen(rect, viewProjection, vertex);
                        if (screenPoint != null)
                        {
                            screenPoints.Add(screenPoint.Value);
                        }
                    }

                    if (screenPoints.Count >= 3)
                    {
                        _gui.DrawList.AddConvexPolyFilled(screenPoints, screenPoints.Count, faceColors[i].GetUInt(), true);

                        // If mouse inside convex poly
                        var mouse = _gui.PointerPos;
                        if (GizmoUtils.IsPointInPolygon(mouse, screenPoints))
                        {
                            var hovCol = GuiStyle.Base11;
                            hovCol.a = 0.25f;
                            _gui.DrawList.AddConvexPolyFilled(screenPoints, screenPoints.Count, ((Color32)hovCol).GetUInt(), true);
                            //_gui.DrawList.AddPolyline(screenPoints, screenPoints.Count, hovCol.GetUInt(), true, 1, true);
                            hovering = true;
                            axis = faceNormal;
                        }
                    }
                }
            }

            return (hovering, axis);
        }

    }
}
