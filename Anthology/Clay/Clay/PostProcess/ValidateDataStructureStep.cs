using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Cross-checks index references and per-vertex array sizes throughout the scene. Issues are
/// logged as warnings; if <see cref="ModelImporterSettings.StrictValidation"/> is enabled, any
/// warning is promoted to an <see cref="ImportException"/>.
/// </summary>
internal sealed class ValidateDataStructureStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.ValidateDataStructure;
    public string Name => "ValidateDataStructure";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        var report = new List<string>();

        ValidateMeshes(scene, report);
        ValidateMaterials(scene, report);
        ValidateSkins(scene, report);
        ValidateAnimations(scene, report);

        foreach (var msg in report)
            context.Log.Warning(msg, Name);

        if (report.Count > 0 && context.Settings.StrictValidation)
            throw new ImportException(
                $"Strict validation failed with {report.Count} issue(s); first: {report[0]}",
                context.SourcePath, context.Format);
    }

    private static void ValidateMeshes(IntermediateScene scene, List<string> r)
    {
        for (int mi = 0; mi < scene.Meshes.Count; mi++)
        {
            var mesh = scene.Meshes[mi];
            int vc = mesh.Positions.Count;

            if (mesh.Normals is { } n && n.Count != vc) r.Add($"Mesh {mi}: Normals count {n.Count} != vertex count {vc}.");
            if (mesh.Tangents is { } t && t.Count != vc) r.Add($"Mesh {mi}: Tangents count {t.Count} != vertex count {vc}.");
            if (mesh.Colors0 is { } c && c.Count != vc) r.Add($"Mesh {mi}: Colors count {c.Count} != vertex count {vc}.");
            for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
                if (mesh.UVs[uv] is { } u && u.Count != vc)
                    r.Add($"Mesh {mi}: UV{uv} count {u.Count} != vertex count {vc}.");

            if (mesh.VertexJoints is { } vj)
            {
                int expected = vc * mesh.MaxInfluencesPerVertex;
                if (vj.Length != expected)
                    r.Add($"Mesh {mi}: VertexJoints length {vj.Length} != expected {expected}.");
            }
            if (mesh.VertexWeights is { } vw)
            {
                int expected = vc * mesh.MaxInfluencesPerVertex;
                if (vw.Length != expected)
                    r.Add($"Mesh {mi}: VertexWeights length {vw.Length} != expected {expected}.");
            }

            // Face indices in range.
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                for (int k = 0; k < face.Indices.Length; k++)
                {
                    int idx = face.Indices[k];
                    if ((uint)idx >= (uint)vc)
                    {
                        r.Add($"Mesh {mi}, face {fi}: index {idx} out of range [0, {vc}).");
                        goto nextMesh;
                    }
                }
            }
            nextMesh:;

            // Material index in range or -1.
            if (mesh.MaterialIndex < -1 || mesh.MaterialIndex >= scene.Materials.Count)
                r.Add($"Mesh {mi}: MaterialIndex {mesh.MaterialIndex} out of range.");

            // Blend shapes must match vertex count.
            for (int bs = 0; bs < mesh.BlendShapes.Count; bs++)
            {
                foreach (var frame in mesh.BlendShapes[bs].Frames)
                {
                    if (frame.DeltaPositions.Length != vc)
                        r.Add($"Mesh {mi}, blend shape {bs}: delta positions length {frame.DeltaPositions.Length} != vertex count {vc}.");
                    if (frame.DeltaNormals is { Length: var dn } && dn != vc)
                        r.Add($"Mesh {mi}, blend shape {bs}: delta normals length {dn} != vertex count {vc}.");
                }
            }
        }
    }

    private static void ValidateMaterials(IntermediateScene scene, List<string> r)
    {
        for (int mi = 0; mi < scene.Materials.Count; mi++)
        {
            var m = scene.Materials[mi];
            CheckSlot(m.BaseColorTexture, scene, mi, "BaseColor", r);
            CheckSlot(m.MetallicRoughnessTexture, scene, mi, "MetallicRoughness", r);
            CheckSlot(m.NormalTexture, scene, mi, "Normal", r);
            CheckSlot(m.OcclusionTexture, scene, mi, "Occlusion", r);
            CheckSlot(m.EmissiveTexture, scene, mi, "Emissive", r);
        }
    }

    private static void CheckSlot(IntermediateTextureSlot? slot, IntermediateScene scene, int matIndex, string label, List<string> r)
    {
        if (slot is null) return;
        if ((uint)slot.TextureIndex >= (uint)scene.Textures.Count)
            r.Add($"Material {matIndex}: {label} texture index {slot.TextureIndex} out of range.");
    }

    private static void ValidateSkins(IntermediateScene scene, List<string> r)
    {
        for (int si = 0; si < scene.Skins.Count; si++)
        {
            var skin = scene.Skins[si];
            if (skin.BoneNodes.Count != skin.InverseBindPoses.Count)
                r.Add($"Skin {si}: BoneNodes count {skin.BoneNodes.Count} != InverseBindPoses count {skin.InverseBindPoses.Count}.");
        }
    }

    private static void ValidateAnimations(IntermediateScene scene, List<string> r)
    {
        for (int ai = 0; ai < scene.Animations.Count; ai++)
        {
            var anim = scene.Animations[ai];
            for (int bi = 0; bi < anim.Bindings.Count; bi++)
            {
                var b = anim.Bindings[bi];
                int valuesPerKey = b.Interpolation == AnimationInterpolation.CubicSpline
                    ? b.Dimension * 3
                    : b.Dimension;
                if (b.Values.Count != b.Times.Count * valuesPerKey)
                    r.Add($"Animation {ai}, binding {bi}: values count {b.Values.Count} != times {b.Times.Count} * stride {valuesPerKey}.");
            }
        }
    }
}
