using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

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
        foreach (var mesh in scene.Meshes)
        {
            foreach (var face in mesh.Faces)
            {
                if (face.Indices.Length == 3)
                {
                    (face.Indices[1], face.Indices[2]) = (face.Indices[2], face.Indices[1]);
                }
                else if (face.Indices.Length > 3)
                {
                    Array.Reverse(face.Indices);
                }
            }
        }
        _ = context;
    }
}
