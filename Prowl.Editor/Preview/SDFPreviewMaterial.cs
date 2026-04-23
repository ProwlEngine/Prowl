using System;

using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Preview;

/// <summary>
/// Builds a ready-to-render <see cref="Material"/> that raymarches a <see cref="MeshSDF"/>
/// in the fragment shader. The caller is responsible for placing/sizing a proxy cube mesh
/// that matches <see cref="MeshSDF.Bounds"/>.
/// </summary>
internal static class SDFPreviewMaterial
{
    public static Material Create(MeshSDF sdf)
    {
        var mat = new Material(Shader.LoadDefault(DefaultShader.SDFRaymarch));
        Apply(mat, sdf);
        return mat;
    }

    public static void Apply(Material mat, MeshSDF sdf)
    {
        if (sdf.Volume == null) return;

        var size = sdf.Bounds.Max - sdf.Bounds.Min;
        var voxel = new Float3(
            size.X / MathF.Max(1, sdf.Resolution.X),
            size.Y / MathF.Max(1, sdf.Resolution.Y),
            size.Z / MathF.Max(1, sdf.Resolution.Z));

        mat.SetTexture3D("_SDF", sdf.Volume);
        mat.SetVector("_BoundsMin", sdf.Bounds.Min);
        mat.SetVector("_BoundsMax", sdf.Bounds.Max);
        mat.SetVector("_VoxelSize", voxel);
        // Surface epsilon scaled to voxel size keeps stepping tolerance consistent across resolutions.
        mat.SetFloat("_SurfaceEpsilon", MathF.Max(0.00005f, MathF.Min(voxel.X, MathF.Min(voxel.Y, voxel.Z)) * 0.25f));
        mat.SetFloat("_StepScale", 0.9f);
        mat.SetFloat("_MaxSteps", 192f);
        mat.SetFloat("_AmbientStrength", 0.2f);
    }
}
