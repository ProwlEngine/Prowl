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

        Float3 camLocal = terrain.WorldToTerrain(camera.Transform.Position);
        camLocal.Y = 0; // Project to terrain XZ plane for 2D distance checks
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

                // Position in terrain-local space
                float lx = tree.Position.X * terrainSize;
                float lz = tree.Position.Y * terrainSize;
                float ly = data.GetInterpolatedHeight(tree.Position.X, tree.Position.Y);

                // Distance check in local space
                float dx = lx - (float)camLocal.X;
                float dz = lz - (float)camLocal.Z;
                if (dx * dx + dz * dz > maxDistSq) continue;

                // Build local transform, then apply terrain world matrix
                Float4x4 localTransform = Float4x4.CreateTranslation(new Float3(lx, ly, lz))
                    * Float4x4.FromAxisAngle(new Float3(0, 1, 0), tree.Rotation)
                    * Float4x4.CreateScale(new Float3(tree.WidthScale, tree.HeightScale, tree.WidthScale));

                _transforms.Add(terrain.Transform.LocalToWorldMatrix * localTransform);
                _colors.Add(new Float4(tree.Tint.R, tree.Tint.G, tree.Tint.B, tree.Tint.A));
            }

            if (_transforms.Count == 0) continue;

            // Bounds from actual positions
            Float3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
            foreach (var tr in _transforms)
            {
                float tx = tr[0, 3], ty = tr[1, 3], tz = tr[2, 3];
                bmin = new Float3(MathF.Min(bmin.X, tx - 5), MathF.Min(bmin.Y, ty), MathF.Min(bmin.Z, tz - 5));
                bmax = new Float3(MathF.Max(bmax.X, tx + 5), MathF.Max(bmax.Y, ty + 20), MathF.Max(bmax.Z, tz + 5));
            }

            InstancedMeshRenderable.CreateBatched(
                renderables, mesh, mat, [.. _transforms],
                (bmin + bmax) * 0.5f, [.. _colors],
                layer: terrain.GameObject.LayerIndex,
                bounds: new AABB(bmin, bmax));
        }
    }
}
