// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Picks surfaces to texture. Clicking a face selects it rather than its brush.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGSurfaceTool : SceneTool
{
    public override string Name => "CSG Surface";
    public override string Icon => EditorIcons.Brush;
    public override string? Tooltip => "Select and texture CSG surfaces";
    public override int Order => 2;

    private const string SurfaceControl = "csg_surface";
    private const string EmptyControl = "csg_surface_empty";

    private static readonly Color32 HoverColor = new(255, 240, 160, 60);
    private static readonly Color32 SelectedFill = new(255, 200, 90, 70);
    private static readonly Color32 SelectedEdge = new(255, 220, 120, 255);

    /// <summary>Whether surface picking is the CSG mode in force.</summary>
    public bool Active => CSGEditMode.Is(CSGMode.Surface);

    private CSGSurfaceRef _lastSmeared;

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override bool SuppressTransformGizmo => Active;

    public override void OnDeactivated() => CSGSurfaceSelection.Clear();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!Active) return;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        CSGSurfaceSelection.Prune();

        if (ctx.Handles.KeyDown(KeyCode.Escape))
        {
            if (CSGSurfaceSelection.Count == 0) CSGEditMode.Current = CSGMode.Object;
            CSGSurfaceSelection.Clear();
            return;
        }

        PickSurface(ctx, model!);
        DrawSelection(ctx);
    }

    /// <summary>Picks against the model's generated surfaces so carved out faces are selectable.</summary>
    private void PickSurface(SceneToolContext ctx, CSGModel model)
    {
        HandleContext handles = ctx.Handles;

        // A click on nothing clears the surfaces only, rather than falling through to scene picking
        ControlID empty = handles.GetControlID(EmptyControl);
        handles.AddControl(empty, HandleContext.BodyDistance, float.MaxValue - 1f);
        if (handles.IsNearest(empty) && handles.TryBeginDrag(empty))
        {
            if (!handles.Shift && !handles.Ctrl) CSGSurfaceSelection.Clear();
            return;
        }

        if (!model.Raycast(handles.MouseRay, out CSGBrush brush, out int plane, out Float3 point, b => !CSGVisibility.IsHidden(b))) return;
        if (plane < 0) return;

        var hit = new CSGSurfaceRef(brush, plane);
        if (!hit.IsValid) return;

        ControlID id = handles.GetControlID(SurfaceControl);
        handles.AddControl(id, HandleContext.BodyDistance, Float3.Distance(handles.MouseRay.Origin, point));
        handles.RequestCursor(id, PaperCursor.Pointer);

        DrawSurface(ctx, hit, HoverColor, default, 0f);

        // Holding the smear key paints the last picked surface's look onto whatever is under the cursor.
        if (handles.IsHeld("CSG/Smear"))
        {
            if (handles.PrimaryHeld && !hit.Equals(_lastSmeared))
            {
                _lastSmeared = hit;
                CSGSurfaceSelection.SmearOnto(hit);
            }
            return;
        }
        _lastSmeared = default;

        if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) return;

        if (handles.Shift) CSGSurfaceSelection.Add(hit);
        else if (handles.Ctrl) CSGSurfaceSelection.Toggle(hit);
        else CSGSurfaceSelection.Select(hit);
    }

    private static void DrawSelection(SceneToolContext ctx)
    {
        foreach (CSGSurfaceRef surface in CSGSurfaceSelection.Selected)
            DrawSurface(ctx, surface, SelectedFill, SelectedEdge, 2f);
    }

    private static void DrawSurface(SceneToolContext ctx, CSGSurfaceRef surface,
                                    Color32 fill, Color32 edge, float thickness)
    {
        List<Float3> corners = CSGSurfaceSelection.WorldCorners(surface);
        if (corners.Count < 3) return;

        CSGGizmos.Polygon(corners.ToArray(), fill);
        if (thickness > 0f) CSGGizmos.Polyline(corners.ToArray(), edge, thickness, closed: true);
    }
}
