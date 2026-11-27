// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Intermediate;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Reverses triangle winding order. Lines and points are untouched.
/// </summary>
internal sealed class FlipWindingOrderStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.FlipWindingOrder;
    public string Name => "FlipWindingOrder";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (IntermediateMesh mesh in scene.Meshes)
        {
            foreach (IntermediateFace face in mesh.Faces)
            {
                Span<int> indices = mesh.Faces.IndicesOf(face);
                if (indices.Length == 3)
                {
                    (indices[1], indices[2]) = (indices[2], indices[1]);
                }
                else if (indices.Length > 3)
                {
                    indices.Reverse();
                }
            }
        }
        _ = context;
    }
}
