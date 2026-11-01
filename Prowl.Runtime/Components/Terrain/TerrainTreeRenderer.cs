// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Places a terrain's trees in the world once, when they or the terrain change, and lets each view draw the ones
/// within <see cref="TerrainComponent.TreeDistance"/> of its camera that it can see.
/// </summary>
internal class TerrainTreeRenderer
{
    private int _signature;
    private bool _placed;
    private Instances[] _instances = [];

    /// <summary>One prototype's trees. Its arrays are never changed once built.</summary>
    public sealed class Instances
    {
        public int Prototype;
        public Double4x4[] Transforms = [];
        public Float4[] Tints = [];
        public Float2[] Local = [];
        public Double3[] Centres = [];
        public float[] Radii = [];
    }

    /// <summary>Trees of one prototype with what they draw with this frame.</summary>
    public readonly struct Batch(Instances instances, GpuMesh mesh, GMaterial[] materials)
    {
        public readonly Instances Instances = instances;
        public readonly GpuMesh Mesh = mesh;
        public readonly GMaterial[] Materials = materials;
    }

    /// <summary>A terrain's trees for one frame, copied on the host.</summary>
    public sealed class Frame
    {
        public Batch[] Batches = [];
        public float Distance;
        public RenderableFlags Flags;
        public uint LayerMask;
        public ProbeAnchorHandle Probe;
        public uint ObjectId;

        /// <summary>Adds the trees near any of the cameras, in terrain local space, that the view sees. Render thread.</summary>
        public void Cull(List<Float3> cameras, in ProviderView view, DrawItemSink sink)
        {
            float maxDistSq = Distance * Distance;
            foreach (Batch batch in Batches)
            {
                Instances trees = batch.Instances;
                for (int i = 0; i < trees.Transforms.Length; i++)
                {
                    if (!Near(trees.Local[i], cameras, maxDistSq) || !view.Sees(trees.Centres[i], trees.Radii[i])) continue;
                    var item = new DrawItem
                    {
                        Transform = trees.Transforms[i],
                        Color = trees.Tints[i],
                        LayerMask = LayerMask,
                        ObjectId = ObjectId,
                        Flags = Flags,
                        Probe = Probe,
                    };
                    for (int sub = 0; sub < batch.Materials.Length; sub++)
                        sink.Add(batch.Mesh, sub, batch.Materials[sub], item);
                }
            }
        }

        private static bool Near(Float2 local, List<Float3> cameras, float maxDistSq)
        {
            foreach (Float3 camera in cameras)
            {
                float dx = local.X - camera.X, dz = local.Y - camera.Z;
                if (dx * dx + dz * dz <= maxDistSq) return true;
            }
            return false;
        }
    }

    /// <summary>Copies what the render thread needs to draw the trees this frame, placing them again when they changed. Host thread.</summary>
    public Frame? Prepare(TerrainData data, TerrainComponent terrain)
    {
        if (data.Trees.Count == 0 || data.TreePrototypes.Count == 0) return null;

        Float4x4 terrainToWorld = terrain.Transform.LocalToWorldMatrix;
        int signature = Signature(data, terrainToWorld);
        if (signature != _signature || !_placed)
        {
            _signature = signature;
            _placed = true;
            _instances = Place(data, terrainToWorld);
        }

        var batches = new List<Batch>();
        foreach (Instances trees in _instances)
        {
            TreePrototype proto = data.TreePrototypes[trees.Prototype];
            GpuMesh? mesh = TerrainComponent.GpuMeshOf(proto.Mesh);
            if (mesh == null) continue;

            // One draw per submesh, so trees with multi-material meshes render each with its own material
            int subMeshCount = proto.Mesh!.SubMeshCount;
            var materials = new GMaterial[subMeshCount];
            bool ready = true;
            for (int sub = 0; sub < subMeshCount && ready; sub++)
            {
                Material? material = sub < proto.Materials.Count ? proto.Materials[sub] : null;
                GMaterial? gpu = TerrainComponent.GpuMaterialOf(material) ?? RenderableState.DefaultGpuMaterial;
                if (gpu == null) ready = false;
                else materials[sub] = gpu;
            }
            if (ready) batches.Add(new Batch(trees, mesh, materials));
        }
        if (batches.Count == 0) return null;

        GameObject go = terrain.GameObject;
        return new Frame
        {
            Batches = [.. batches],
            Distance = terrain.TreeDistance,
            Flags = terrain.RenderFlags,
            LayerMask = RenderableState.LayerBit(go.LayerIndex),
            ObjectId = (uint)terrain.InstanceID,
            Probe = terrain.ProbeAnchor,
        };
    }

