using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Photonic.Raytracing;

namespace Prowl.Photonic.Integration;

/// <summary>
/// The ray-tracing acceleration structure + pre-resolved material tables for a bake. Shared by the
/// per-frame <see cref="Job"/> (lightmap texels) and <see cref="LightmapBaker.BakeProbes"/> (light
/// probes) so both trace against an identical scene.
/// </summary>
/// <remarks>
/// Baking is static (nothing moves) and every surface is baked uniquely, so there's no need for a
/// two-level TLAS-over-instances structure. Instead we bake all instances' triangles into a single
/// <b>world-space</b> <see cref="Raytracing.Blas"/> — one tight triangle-level BVH with no per-ray
/// instance transform and no instance-AABB culling to defeat on overlapping geometry. Per-hit shading
/// reads world position/normal/UV0 straight off the merged mesh; the merged material groups map 1:1
/// to <see cref="MergedMats"/>.
/// <para>The rasterizer keys texels by the source <see cref="BakeInstance"/> + its material group, so
/// <see cref="InstanceMaterials"/> keeps per-instance material resolution for that path.</para>
/// </remarks>
internal sealed class BakeAcceleration
{
    /// <summary>Single merged world-space BLAS over every instance's triangles.</summary>
    public required Blas Blas;

    /// <summary>Resolved material per merged material group (indexed by <c>TriRef.MaterialGroupIndex</c>).</summary>
    public required BakeMaterial?[] MergedMats;

    /// <summary>Resolved materials per instance: <c>InstanceMaterials[instanceIndex][materialGroupIndex]</c>. Used by the rasterizer/texel path.</summary>
    public required BakeMaterial?[][] InstanceMaterials;

    /// <summary>Canonical instance array (used by the rasterizer, which is independent of the BLAS).</summary>
    public required BakeInstance[] Instances;

    public static BakeAcceleration Build(BakeScene scene, BakeInstance[] instances)
    {
        // Per-instance material resolution: the rasterizer tags each texel with (instanceIndex,
        // materialGroupIndex) into the instance's own mesh, so resolve materials per instance.
        var instanceMaterials = new BakeMaterial?[instances.Length][];
        for (int i = 0; i < instances.Length; i++)
        {
            var groups = instances[i].Mesh.MaterialGroups;
            var arr = new BakeMaterial?[groups.Count];
            for (int g = 0; g < arr.Length; g++) arr[g] = scene.FindMaterial(groups[g].MaterialName);
            instanceMaterials[i] = arr;
        }

        // Merge every instance's triangles into one world-space mesh. Positions + normals are baked to
        // world; each material group is re-added with indices offset to the instance's vertex base.
        var positions = new List<Float3>();
        var normals = new List<Float3>();   // world-space, NOT pre-normalised (barycentric result is normalised at hit time)
        var uv0 = new List<Float2>();
        var mergedGroups = new List<BakeMesh.MaterialGroup>();
        var mergedMats = new List<BakeMaterial?>();

        foreach (var inst in instances)
        {
            var mesh = inst.Mesh;
            var w = inst.WorldTransform;
            int baseV = positions.Count;

            var srcPos = mesh.Positions;
            var srcNrm = mesh.Normals;
            mesh.UVLayers.TryGetValue("UV0", out var srcUV0);
            for (int v = 0; v < srcPos.Length; v++)
            {
                positions.Add(RayMath.Transform(w, srcPos[v], 1f));
                normals.Add(RayMath.Transform(w, v < srcNrm.Length ? srcNrm[v] : Float3.Zero, 0f));
                uv0.Add(srcUV0 != null && v < srcUV0.Length ? srcUV0[v] : Float2.Zero);
            }

            var groups = mesh.MaterialGroups;
            for (int g = 0; g < groups.Count; g++)
            {
                var src = groups[g].Indices;
                var remapped = new int[src.Length];
                for (int k = 0; k < src.Length; k++) remapped[k] = src[k] + baseV;
                mergedGroups.Add(new BakeMesh.MaterialGroup(groups[g].MaterialName, remapped));
                mergedMats.Add(scene.FindMaterial(groups[g].MaterialName));
            }
        }

        // Construct the merged mesh directly (not via scene.BeginMesh) so it isn't registered on the
        // scene — Build runs once for the lightmap job and again for probes.
        var uvLayers = new Dictionary<string, Float2[]> { ["UV0"] = uv0.ToArray() };
        var merged = new BakeMesh("__merged_bake__", positions.ToArray(), normals.ToArray(), uvLayers, mergedGroups);

        var blas = new Blas(merged);
        blas.Build();

        return new BakeAcceleration
        {
            Blas = blas,
            MergedMats = mergedMats.ToArray(),
            InstanceMaterials = instanceMaterials,
            Instances = instances,
        };
    }
}
