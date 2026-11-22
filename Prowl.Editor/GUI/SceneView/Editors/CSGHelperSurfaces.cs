// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Which surfaces the viewport draws as helpers rather than leaving invisible.</summary>
[Flags]
public enum CSGHelperFlags
{
    None = 0,
    /// <summary>Surfaces that render normally. Only useful alongside the others.</summary>
    Visible = 1 << 0,
    /// <summary>Surfaces that collide but do not render.</summary>
    CollisionOnly = 1 << 1,
    /// <summary>Surfaces that neither render nor collide, and so produce nothing at all.</summary>
    Inert = 1 << 2,
    /// <summary>Face pieces the operations removed, which also makes them clickable.</summary>
    Discarded = 1 << 3,
}

/// <summary>Draws surfaces that would otherwise be invisible as overlay polygons, such as collision only or fully disabled faces.</summary>
public static class CSGHelperSurfaces
{
    private static readonly Color32 CollisionColor = new(120, 220, 255, 70);
    private static readonly Color32 InertColor = new(255, 90, 90, 70);
    private static readonly Color32 VisibleColor = new(160, 255, 160, 40);
    private static readonly Color32 DiscardedColor = new(200, 140, 255, 60);

    /// <summary>Which kinds are drawn.</summary>
    public static CSGHelperFlags Flags { get; set; } = CSGHelperFlags.None;

    public static void Set(CSGHelperFlags flag, bool on) => Flags = on ? Flags | flag : Flags & ~flag;

    /// <summary>Whether unselected subtractive and intersecting brushes are outlined. Selected ones always are.</summary>
    public static bool SubtractiveOutlines { get; set; } = true;
    public static bool IntersectOutlines { get; set; } = true;

    /// <summary>Draw every brush face whose flags put it in one of the enabled categories.</summary>
    public static void Draw(SceneToolContext ctx, CSGModel model)
    {
        model.KeepDiscarded = Flags.HasFlag(CSGHelperFlags.Discarded);
        if (Flags == CSGHelperFlags.None) return;

        if (model.KeepDiscarded)
        {
            foreach (CSGModelFace face in model.DiscardedFaces())
            {
                if (CSGVisibility.IsHidden(face.Brush)) continue;
                CSGGizmos.Polygon(face.Corners, DiscardedColor);
                CSGGizmos.Polyline(face.Corners, Opaque(DiscardedColor), 1f, closed: true);
            }
        }

        foreach (GameObject go in model.GameObject.GetChildrenDeep())
        {
            CSGBrush brush = go.GetComponent<CSGBrush>();
            if (brush.IsNotValid() || !brush.EnabledInHierarchy || !brush.IsShapeValid) continue;
            if (CSGVisibility.IsHidden(brush)) continue;

            DrawBrush(ctx, brush, model);
        }
    }

    private static void DrawBrush(SceneToolContext ctx, CSGBrush brush, CSGModel model)
    {
        SurfaceData[] surfaces = brush.Surfaces;
        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;

        for (int plane = 0; plane < surfaces.Length; plane++)
        {
            CSGHelperFlags kind = Classify(brush, surfaces[plane], model);
            if (kind == CSGHelperFlags.None || !Flags.HasFlag(kind)) continue;

            List<Float3> ring = brush.FaceCorners(plane);
            if (ring.Count < 3) continue;

            for (int i = 0; i < ring.Count; i++) ring[i] = Float4x4.TransformPoint(ring[i], toWorld);

            Float3[] corners = ring.ToArray();
            Color32 colour = ColourFor(kind);

            CSGGizmos.Polygon(corners, colour);
            CSGGizmos.Polyline(corners, Opaque(colour), 1f, closed: true);
        }
    }

    /// <summary>Combines model, brush and surface flags the same way the mesh builder does.</summary>
    private static CSGHelperFlags Classify(CSGBrush brush, SurfaceData surface, CSGModel model)
    {
        bool render = !model.Flags.HasFlag(CSGModelFlags.DoNotRender)
                   && !brush.Flags.HasFlag(CSGBrushFlags.NoRender)
                   && !surface.Flags.HasFlag(CSGSurfaceFlags.NoRender);

        bool collide = !model.Flags.HasFlag(CSGModelFlags.NoCollider)
                    && !brush.Flags.HasFlag(CSGBrushFlags.NoCollision)
                    && !surface.Flags.HasFlag(CSGSurfaceFlags.NoCollision);

        if (render) return CSGHelperFlags.Visible;
        return collide ? CSGHelperFlags.CollisionOnly : CSGHelperFlags.Inert;
    }

    private static Color32 ColourFor(CSGHelperFlags kind) => kind switch
    {
        CSGHelperFlags.CollisionOnly => CollisionColor,
        CSGHelperFlags.Inert => InertColor,

        _ => VisibleColor,
    };

    private static Color32 Opaque(Color32 c) => new(c.R, c.G, c.B, 220);
}
