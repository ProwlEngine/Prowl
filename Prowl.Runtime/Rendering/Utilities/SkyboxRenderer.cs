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
    private static Material? s_gradientSkybox;
    private static Material? s_cubemapSkybox;
    private static bool s_loggedMissingShader;

    public static void Render(CommandBuffer cmd)
    {
        EnsureResources();
        if (s_skyDome == null || s_skybox == null)
            return;

        s_skybox.SetVector("_SunDir", Float3.Normalize(new Float3(0.5f, 0.7f, 0.5f)));
        cmd.DrawMesh(s_skyDome, s_skybox);
    }

    public static void Render(CommandBuffer cmd, Scene scene, IRenderableLight? directionalLight)
    {
        EnsureResources();
        if (s_skyDome == null)
            return;

        Scene.SkyboxParams skyParams = scene.Skybox;

        switch (skyParams.Mode)
        {
            case Scene.SkyboxMode.Procedural:
            {
                if (s_skybox == null)
                    return;
                Float3 sunDir = directionalLight != null
                    ? directionalLight.GetLightDirection()
                    : Float3.Normalize(new Float3(0.5f, -0.7f, 0.5f));
                s_skybox.SetVector("_SunDir", sunDir);
                cmd.DrawMesh(s_skyDome, s_skybox);
                break;
            }

            case Scene.SkyboxMode.SolidColor:
                break;

            case Scene.SkyboxMode.Gradient:
            {
                if (s_gradientSkybox == null)
                    return;
                s_gradientSkybox.SetColor("_TopColor", skyParams.GradientTop);
                s_gradientSkybox.SetColor("_BottomColor", skyParams.GradientBottom);
                s_gradientSkybox.SetFloat("_Exponent", skyParams.GradientExponent);
                cmd.DrawMesh(s_skyDome, s_gradientSkybox);
                break;
            }

            case Scene.SkyboxMode.Material:
            {
                Material? customMat = skyParams.CustomMaterial.Res;
                if (customMat.IsValid())
                    cmd.DrawMesh(s_skyDome, customMat);
                else if (s_cubemapSkybox != null)
                    cmd.DrawMesh(s_skyDome, s_cubemapSkybox);
                break;
            }
        }
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
            else if (!s_loggedMissingShader)
            {
                s_loggedMissingShader = true;
                Debug.LogError("SkyboxRenderer: Shader.LoadDefault(DefaultShader.ProceduralSkybox) returned an invalid shader.");
            }
        }

        if (s_gradientSkybox == null)
        {
            Shader? shader = Shader.LoadDefault(DefaultShader.GradientSkybox);
            if (shader.IsValid())
                s_gradientSkybox = new Material(shader);
        }

        if (s_cubemapSkybox == null)
        {
            Shader? shader = Shader.LoadDefault(DefaultShader.CubemapSkybox);
            if (shader.IsValid())
                s_cubemapSkybox = new Material(shader);
        }
    }
}
