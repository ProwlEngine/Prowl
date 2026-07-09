// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A set of light-probe sample points (local space). The lightmap bake samples baked SH at every
/// group's world positions, tetrahedralizes them, and stores the result on the scene; dynamic
/// objects then read interpolated SH from <c>Scene.ProbeVolume</c>.
/// </summary>
[AddComponentMenu("Rendering/Light Probe Group")]
[ComponentIcon("")] // Lightbulb
public class LightProbeGroup : MonoBehaviour
{
    /// <summary>Probe positions in this object's local space.</summary>
    public List<Float3> ProbePositions = new();

    /// <summary>Probe world positions (local positions transformed by this object's matrix).</summary>
    public IEnumerable<Float3> GetWorldPositions()
    {
        var m = Transform.LocalToWorldMatrix;
        foreach (var p in ProbePositions)
            yield return Float4x4.TransformPoint(p, m);
    }

    public override void DrawGizmos()
    {
        var m = Transform.LocalToWorldMatrix;
        foreach (var p in ProbePositions)
            Debug.DrawWireSphere(Float4x4.TransformPoint(p, m), 0.1f, new Color(1f, 0.82f, 0.2f, 0.7f), 6);
    }

    public override void DrawGizmosSelected()
    {
        var m = Transform.LocalToWorldMatrix;
        foreach (var p in ProbePositions)
            Debug.DrawWireSphere(Float4x4.TransformPoint(p, m), 0.12f, new Color(1f, 0.95f, 0.4f, 1f), 8);
    }

    /// <summary>Replace the probes with a regular 3D grid spanning a local-space box (inclusive of both ends).</summary>
    public void GenerateGrid(Float3 min, Float3 max, int countX, int countY, int countZ)
    {
        ProbePositions.Clear();
        countX = System.Math.Max(1, countX);
        countY = System.Math.Max(1, countY);
        countZ = System.Math.Max(1, countZ);
        for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
                for (int x = 0; x < countX; x++)
                {
                    float fx = countX == 1 ? 0.5f : x / (float)(countX - 1);
                    float fy = countY == 1 ? 0.5f : y / (float)(countY - 1);
                    float fz = countZ == 1 ? 0.5f : z / (float)(countZ - 1);
                    ProbePositions.Add(new Float3(
                        min.X + (max.X - min.X) * fx,
                        min.Y + (max.Y - min.Y) * fy,
                        min.Z + (max.Z - min.Z) * fz));
                }
    }
}
