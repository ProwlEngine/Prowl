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
/// the Create menu. Currently each template just adds the master output node — the
/// property/sampler nodes that used to seed each template were swept out as part of
/// the node-rewrite pass; this file will be rebuilt once the new property + texture
/// nodes are in (next pass).
/// </summary>
public static class ShaderGraphTemplates
{
    public static void Register()
    {
        const string ext = ".shadergraph";
        const string icon = "";
        CreateAssetMenuRegistry.RemoveManualEntriesByPrefix("Shader Graph/");
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Lit PBR",     ext, icon, 50, typeof(ShaderGraph), Surface);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Lit Basic",   ext, icon, 51, typeof(ShaderGraph), LitBasic);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Unlit",       ext, icon, 52, typeof(ShaderGraph), Unlit);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Particle",    ext, icon, 53, typeof(ShaderGraph), Particle);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Sky",         ext, icon, 54, typeof(ShaderGraph), Sky);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Post Effect", ext, icon, 55, typeof(ShaderGraph), PostEffect);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Terrain",     ext, icon, 56, typeof(ShaderGraph), Terrain);
        CreateAssetMenuRegistry.AddManualEntry("Shader Graph/Grass",       ext, icon, 57, typeof(ShaderGraph), Grass);
        Debug.Log("[ShaderGraph] Registered 8 template menu entries under 'Shader Graph/'.");
    }

    private static ShaderGraph New(ShaderGraphTemplate kind) => new ShaderGraph { Template = kind };

    private static PBROutputNode AddMaster(ShaderGraph g, Float2 pos)
    {
        var master = new PBROutputNode { Position = pos };
        g.AddNode(master);
        return master;
    }

    public static EngineObject Surface()
    {
        var g = New(ShaderGraphTemplate.Surface);
        AddMaster(g, new Float2(500, 80));
        return g;
    }

    public static EngineObject Unlit()
    {
        var g = New(ShaderGraphTemplate.Unlit);
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        return g;
    }

    public static EngineObject LitBasic()
    {
        var g = New(ShaderGraphTemplate.LitBasic);
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Lambert;
        return g;
    }

    public static EngineObject Particle()
    {
        var g = New(ShaderGraphTemplate.Particle);
        g.RenderSettings = ShaderGraphRenderSettings.AdditiveDefaults();
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        return g;
    }

    public static EngineObject Sky()
    {
        var g = New(ShaderGraphTemplate.Sky);
        g.RenderSettings = ShaderGraphRenderSettings.SkyDefaults();
        var master = AddMaster(g, new Float2(500, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        return g;
    }

    public static EngineObject PostEffect()
    {
        var g = New(ShaderGraphTemplate.PostEffect);
        g.RenderSettings = ShaderGraphRenderSettings.PostEffectDefaults();
        var master = AddMaster(g, new Float2(540, 80));
        master.Lighting = ShaderLightingMode.Unlit;
        return g;
    }

    public static EngineObject Terrain()
    {
        var g = New(ShaderGraphTemplate.Terrain);
        AddMaster(g, new Float2(500, 80));
        return g;
    }

    public static EngineObject Grass()
    {
        var g = New(ShaderGraphTemplate.Grass);
        AddMaster(g, new Float2(500, 80));
        return g;
    }
}
