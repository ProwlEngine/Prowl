// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>Records single-instance mesh draws for a culled index list, picking each material's pass by render-order tag.</summary>
internal static class RenderableDrawer
{
    public static void Draw(CommandBuffer cmd, Camera camera, IReadOnlyList<IRenderable> renderables, List<int> indices, string renderOrder)
    {
        var viewer = new ViewerData(camera);

        for (int i = 0; i < indices.Count; i++)
        {
            IRenderable renderable = renderables[indices[i]];

            renderable.GetRenderingData(viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData);
            if (instanceData != null && instanceData.Length > 0)
            {
                EmitRenderable(cmd, renderable, "", "", culled: true);
                continue;
            }

            Material material = renderable.GetMaterial();
            if (mesh.IsNotValid() || material.IsNotValid() || material.Shader.IsNotValid())
            {
                EmitRenderable(cmd, renderable, material.IsValid() ? material.Name : "", "", culled: true);
                continue;
            }

            Shader shader = material.Shader;
            ShaderPass? pass = shader.GetPassWithTagOrNull(PassTags.RenderOrder, renderOrder);
            if (pass == null)
                pass = shader.GetPass(0);

            EmitRenderable(cmd, renderable, material.Name, mesh.Name, culled: false);

            DrawParams p = new(model, renderable.GetWorldToObjectMatrix(model), GetPreviousMatrix(renderable, model), renderable.GetSubMeshIndex(), properties);
            cmd.DrawMesh(mesh, material, pass, in p);
        }
    }

    public static void EmitCulled(CommandBuffer cmd, IReadOnlyList<IRenderable> renderables, List<int> culled)
    {
        if (!cmd.WantsMetadata)
            return;

        for (int i = 0; i < culled.Count; i++)
        {
            IRenderable renderable = renderables[culled[i]];
            Material material = renderable.GetMaterial();
            EmitRenderable(cmd, renderable, material.IsValid() ? material.Name : "", "", culled: true);
        }
    }

    private static Float4x4 GetPreviousMatrix(IRenderable renderable, in Float4x4 model)
    {
        if (renderable is MeshRenderable mesh)
            return mesh.PreviousMatrix;
        if (renderable is SkinnedMeshRenderable skinned)
            return skinned.PreviousMatrix;
        return model;
    }

    private static void EmitRenderable(CommandBuffer cmd, IRenderable renderable, string materialName, string meshName, bool culled)
    {
        if (!cmd.WantsMetadata)
            return;

        cmd.RecordMetadata(new RenderableMetadata
        {
            MaterialName = materialName,
            MeshName = meshName,
            Layer = renderable.GetLayer(),
            Position = renderable.GetPosition(),
            Culled = culled,
        });
    }
}
