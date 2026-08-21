// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Stamps an area over a world region during navmesh bakes, independent of which objects the
/// geometry came from — mark a danger zone, make a doorway expensive, or (with Not Walkable)
/// erase walkability inside the box. Mirrors Unity's NavMeshModifierVolume. The volume only
/// re-marks surface that geometry produced; it never creates walkable surface. Applied during
/// full bakes AND partial rebuilds whose tiles intersect it; like modifiers, changing a
/// volume does not rebake anything by itself. When toggling or moving a volume at runtime,
/// rebuild its ENTIRE footprint region — rebuilding a sub-region leaves the stamp
/// half-applied in the untouched tiles.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Modifier Volume")]
[ComponentIcon("")] // cube icon
public class NavMeshModifierVolume : MonoBehaviour
{
    [Tooltip("Volume center, local to this GameObject.")]
    public Float3 Center;

    [Tooltip("Volume size, local to this GameObject (scaled and rotated by the Transform).")]
    public Float3 Size = new(4, 3, 4);

    [Tooltip("The area stamped inside the volume. Not Walkable erases walkability (punches a hole).")]
    [NavMeshArea]
    public int Area = NavMeshAreas.Walkable;

    [Tooltip("Apply to bakes of every agent type. Turn off to pick specific types.")]
    public bool AffectAllAgentTypes = true;

    [Tooltip("Agent types whose bakes this volume affects, when not affecting all.")]
    [NavMeshAgentType]
    [EnableIf(nameof(UsesExplicitAgentTypes))]
    public List<int> AffectedAgentTypeIds = [];

    private bool UsesExplicitAgentTypes => !AffectAllAgentTypes;

    /// <summary>Does this volume apply to bakes for the given agent type?</summary>
    public bool AffectsAgentType(int agentTypeId)
        => AffectAllAgentTypes || AffectedAgentTypeIds.Contains(agentTypeId);

    /// <summary>The world-space convex prism this volume marks (rotation and scale applied).</summary>
    public NavMeshAreaVolume ComputeAreaVolume()
        => NavMeshAreaVolume.FromOrientedBox(Transform.LocalToWorldMatrix, Center, Size, Area);

    public override void DrawGizmosSelected()
    {
        // Wire box in the same per-area colour the scene-view navmesh overlay uses, drawn
        // under the full transform so the gizmo shows the same rotated/scaled region the bake
        // marks (same idiom as BoxCollider.DrawGizmos).
        Color c = NavMeshSurface.AreaColor(Area);
        Debug.PushMatrix(Transform.LocalToWorldMatrix);
        Debug.DrawWireCube(Center, Size * 0.5f, new Color(c.R, c.G, c.B, 1f));
        Debug.PopMatrix();
    }
}
