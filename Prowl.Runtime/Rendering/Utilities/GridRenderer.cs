// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Draws the editor reference grid into a command buffer whose render target is already bound. The
/// grid mesh follows the camera on the XZ plane so it appears infinite. A pass calls
/// <see cref="Render"/> once with the camera position.
/// </summary>
public static class GridRenderer
{
    private static Mesh? s_gridMesh;
    private static Material? s_gridMaterial;
    private static bool s_loggedMissingShader;

    /// <summary>Records the grid into <paramref name="cmd"/>, centered under <paramref name="cameraPosition"/>.</summary>
    public static void Render(CommandBuffer cmd, Float3 cameraPosition, Texture2D depthCopy)
    {
        EnsureResources();
        if (s_gridMesh == null || s_gridMaterial == null)
            return;

        s_gridMaterial.SetTexture("_CameraDepthTexture", depthCopy);

        float cx = MathF.Round((float)cameraPosition.X);
        float cz = MathF.Round((float)cameraPosition.Z);
        Float4x4 model = Float4x4.CreateTranslation(new Float3(cx, 0, cz));

        cmd.DrawMesh(s_gridMesh, s_gridMaterial, 0, model, null);
    }

    private static void EnsureResources()
    {
        if (s_gridMesh == null)
        {
            const float e = 500f;
            var mesh = new Mesh
            {
                Vertices = [new(-e, 0, -e), new(e, 0, -e), new(e, 0, e), new(-e, 0, e)],
                UV = [new(-e, -e), new(e, -e), new(e, e), new(-e, e)],
                Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY],
                Indices = [0, 2, 1, 0, 3, 2]
            };
            mesh.RecalculateBounds();
            mesh.Upload();
            s_gridMesh = mesh;
        }

        if (s_gridMaterial == null)
        {
            Shader? shader = Shader.LoadDefault(DefaultShader.Grid);
            if (shader.IsValid())
            {
                s_gridMaterial = new Material(shader);
                s_gridMaterial.SetColor("_GridColor", new Color(0.5f, 0.5f, 0.5f, 0.3f));
                s_gridMaterial.SetFloat("_PrimaryGridSize", 1f);
                s_gridMaterial.SetFloat("_SecondaryGridSize", 0.25f);
                s_gridMaterial.SetFloat("_LineWidth", 0.02f);
                s_gridMaterial.SetFloat("_Falloff", 1.5f);
                s_gridMaterial.SetFloat("_MaxDist", 500f);
            }
            else if (!s_loggedMissingShader)
            {
                s_loggedMissingShader = true;
                Debug.LogError("GridRenderer: Shader.LoadDefault(DefaultShader.Grid) returned an invalid shader.");
            }
        }
    }
}
