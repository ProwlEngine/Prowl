// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>How the master node lights its surface. Mirrors ShaderForge's lighting
/// mode dropdown. Selecting a mode hides mode-specific ports on the master node and
/// switches the compiler to a different emission path.</summary>
/// <remarks>Custom lighting isn't a separate mode — <see cref="Unlit"/> already
/// passes albedo straight through, so users who want to drive their own lit colour
/// just compute it inside the graph and wire the result into Albedo.</remarks>
public enum ShaderLightingMode
{
    /// <summary>Albedo passes straight through — no lighting calc, no normals,
    /// no PBR. Useful for UI, particles, billboards, gizmos, and custom-lighting
    /// graphs where the user builds their lighting math inside the graph.</summary>
    Unlit,
    /// <summary>Full PBR forward lighting using Prowl's standard pipeline.</summary>
    PBR,
    /// <summary>Lambert diffuse only — cheaper than PBR, no specular term. Just
    /// albedo · N·L plus ambient.</summary>
    Lambert,
    /// <summary>Blinn-Phong — Lambert diffuse plus a half-vector specular lobe. Uses
    /// Roughness input as a gloss exponent (higher = tighter highlight).</summary>
    BlinnPhong,
}

/// <summary>
/// The master output node for surface shaders — every shader graph needs exactly one.
/// Hidden from the creation menu (auto-seeded by templates) and intentionally NOT
/// <c>IShaderNode</c> — the compiler dispatches it directly via type-check.
/// </summary>
/// <remarks>
/// Lighting / surface features are picked via enum + bool toggles on the node itself,
/// not via separate node types — same pattern as ShaderForge's SFN_Final + the
/// graph-level lighting settings. Toggles drive <c>Port.IsHidden</c> so the user
/// only sees the inputs that actually matter for the chosen mode.
/// </remarks>
[HiddenFromMenu]
public sealed class PBROutputNode : Node, IShaderGraphNode
{
    public override string Title => "Output";
    public override string Category => "Output";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 200, 80, 100);

    /// <summary>Lighting model the compiler emits. Unlit hides every PBR input.</summary>
    public ShaderLightingMode Lighting = ShaderLightingMode.PBR;

    /// <summary>Anisotropic specular path (PBR only). Adds Anisotropy + Direction inputs.</summary>
    public bool Anisotropic = false;

    /// <summary>Parallax Occlusion Mapping (PBR only). Adds Height + Height Steps inputs.</summary>
    public bool UseParallax = false;

    /// <summary>Per-light backscatter / subsurface scattering (PBR only).</summary>
    public bool UseTranslucency = false;

    protected override void DefineNode()
    {
        // ─── Vertex stage ─────────────────────────────────────────────────────────────
        AddInput<Float3>("Vertex Position",  Float3.Zero, layout: PortLayout.Above);
        AddInput<Float3>("Vertex Normal",    Float3.Zero, layout: PortLayout.Above);

        // ─── Surface (fragment) ───────────────────────────────────────────────────────
        // Defaults match Prowl's StandardSurface.glsl: white albedo (bright bland
        // PBR baseline), tangent-space (0,0,1) normal (passthrough), non-metal,
        // mid-rough, full ambient (AO=1, "white" texture convention), no emission,
        // no alpha cutoff (cutoff=0 means the discard never fires).
        AddInput<Color>("Albedo",            new Color(1f, 1f, 1f, 1f));
        AddInput<float>("Alpha",             1.0f);
        AddInput<Float3>("Normal",           new Float3(0, 0, 1));   // tangent-space
        AddInput<float>("Metallic",          0.0f);
        AddInput<float>("Roughness",         0.5f);
        AddInput<float>("Occlusion",         1.0f);
        AddInput<Float3>("Emission",         Float3.Zero);
        AddInput<float>("Alpha Cutoff",      0.0f);

        // ─── POM (only meaningful when UseParallax + PBR) ────────────────────────────
        AddInput<float>("Height",            0.0f);
        AddInput<int>("Height Steps",        16);

        // ─── Translucency (only meaningful when UseTranslucency + PBR) ───────────────
        AddInput<float>("Translucency",          0.0f);
        AddInput<float>("Scattering Power",      0.0f);
        AddInput<float>("Scattering Distortion", 0.5f);
        AddInput<float>("Scattering Scale",      1.0f);

        // ─── Anisotropic-only inputs (PBR) ───────────────────────────────────────────
        AddInput<float>("Anisotropy",            0.0f);
        AddInput<Float3>("Anisotropy Direction", new Float3(1, 0, 0));

        // Each lighting mode uses a distinct subset of inputs — hiding ports reduces
        // visual clutter without having to duplicate the node per mode.
        bool lambert    = Lighting == ShaderLightingMode.Lambert;
        bool blinnphong = Lighting == ShaderLightingMode.BlinnPhong;
        bool pbr        = Lighting == ShaderLightingMode.PBR;
        bool anyLit     = lambert || blinnphong || pbr;

        // Normal applies to anything that lights the surface.
        SetHiddenByName("Normal",     !anyLit);
        // Metallic/Roughness/Occlusion are PBR-only; Blinn-Phong reuses Roughness as
        // a gloss exponent so we keep it visible in that mode.
        SetHiddenByName("Metallic",   !pbr);
        SetHiddenByName("Roughness",  !(pbr || blinnphong));
        SetHiddenByName("Occlusion",  !anyLit);

        // Feature toggles are PBR-only — the simpler lighting modes don't support them.
        SetHiddenByName("Height",                 !(pbr && UseParallax));
        SetHiddenByName("Height Steps",           !(pbr && UseParallax));
        SetHiddenByName("Translucency",           !(pbr && UseTranslucency));
        SetHiddenByName("Scattering Power",       !(pbr && UseTranslucency));
        SetHiddenByName("Scattering Distortion",  !(pbr && UseTranslucency));
        SetHiddenByName("Scattering Scale",       !(pbr && UseTranslucency));
        SetHiddenByName("Anisotropy",             !(pbr && Anisotropic));
        SetHiddenByName("Anisotropy Direction",   !(pbr && Anisotropic));
    }

    private void SetHiddenByName(string name, bool hidden)
    {
        var p = GetInput(name);
        if (p != null) p.IsHidden = hidden;
    }
}
