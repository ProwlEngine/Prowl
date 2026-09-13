// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Stamps an area over a world region, independent of which objects the geometry inside it actually
/// came from. Read directly by <see cref="NavMeshGeometryCollector"/> at collection time, after
/// ordinary geometry has already been gathered - a volume only re-marks surface that geometry
/// produced, it never creates walkable surface of its own, so an empty region gains nothing from
/// being inside one.
/// </summary>
[AddComponentMenu("Navigation/NavMesh Modifier Volume")]
public sealed class NavMeshModifierVolume : MonoBehaviour
{
    /// <summary>Center of the box, local to this GameObject's transform.</summary>
    public Float3 Center;

    /// <summary>Size of the box, local to this GameObject's transform (rotation and scale are honoured).</summary>
    public Float3 Size = Float3.One;

    /// <summary>The area (see <see cref="NavMeshAreas"/>) stamped on any baked geometry inside this
    /// volume - <see cref="NavMeshGeometryCollector"/> converts it directly into a Recast convex-volume
    /// area stamp at bake time (see <see cref="NavMeshAreaVolumeInput"/>), not a per-triangle tag.
    /// <see cref="NavMeshAreas.NotWalkable"/> punches a real hole.</summary>
    public int Area = NavMeshAreas.NotWalkable;

    /// <summary>When true, this volume applies to every agent type's bake. When false, only the types
    /// listed in <see cref="AgentTypeIds"/>.</summary>
    public bool AffectAllAgentTypes = true;

    /// <summary>Agent types this volume affects when <see cref="AffectAllAgentTypes"/> is false.</summary>
    public List<int> AgentTypeIds = [];

    /// <summary>Whether this volume applies to a bake for the given agent type.</summary>
    public bool AppliesTo(int agentTypeId) => AffectAllAgentTypes || AgentTypeIds.Contains(agentTypeId);

    /// <summary>Whether a world-space point falls inside this volume's oriented box.</summary>
    public bool Contains(Float3 worldPoint)
    {
        Float3 local = Transform.InverseTransformPoint(worldPoint) - Center;
        Float3 half = Size * 0.5f;
        return Maths.Abs(local.X) <= half.X && Maths.Abs(local.Y) <= half.Y && Maths.Abs(local.Z) <= half.Z;
    }

    /// <summary>Draws this volume's oriented box outline.</summary>
    public override void DrawGizmos()
    {
        Debug.PushMatrix(Float4x4.CreateTRS(Transform.Position, Transform.Rotation, Transform.LossyScale));
        Debug.DrawWireCube(Center, Size * 0.5f, new Color(1f, 0.8f, 0.2f, 1f));
        Debug.PopMatrix();
    }
}
