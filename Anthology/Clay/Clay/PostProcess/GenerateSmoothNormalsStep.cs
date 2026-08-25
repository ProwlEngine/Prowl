// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Generates angle-weighted smooth normals for meshes that arrived without any, splitting vertices
/// across edges sharper than <see cref="ModelImporterSettings.SmoothNormalsAngleDeg"/>.
/// </summary>
internal sealed class GenerateSmoothNormalsStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.GenerateSmoothNormals;
    public string Name => "GenerateSmoothNormals";

    public void Execute(IntermediateScene scene, ImportContext context)
        => NormalGenerationStepRunner.Run(scene, context, context.Settings.SmoothNormalsAngleDeg, Name);
}

/// <summary>Shared body of the two normal-generation steps.</summary>
internal static class NormalGenerationStepRunner
{
    public static void Run(IntermediateScene scene, ImportContext context, float angleDeg, string stepName)
    {
        int generated = 0;
        int split = 0;

        foreach (var mesh in scene.Meshes)
        {
            // Authored normals win. A source that shipped them knows its own smoothing groups, and
            // reconstructing them from geometry alone would throw that away.
            if (mesh.Normals is { Count: > 0 } && !context.Settings.RecalculateNormals)
                continue;

            if ((mesh.PrimitiveKinds & PrimitiveKind.Triangle) == 0)
                continue;

            split += NormalGeneration.Generate(mesh, angleDeg);
            generated++;
        }

        if (generated == 0)
            return;

        context.Log.Info(
            split > 0
                ? $"Generated normals for {generated} mesh(es); {split} vertex/vertices split on edges sharper than {angleDeg:0.#} degrees."
                : $"Generated normals for {generated} mesh(es).",
            stepName);
    }
}
