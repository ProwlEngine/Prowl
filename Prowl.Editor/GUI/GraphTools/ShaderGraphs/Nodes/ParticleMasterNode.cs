// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Master output for the <c>Particle</c> shader type. GPU-instanced billboards;
/// vertex stage extracts position / rotation / scale from the instance matrix and
/// builds a camera-aligned quad. Graph authors just supply the per-pixel colour
/// (and optional soft-particles toggle for depth fade).
/// </summary>
public sealed class ParticleMasterNode : MasterNodeBase
{
    public override string Title => "Particle Output";

    /// <summary>
    /// When on, the fragment samples <c>_CameraDepthTexture</c> and fades alpha near
    /// intersections with scene geometry. Requires the camera's DepthTextureMode to
    /// include Depth; otherwise the sample returns 0 and the particle disappears
    /// when near geometry. Users who want full control over depth fade can leave
    /// this off and wire a <c>Depth Blend</c> node into Alpha manually.
    /// </summary>
    public bool SoftParticles = true;

    /// <summary>Blend distance (world units) for the soft-particles fade. Only used
    /// when <see cref="SoftParticles"/> is on.</summary>
    public float SoftParticleDistance = 1.0f;

    protected override void DefineNode()
    {
        // RGB color + alpha together default is white opaque so a freshly-seeded
        // graph shows something instead of black+transparent.
        AddInput<Float4>("Color", new Float4(1f, 1f, 1f, 1f),
            tooltip: "RGB x A output per pixel. Alpha drives the blend (Additive / Alpha / etc. from RenderSettings).");

        // Optional explicit alpha override when wired, replaces Color.a.
        AddInput<float>("Alpha", 1f,
            tooltip: "Optional alpha override. When unwired, Color.a is used directly.");
    }
}
