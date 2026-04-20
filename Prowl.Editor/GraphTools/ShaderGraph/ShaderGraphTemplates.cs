// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools.ShaderGraphs;

/// <summary>
/// Builds pre-populated <see cref="ShaderGraph"/> instances for each template entry in
/// the Create menu. Each template factory does the same setup steps: instantiate the
/// graph, add the master output node at a sensible position, optionally seed a couple
/// of property nodes wired into common inputs. Persisted via Echo from there.
/// </summary>
public static class ShaderGraphTemplates
{
    /// <summary>Hook called once at editor startup AND after every script-assembly
    /// reload (manual entries get wiped by registry Reinitialize). Idempotent —
    /// removes any previously-registered template entries before re-adding so
    /// repeated calls don't stack duplicates.</summary>
    public static void Register()
    {
        const string ext = ".shadergraph";
        const string icon = ""; // default file icon
        CreateAssetMenuRegistry.RemoveManualEntriesByPrefix("Shader Graph/");
        // Lit family — users typically start here. Transparency isn't a template —
        // it's just render-state tweaks (Blend=Alpha, ZWrite=Off, Queue=Transparent)
        // exposed in the sidebar, so any of these can be flipped transparent afterwards.
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Lit PBR",     ext, icon, 50, typeof(ShaderGraph), Surface);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Lit Basic",   ext, icon, 51, typeof(ShaderGraph), LitBasic);
        // Unlit doubles as "custom lighting" — users building their own lighting math
        // wire the result into Albedo since no BRDF runs in Unlit mode.
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Unlit",       ext, icon, 52, typeof(ShaderGraph), Unlit);
        // Specialised surfaces — different render-state OR topology from the lit family.
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Particle",    ext, icon, 53, typeof(ShaderGraph), Particle);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Sky",         ext, icon, 54, typeof(ShaderGraph), Sky);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Post Effect", ext, icon, 55, typeof(ShaderGraph), PostEffect);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Terrain",     ext, icon, 56, typeof(ShaderGraph), Terrain);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Grass",       ext, icon, 57, typeof(ShaderGraph), Grass);
        Debug.Log("[ShaderGraph] Registered 8 template menu entries under 'Shader Graph/'.");
    }

    private static ShaderGraph New(ShaderGraphTemplate kind)
    {
        return new ShaderGraph { Template = kind };
    }

    private static PBROutputNode AddMaster(ShaderGraph g, Float2 pos)
    {
        var master = new PBROutputNode { Position = pos };
        g.AddNode(master);
        return master;
    }

    // ─── Surface ─────────────────────────────────────────────────────────────────────
    // Master output + a Color property wired into Albedo. Simplest possible
    // graph that still produces something visually meaningful.

    public static EngineObject Surface()
    {
        var g = New(ShaderGraphTemplate.Surface);
        var master = AddMaster(g, new Float2(500, 80));
        var col = new ColorPropertyNode
        {
            Name = "_MainColor", Label = "Color",
            Value = new Color(1, 1, 1, 1),
            Position = new Float2(120, 80),
        };
        g.AddNode(col);
        g.Edges.Add(new Edge {
            SourceNodeId = col.Id, SourcePortName = "Out",
            TargetNodeId = master.Id, TargetPortName = "Albedo",
        });
        return g;
    }

    // ─── Unlit ───────────────────────────────────────────────────────────────────────
    // Same shape as Surface but the master's Lighting mode flips to Unlit, which the
    // node's DefineNode then uses to hide every PBR-only port (Normal/Metallic/etc.).
    // No separate node type — same approach ShaderForge uses.

    public static EngineObject Unlit()
    {
        var g = New(ShaderGraphTemplate.Unlit);
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        var col = new ColorPropertyNode
        {
            Name = "_MainColor", Label = "Color",
            Value = new Color(1, 1, 1, 1),
            Position = new Float2(120, 80),
        };
        g.AddNode(col);
        g.Edges.Add(new Edge {
            SourceNodeId = col.Id, SourcePortName = "Out",
            TargetNodeId = master.Id, TargetPortName = "Albedo",
        });
        return g;
    }

    // ─── Lit Basic ──────────────────────────────────────────────────────────────────
    // Lambert diffuse — cheapest real-lighting path. Surface seed (color → albedo),
    // just without the PBR material terms.

    public static EngineObject LitBasic()
    {
        var g = New(ShaderGraphTemplate.LitBasic);
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Lambert;
        var col = new ColorPropertyNode
        {
            Name = "_MainColor", Label = "Color",
            Value = new Color(1, 1, 1, 1),
            Position = new Float2(120, 80),
        };
        g.AddNode(col);
        g.Edges.Add(new Edge {
            SourceNodeId = col.Id, SourcePortName = "Out",
            TargetNodeId = master.Id, TargetPortName = "Albedo",
        });
        return g;
    }

    // ─── Particle ───────────────────────────────────────────────────────────────────
    // Additive unlit billboard. Additive blend, no depth write, no culling — standard
    // particle setup. Compiler reads these off the graph's RenderSettings at compile.

    public static EngineObject Particle()
    {
        var g = New(ShaderGraphTemplate.Particle);
        g.RenderSettings = ShaderGraphRenderSettings.AdditiveDefaults();
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        var col = new ColorPropertyNode
        {
            Name = "_MainColor", Label = "Color",
            Value = new Color(1, 1, 1, 1),
            Position = new Float2(120, 60),
        };
        var alpha = new FloatPropertyNode
        {
            Name = "_Alpha", Label = "Alpha", Value = 1f,
            Position = new Float2(120, 200),
        };
        g.AddNode(col); g.AddNode(alpha);
        g.Edges.Add(new Edge { SourceNodeId = col.Id,   SourcePortName = "Out", TargetNodeId = master.Id, TargetPortName = "Albedo" });
        g.Edges.Add(new Edge { SourceNodeId = alpha.Id, SourcePortName = "Out", TargetNodeId = master.Id, TargetPortName = "Alpha" });
        return g;
    }

    // ─── Sky ─────────────────────────────────────────────────────────────────────────
    // Background pass: front-face cull, no zwrite, no lighting. Seed is a color that
    // users can replace with a cubemap sample or gradient node once those exist.

    public static EngineObject Sky()
    {
        var g = New(ShaderGraphTemplate.Sky);
        g.RenderSettings = ShaderGraphRenderSettings.SkyDefaults();
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        var col = new ColorPropertyNode
        {
            Name = "_SkyColor", Label = "Sky Color",
            Value = new Color(0.4f, 0.6f, 0.9f, 1f),
            Position = new Float2(120, 80),
        };
        g.AddNode(col);
        g.Edges.Add(new Edge { SourceNodeId = col.Id, SourcePortName = "Out", TargetNodeId = master.Id, TargetPortName = "Albedo" });
        return g;
    }

    // ─── Post Effect ─────────────────────────────────────────────────────────────────
    // Fullscreen image effect — source tex sampled at UV, straight pass-through.
    // Previously called Image Effect; same behaviour, renamed for consistency with the
    // Post Effect terminology used elsewhere.

    public static EngineObject PostEffect()
    {
        var g = New(ShaderGraphTemplate.PostEffect);
        g.RenderSettings = ShaderGraphRenderSettings.PostEffectDefaults();
        var master = AddMaster(g, new Float2(540, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        var tex = new Texture2DPropertyNode
        {
            Name = "_MainTex", Label = "Source",
            DefaultTextureName = "white",
            Position = new Float2(60, 60),
        };
        var sample = new Tex2DSampleNode { Position = new Float2(280, 80) };
        g.AddNode(tex); g.AddNode(sample);
        g.Edges.Add(new Edge { SourceNodeId = tex.Id,    SourcePortName = "Out",  TargetNodeId = sample.Id, TargetPortName = "Sampler" });
        g.Edges.Add(new Edge { SourceNodeId = sample.Id, SourcePortName = "RGBA", TargetNodeId = master.Id, TargetPortName = "Albedo" });
        return g;
    }

    // ─── Terrain ─────────────────────────────────────────────────────────────────────
    // Just a master + a placeholder texture for now — a proper splat-blend layout is
    // worth its own follow-up once texture-blending nodes exist (#97).

    public static EngineObject Terrain()
    {
        var g = New(ShaderGraphTemplate.Terrain);
        var master = AddMaster(g, new Float2(500, 80));
        var tex = new Texture2DPropertyNode
        {
            Name = "_MainTex", Label = "Diffuse",
            DefaultTextureName = "white",
            Position = new Float2(60, 60),
        };
        var sample = new Tex2DSampleNode { Position = new Float2(280, 80) };
        g.AddNode(tex); g.AddNode(sample);
        g.Edges.Add(new Edge { SourceNodeId = tex.Id,    SourcePortName = "Out",  TargetNodeId = sample.Id, TargetPortName = "Sampler" });
        g.Edges.Add(new Edge { SourceNodeId = sample.Id, SourcePortName = "RGBA", TargetNodeId = master.Id, TargetPortName = "Albedo" });
        return g;
    }

    // ─── Grass ───────────────────────────────────────────────────────────────────────
    // Same starter as Surface for now — vertex-stage wind drives in a follow-up once
    // the vertex-stage nodes (Time, Sin, Vertex Position offset) exist.

    public static EngineObject Grass()
    {
        var g = New(ShaderGraphTemplate.Grass);
        var master = AddMaster(g, new Float2(500, 80));
        var col = new ColorPropertyNode
        {
            Name = "_MainColor", Label = "Tint", Value = new Color(0.4f, 0.7f, 0.3f, 1f),
            Position = new Float2(120, 80),
        };
        g.AddNode(col);
        g.Edges.Add(new Edge { SourceNodeId = col.Id, SourcePortName = "Out", TargetNodeId = master.Id, TargetPortName = "Albedo" });
        return g;
    }

}
