// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Draws the debug gizmo geometry (wire + solid batches and icon quads) into a command buffer whose
/// render target is already bound. A pass calls <see cref="Render"/> once; the utility records the
/// gizmo draws and returns.
/// </summary>
public static class GizmoRenderer
{
    private static Material? s_gizmoMaterial;
    private static Material? s_iconMaterial;
    private static Mesh? s_iconQuad;

    /// <summary>Records the current frame's gizmos into <paramref name="cmd"/>.</summary>
    public static void Render(CommandBuffer cmd, Texture2D depthCopy)
    {
        Shader? gizmoShader = Shader.LoadDefault(DefaultShader.Gizmos);
        if (gizmoShader.IsValid())
        {
            s_gizmoMaterial ??= new Material(gizmoShader);
            s_gizmoMaterial.SetTexture("_CameraDepthTexture", depthCopy);
            ShaderPass pass = gizmoShader.GetPass(0);
            if (pass != null)
            {
                (GizmoBuilder.Batch? wire, GizmoBuilder.Batch? solid) = Debug.UploadGizmos();
                if (wire is { HasData: true }) DrawBatch(cmd, pass, s_gizmoMaterial, wire);
                if (solid is { HasData: true }) DrawBatch(cmd, pass, s_gizmoMaterial, solid);
            }
        }

        DrawIcons(cmd, depthCopy);
    }

    private static void DrawBatch(CommandBuffer cmd, ShaderPass pass, Material material, GizmoBuilder.Batch batch)
    {
        cmd.SetShader(pass);
        cmd.SetMaterialProperties(material);
        cmd.SetVertexSource(batch);
        cmd.DrawIndexed(1, 0, 0, 0);
    }

    private static void DrawIcons(CommandBuffer cmd, Texture2D depthCopy)
    {
        List<GizmoBuilder.IconDrawCall> icons = Debug.GetGizmoIcons();
        if (icons.Count == 0)
            return;

        Shader? iconShader = Shader.LoadDefault(DefaultShader.GizmoIcon);
        if (!iconShader.IsValid())
            return;

        s_iconMaterial ??= new Material(iconShader);
        s_iconMaterial.SetTexture("_CameraDepthTexture", depthCopy);
        s_iconQuad ??= Mesh.GetFullscreenQuad();

        foreach (GizmoBuilder.IconDrawCall icon in icons)
        {
            if (icon.Texture == null || icon.Texture.IsDisposed) continue;

            s_iconMaterial.SetTexture("_MainTex", icon.Texture);
            s_iconMaterial.SetVector("_IconCenter", icon.Center);
            s_iconMaterial.SetFloat("_IconScale", icon.Scale);
            s_iconMaterial.SetVector("_IconColor", new Float4(icon.Color.R, icon.Color.G, icon.Color.B, icon.Color.A));
            cmd.DrawMesh(s_iconQuad, s_iconMaterial);
        }
    }
}
