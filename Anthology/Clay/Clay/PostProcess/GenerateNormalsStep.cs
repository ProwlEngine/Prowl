// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Generates flat, per-face normals for meshes that arrived without any.
/// </summary>
/// <remarks>
/// Flat shading is smoothing with a threshold of zero: only exactly coplanar adjacent faces share a
/// normal, everything else splits. That keeps the vertex growth to the edges that actually need it
/// rather than unindexing the mesh outright.
/// <para>
/// Runs after <see cref="GenerateSmoothNormalsStep"/> in the canonical order, so if both flags are
/// set the smooth normals are already in place and this step finds nothing to do.
/// </para>
/// </remarks>
internal sealed class GenerateNormalsStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.GenerateNormals;
    public string Name => "GenerateNormals";

    public void Execute(IntermediateScene scene, ImportContext context)
        => NormalGenerationStepRunner.Run(scene, context, angleDeg: 0f, Name);
}
