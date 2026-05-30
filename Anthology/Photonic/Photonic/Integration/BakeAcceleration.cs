using Prowl.Vector;
using Prowl.Photonic.Raytracing;

namespace Prowl.Photonic.Integration;

/// <summary>
/// The ray-tracing acceleration structures + pre-resolved material tables for a bake. Shared by
/// the per-frame <see cref="Job"/> (lightmap texels) and <see cref="LightmapBaker.BakeProbes"/>
/// (light probes), so both trace against an identical scene.
/// </summary>
/// <remarks>
/// <see cref="Instances"/> is the canonical instance array: a hit's <c>InstanceIndex</c> indexes
/// into it, and <see cref="InstanceToBlas"/> / the TLAS instance refs are built against the same
/// ordering. Pass this exact array to <see cref="PathIntegrator"/>.
/// </remarks>
internal sealed class BakeAcceleration
{
    public required Tlas Tlas;
    public required BakeInstance[] Instances;
    public required Blas[] Blas;
    public required int[] InstanceToBlas;
    public required BakeMaterial?[][] ResolvedMats;

    public static BakeAcceleration Build(BakeScene scene, BakeInstance[] instances)
    {
        // 1) One BLAS per unique mesh (instances may share meshes).
        var meshToBlas = new System.Collections.Generic.Dictionary<BakeMesh, int>(
            System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var blasList = new System.Collections.Generic.List<Blas>();
        foreach (var inst in instances)
        {
            if (!meshToBlas.ContainsKey(inst.Mesh))
            {
                meshToBlas[inst.Mesh] = blasList.Count;
                var b = new Blas(inst.Mesh);
                b.Build();
                blasList.Add(b);
            }
        }

        var blas = blasList.ToArray();
        var instanceToBlas = new int[instances.Length];
        for (int i = 0; i < instances.Length; i++) instanceToBlas[i] = meshToBlas[instances[i].Mesh];

        // 2) Pre-resolve materials per (BLAS, material-group) to keep the hot path free of dictionary hits.
        var resolvedMats = new BakeMaterial?[blas.Length][];
        for (int bi = 0; bi < blas.Length; bi++)
        {
            var groups = blas[bi].Mesh.MaterialGroups;
            var arr = new BakeMaterial?[groups.Count];
            for (int g = 0; g < arr.Length; g++) arr[g] = scene.FindMaterial(groups[g].MaterialName);
            resolvedMats[bi] = arr;
        }

        // 3) TLAS over instances.
        var refs = new Tlas.InstanceRef[instances.Length];
        for (int i = 0; i < instances.Length; i++)
        {
            var w = instances[i].WorldTransform;
            if (!Float4x4.Invert(w, out var inv)) inv = Float4x4.Identity;
            refs[i] = new Tlas.InstanceRef
            {
                InstanceIndex = i,
                BlasIndex = instanceToBlas[i],
                WorldFromLocal = w,
                LocalFromWorld = inv,
                IsIdentity = IsIdentityTransform(w),
            };
        }
        var tlas = new Tlas();
        tlas.Build(refs, blas);

        return new BakeAcceleration
        {
            Tlas = tlas,
            Instances = instances,
            Blas = blas,
            InstanceToBlas = instanceToBlas,
            ResolvedMats = resolvedMats,
        };
    }

    internal static bool IsIdentityTransform(Float4x4 m)
    {
        const float E = 1e-6f;
        return System.Math.Abs(m.c0.X - 1) < E && System.Math.Abs(m.c0.Y) < E && System.Math.Abs(m.c0.Z) < E && System.Math.Abs(m.c0.W) < E
            && System.Math.Abs(m.c1.X) < E && System.Math.Abs(m.c1.Y - 1) < E && System.Math.Abs(m.c1.Z) < E && System.Math.Abs(m.c1.W) < E
            && System.Math.Abs(m.c2.X) < E && System.Math.Abs(m.c2.Y) < E && System.Math.Abs(m.c2.Z - 1) < E && System.Math.Abs(m.c2.W) < E
            && System.Math.Abs(m.c3.X) < E && System.Math.Abs(m.c3.Y) < E && System.Math.Abs(m.c3.Z) < E && System.Math.Abs(m.c3.W - 1) < E;
    }
}
