// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

internal class TerrainTreeRenderer
{
    private readonly List<Float4x4> _transforms = [];
    private readonly List<Float4> _colors = [];

    public void CollectRenderables(
        TerrainData data, TerrainComponent terrain, Camera camera,
        float maxDistance, List<IRenderable> renderables)
    {
        if (data.Trees.Count == 0 || data.TreePrototypes.Count == 0) return;

        Float3 terrainPos = terrain.Transform.Position;
        Float3 cameraPos = camera.Transform.Position;
        float terrainSize = data.Size;
        float maxDistSq = maxDistance * maxDistance;

        for (int protoIdx = 0; protoIdx < data.TreePrototypes.Count; protoIdx++)
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

                float wx = (float)terrainPos.X + tree.Position.X * terrainSize;
                float wz = (float)terrainPos.Z + tree.Position.Y * terrainSize;
                float wy = (float)terrainPos.Y + data.GetInterpolatedHeight(tree.Position.X, tree.Position.Y);

                float dx = wx - (float)cameraPos.X;
                float dz = wz - (float)cameraPos.Z;
                if (dx * dx + dz * dz > maxDistSq) continue;

                var transform = Float4x4.CreateTranslation(new Float3(wx, wy, wz))
                    * Float4x4.FromAxisAngle(new Float3(0, 1, 0), tree.Rotation)
                    * Float4x4.CreateScale(new Float3(tree.WidthScale, tree.HeightScale, tree.WidthScale));

                _transforms.Add(transform);
                _colors.Add(new Float4(tree.Tint.R, tree.Tint.G, tree.Tint.B, tree.Tint.A));
            }

            if (_transforms.Count == 0) continue;

            Float3 bmin = terrainPos;
            Float3 bmax = terrainPos + new Float3(terrainSize, data.Height + 20f, terrainSize);

            InstancedMeshRenderable.CreateBatched(
                renderables, mesh, mat, [.. _transforms],
                (bmin + bmax) * 0.5f, [.. _colors],
                layer: terrain.GameObject.LayerIndex,
                bounds: new AABB(bmin, bmax));
        }
    }
}
