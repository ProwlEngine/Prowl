namespace Prowl.Runtime.CSG;

/// <summary>
/// A component on a <see cref="CSGModel"/>'s GameObject that edits the meshes each build produces, before the model
/// applies them. It may rewrite UVs, colours and geometry or swap materials. Keep it deterministic, since an unchanged
/// result lets the model keep the meshes it already has.
/// </summary>
public interface ICSGMeshProcessor
{
    void ProcessMeshes(CSGModel model, CSGMeshes meshes);
}
