// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Collects GPU-instanced tree renderables from the terrain's tree instance list.
/// Owned by TerrainComponent, not a standalone component.
/// </summary>
internal class TerrainTreeRenderer
{
    // Per-type scratch buffers reused each frame
    private readonly List<Float4x4> _transforms = [];
    private readonly List<Float4> _colors = [];

    public void CollectRenderables(
        TerrainData data,
        TerrainComponent terrain,
        Camera camera,
        float maxDistance,
        List<IRenderable> renderables)
    {
        if (data.Trees.Count == 0 || data.TreePrototypes.Length == 0)
            return;

        Float3 terrainPos = terrain.Transform.Position;
        Float3 cameraPos = camera.Transform.Position;
        float terrainSize = data.Size;
        float maxDistSq = maxDistance * maxDistance;

        // Group trees by prototype and render each group
        for (int protoIdx = 0; protoIdx < data.TreePrototypes.Length; protoIdx++)
        {
            var proto = data.TreePrototypes[protoIdx];
            var mesh = proto.Mesh.Res;
            if (mesh == null) continue;

            var mat = proto.Material.Res ?? Material.LoadDefault(DefaultMaterial.Standard);
            if (mat == null) continue;

            _transforms.Clear();
            _colors.Clear();

            foreach (var tree in data.Trees)
            {
                if (tree.PrototypeIndex != protoIdx) continue;

                // World position from terrain UV
                float worldX = (float)terrainPos.X + tree.Position.X * terrainSize;
                float worldZ = (float)terrainPos.Z + tree.Position.Y * terrainSize;
                float worldY = (float)terrainPos.Y + data.GetInterpolatedHeight(tree.Position.X, tree.Position.Y);

                // Distance culling
                float dx = worldX - (float)cameraPos.X;
                float dz = worldZ - (float)cameraPos.Z;
                if (dx * dx + dz * dz > maxDistSq) continue;

                // Build transform: translate * rotateY * uniform scale
                var transform = Float4x4.CreateScale(tree.Scale)
                    * Float4x4.FromAxisAngle(new Float3(0, 1, 0), tree.Rotation)
                    * Float4x4.CreateTranslation(new Float3(worldX, worldY, worldZ));

                _transforms.Add(transform);
                _colors.Add(new Float4(tree.Tint.R, tree.Tint.G, tree.Tint.B, tree.Tint.A));
            }

            if (_transforms.Count == 0) continue;

            Float3 boundsMin = terrainPos;
            Float3 boundsMax = terrainPos + new Float3(terrainSize, data.Height + 20f, terrainSize);
            var bounds = new AABB(boundsMin, boundsMax);

            InstancedMeshRenderable.CreateBatched(
                renderables,
                mesh,
                mat,
                [.. _transforms],
                (boundsMin + boundsMax) * 0.5f,
                [.. _colors],
                layer: terrain.GameObject.LayerIndex,
                bounds: bounds
            );
        }
    }
}
