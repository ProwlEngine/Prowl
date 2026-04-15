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
    private readonly List<Rendering.InstanceData> _instanceDataList = [];
    private static Material? s_defaultStandardMat;
    private static readonly PropertyState s_emptyProps = new();

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

            s_defaultStandardMat ??= Material.LoadDefault(DefaultMaterial.Standard);
            var mat = proto.Material.Res ?? s_defaultStandardMat;
            if (mat == null) continue;

            _transforms.Clear();
            _colors.Clear();
            _instanceDataList.Clear();

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

                var worldTransform = terrain.Transform.LocalToWorldMatrix * localTransform;
                var color = new Float4(tree.Tint.R, tree.Tint.G, tree.Tint.B, tree.Tint.A);
                _transforms.Add(worldTransform);
                _colors.Add(color);
                _instanceDataList.Add(new Rendering.InstanceData(worldTransform, color));
            }

            if (_instanceDataList.Count == 0) continue;

            // Bounds from actual positions + mesh extents
            float meshExtent = mesh.bounds.Size.X > 0 ? MathF.Max(mesh.bounds.Size.X, mesh.bounds.Size.Z) * 0.5f : 5f;
            float meshHeight = mesh.bounds.Size.Y > 0 ? mesh.bounds.Size.Y : 20f;
            Float3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
            foreach (var tr in _transforms)
            {
                float tx = tr[0, 3], ty = tr[1, 3], tz = tr[2, 3];
                bmin = new Float3(MathF.Min(bmin.X, tx - meshExtent), MathF.Min(bmin.Y, ty), MathF.Min(bmin.Z, tz - meshExtent));
                bmax = new Float3(MathF.Max(bmax.X, tx + meshExtent), MathF.Max(bmax.Y, ty + meshHeight), MathF.Max(bmax.Z, tz + meshExtent));
            }

            renderables.Add(new InstancedMeshRenderable(
                mesh, mat, [.. _instanceDataList],
                (bmin + bmax) * 0.5f,
                terrain.GameObject.LayerIndex,
                s_emptyProps,
                new AABB(bmin, bmax)));
        }
    }
}
