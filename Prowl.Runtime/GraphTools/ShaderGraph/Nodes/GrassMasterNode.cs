// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Master output for the <c>Grass</c> shader type. GPU-instanced blades with
/// per-instance position (terrain-local, read from <c>instanceModelRow3</c>),
/// camera-axis billboarding, terrain-normal alignment, wind sway, and distance
/// fade. The vertex stage is hardcoded by <c>GrassPass</c> — users drive the
/// fragment surface via the inputs below.
/// </summary>
public sealed class GrassMasterNode : MasterNodeBase
{
    public override string Title => "Grass Output";

    /// <summary>Lighting model. Lambert is the sane default for grass — cheap,
    /// no specular, works great with terrain-aligned normals.</summary>
    public ShaderLightingMode Lighting = ShaderLightingMode.Lambert;

    /// <summary>When true, each blade's normal aligns with the terrain surface at
    /// its footprint (lit consistently with the ground). When false, normals face
    /// world-up — more artistic, less grounded.</summary>
    public bool AlignToTerrain = true;

    /// <summary>Cylindrical-billboard (true) vs baked mesh orientation (false).
    /// Billboarding keeps blades facing the camera; static mesh orientation is
    /// useful for authored blade meshes.</summary>
    public bool Billboard = true;

    protected override void DefineNode()
    {
        // Fragment surface — Albedo + alpha cutout matches the hand-written grass shader.
        AddInput<Color>("Albedo",       new Color(1f, 1f, 1f, 1f));
        AddInput<float>("Alpha",        1f);
        AddInput<float>("Alpha Cutoff", 0.5f,
            tooltip: "Discard threshold. 0 disables the cutout.");
        AddInput<Float3>("Emission",    Float3.Zero);

        // Lit variants add normal + PBR knobs. Grass is rarely metal so Metallic is hidden by default.
        AddInput<Float3>("Normal",      new Float3(0, 0, 1));
        AddInput<float>("Roughness",    0.9f);

        // Wind inputs — scalars drive how strong and how fast the blades sway. Wired
        // as inputs so users can modulate them with global wind nodes / noise / etc.
        // Sane defaults match Default/Grass.shader.
        AddInput<float>("Wind Strength", 0.3f);
        AddInput<float>("Wind Speed",    1.5f);

        bool unlit = Lighting == ShaderLightingMode.Unlit;
        SetHidden("Normal",    unlit);
        SetHidden("Roughness", Lighting != ShaderLightingMode.PBR && Lighting != ShaderLightingMode.BlinnPhong);
    }

    private void SetHidden(string name, bool hidden)
    {
        var p = GetInput(name);
        if (p != null) p.IsHidden = hidden;
    }
}
