// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Draws the procedural skybox into a command buffer whose render target is already bound. The sky
/// shader treats its vertex positions as world-space directions and strips translation out of the
/// view matrix itself, so it must be drawn with an actual dome mesh rather than a fullscreen blit
/// triangle - a raw NDC triangle fed through that vertex shader produces garbage clip positions.
/// </summary>
public static class SkyboxRenderer
{
    private static Mesh? s_skyDome;
    private static Material? s_skybox;

    public static void Render(CommandBuffer cmd)
    {
        EnsureResources();
        if (s_skyDome == null || s_skybox == null)
            return;

        s_skybox.SetVector("_SunDir", Float3.Normalize(new Float3(0.5f, 0.7f, 0.5f)));
        cmd.DrawMesh(s_skyDome, s_skybox);
    }

    private static void EnsureResources()
    {
        if (s_skyDome == null)
        {
            using var stream = EmbeddedResources.GetStream("Assets/Defaults/SkyDome.obj");
            var skyImport = new AssetImporting.ModelImporter().Import(stream, "SkyDome.obj");
            s_skyDome = skyImport.Meshes.Count > 0 ? skyImport.Meshes[0] : new Mesh { Name = "SkyDome" };
            s_skyDome.Upload();
        }

        if (s_skybox == null)
        {
            Shader? shader = Shader.LoadDefault(DefaultShader.ProceduralSkybox);
            if (shader.IsValid())
                s_skybox = new Material(shader);
        }
    }
}
