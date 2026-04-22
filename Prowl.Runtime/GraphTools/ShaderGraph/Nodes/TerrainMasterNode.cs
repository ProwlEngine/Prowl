// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Master output for the <c>Terrain</c> shader type. Heightmap-displaced quadtree
/// chunk rendering; fragment evaluates a lit surface with whatever the graph
/// produces for Albedo / Normal / Roughness / etc. The vertex stage is hardcoded
/// by <c>TerrainPass</c> to sample <c>_Heightmap</c>, displace the vertex, and
/// compute a central-differences world-space normal from the height field.
/// </summary>
/// <remarks>
/// Splatmap-weighted multi-layer blending isn't baked into the master — users
/// wire it explicitly using the <c>Splatmap Weights</c> node plus plain math
/// nodes. That keeps the master simple and the blending logic fully overridable.
/// </remarks>
public sealed class TerrainMasterNode : MasterNodeBase
{
    public override string Title => "Terrain Output";

    /// <summary>Lighting model. Terrain is almost always PBR; Lambert exists for
    /// stylized looks that don't want specular.</summary>
    public ShaderLightingMode Lighting = ShaderLightingMode.PBR;

    protected override void DefineNode()
    {
        // Fragment surface inputs — same shape as SurfaceMasterNode since terrain
        // is fundamentally a lit surface. Defaults match StandardSurface.glsl.
        AddInput<Color>("Albedo",       new Color(1f, 1f, 1f, 1f));
        AddInput<Float3>("Normal",      new Float3(0, 0, 1),  // tangent-space passthrough
            tooltip: "Tangent-space normal. Defaults to (0,0,1). TBN uses the heightmap-derived world normal.");
        AddInput<float>("Metallic",     0f);
        AddInput<float>("Roughness",    0.8f);
        AddInput<float>("Occlusion",    1f);
        AddInput<Float3>("Emission",    Float3.Zero);

        bool pbr = Lighting == ShaderLightingMode.PBR;
        SetHidden("Metallic",  !pbr);
        SetHidden("Roughness", !pbr);
    }

    private void SetHidden(string name, bool hidden)
    {
        var p = GetInput(name);
        if (p != null) p.IsHidden = hidden;
    }
}
