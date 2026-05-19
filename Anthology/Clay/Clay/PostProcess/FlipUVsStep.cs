using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Flips the V coordinate on every UV channel: <c>V := 1 - V</c>. Bitangent signs in
/// <see cref="IntermediateMesh.Tangents"/> are negated to keep the tangent frame consistent.
/// </summary>
/// <remarks>
/// Use when the source's UV origin convention doesn't match the engine's. glTF authors V starting
/// at the top, OBJ and FBX usually at the bottom - the realtime preset enables this for glTF.
/// </remarks>
internal sealed class FlipUVsStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.FlipUVs;
    public string Name => "FlipUVs";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var mesh in scene.Meshes)
        {
            for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            {
                if (mesh.UVs[uv] is not { } list) continue;
                for (int i = 0; i < list.Count; i++)
                    list[i] = new Float2(list[i].X, 1f - list[i].Y);
            }

            if (mesh.Tangents is { } tangents)
            {
                for (int i = 0; i < tangents.Count; i++)
                {
                    var t = tangents[i];
                    tangents[i] = new Float4(t.X, t.Y, t.Z, -t.W);
                }
            }
        }
        _ = context;
    }
}