    // Everything the placed trees depend on, so they are placed again only when one of them moves
    private static int Signature(TerrainData data, Float4x4 terrainToWorld)
    {
        var hash = new HashCode();
        hash.Add(terrainToWorld);
        hash.Add(data.Size);
        hash.Add(data.Height);
        hash.Add(data.Interpolation);
        hash.Add(data.HeightsVersion);
        hash.Add(data.HolesVersion);
        foreach (TreePrototype proto in data.TreePrototypes)
        {
            Mesh? mesh = proto.Mesh;
            hash.Add(mesh.IsValid() ? mesh.InstanceID : 0);
            hash.Add(mesh.IsValid() && mesh.IsLoaded ? mesh.bounds.GetHashCode() : 0);
        }
        foreach (TreeInstance tree in data.Trees)
        {
            hash.Add(tree.Position);
            hash.Add(tree.PrototypeIndex);
            hash.Add(tree.Rotation);
            hash.Add(tree.WidthScale);
            hash.Add(tree.HeightScale);
            hash.Add(tree.Tint);
        }
        return hash.ToHashCode();
    }

    private static Instances[] Place(TerrainData data, Float4x4 terrainToWorld)
    {
        float terrainSize = data.Size;
        var result = new List<Instances>();
        var transforms = new List<Float4x4>();
        var tints = new List<Float4>();
        var locals = new List<Float2>();

        for (int protoIdx = 0; protoIdx < data.TreePrototypes.Count; protoIdx++)
        {
            Mesh? mesh = data.TreePrototypes[protoIdx].Mesh;
            if (mesh.IsNotValid() || !mesh.IsLoaded) continue;

            transforms.Clear();
            tints.Clear();
            locals.Clear();
            foreach (TreeInstance tree in data.Trees)
            {
                if (tree.PrototypeIndex != protoIdx) continue;
                if (data.IsHoleAt(tree.Position.X, tree.Position.Y)) continue;

                // Position in terrain-local space
                float lx = tree.Position.X * terrainSize;
                float lz = tree.Position.Y * terrainSize;
                float ly = data.GetInterpolatedHeight(tree.Position.X, tree.Position.Y);

                Float4x4 localTransform = Float4x4.CreateTranslation(new Float3(lx, ly, lz))
                    * Float4x4.FromAxisAngle(new Float3(0, 1, 0), tree.Rotation)
                    * Float4x4.CreateScale(new Float3(tree.WidthScale, tree.HeightScale, tree.WidthScale));

                transforms.Add(terrainToWorld * localTransform);
                tints.Add(new Float4(tree.Tint.R, tree.Tint.G, tree.Tint.B, tree.Tint.A));
                locals.Add(new Float2(lx, lz));
            }
            if (transforms.Count == 0) continue;

            AABB bounds = mesh.bounds;
            Float3 meshCentre = (bounds.Min + bounds.Max) * 0.5f;
            float meshRadius = Float3.Length(bounds.Max - bounds.Min) * 0.5f;

            var trees = new Instances
            {
                Prototype = protoIdx,
                Transforms = new Double4x4[transforms.Count],
                Tints = [.. tints],
                Local = [.. locals],
                Centres = new Double3[transforms.Count],
                Radii = new float[transforms.Count],
            };
            for (int i = 0; i < transforms.Count; i++)
            {
                trees.Transforms[i] = (Double4x4)transforms[i];
                trees.Centres[i] = Float4x4.TransformPoint(meshCentre, transforms[i]);
                trees.Radii[i] = meshRadius * TerrainComponent.MaxScale(transforms[i]);
            }
            result.Add(trees);
        }
        return [.. result];
    }
}
