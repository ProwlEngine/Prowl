using System;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Bridge between the runtime <see cref="MeshFeatureRegistry"/> and the editor
/// <see cref="ImportContext"/>. Walks every registered feature spec, asks it to
/// generate a feature for the mesh, and registers the result as a read-only sub-asset.
/// </summary>
public static class MeshFeatureImporter
{
    /// <summary>
    /// Generate every enabled feature for the mesh and attach them as sub-assets.
    /// Sub-asset names are <c>{meshName}_{featureKey}</c> for deterministic GUIDs
    /// across reimports.
    /// </summary>
    /// <param name="ownerIdentity">The sub-asset identity of the mesh these features belong to, so each
    /// feature keys off its owner plus its own spec key. Registration order would not do here: a feature
    /// that stops generating shifts every later one onto the wrong GUID.</param>
    public static void GenerateAll(Mesh mesh, EchoObject? settings, ImportContext ctx, string ownerIdentity)
    {
        foreach (var spec in MeshFeatureRegistry.Specs)
        {
            EngineObject? feature;
            try
            {
                feature = spec.TryGenerate(mesh, settings);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Mesh feature '{spec.Key}' generation failed for {mesh.Name}: {ex.Message}");
                continue;
            }

            if (feature == null) continue;

            string meshName = string.IsNullOrEmpty(mesh.Name) ? "Mesh" : mesh.Name;
            ctx.AddSubAsset($"{meshName}_{spec.Key}", feature, SubAssetIdentity.Key($"{ownerIdentity}/{spec.Key}"));
        }
    }
}
