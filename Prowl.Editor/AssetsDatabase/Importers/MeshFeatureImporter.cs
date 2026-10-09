using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    /// Generate every enabled feature for each mesh and attach them as sub-assets.
    /// Sub-asset names are <c>{meshName}_{featureKey}</c> for deterministic GUIDs
    /// across reimports.
    /// </summary>
    /// <param name="ownerIdentities">The sub-asset identity of each mesh, so each feature keys off its owner
    /// plus its own spec key. Registration order would not do here: a feature that stops generating shifts
    /// every later one onto the wrong GUID.</param>
    public static void GenerateAll(IReadOnlyList<Mesh> meshes, EchoObject? settings, ImportContext ctx, IReadOnlyList<string> ownerIdentities)
    {
        var specs = MeshFeatureRegistry.Specs.ToArray();

        // Meshes generate side by side, then register in order so the import stays deterministic
        var features = new Asset?[meshes.Count, specs.Length];
        Parallel.For(0, meshes.Count, m =>
        {
            for (int s = 0; s < specs.Length; s++)
            {
                try
                {
                    features[m, s] = specs[s].TryGenerate(meshes[m], settings);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Mesh feature '{specs[s].Key}' generation failed for {meshes[m].Name}: {ex.Message}");
                }
            }
        });

        for (int m = 0; m < meshes.Count; m++)
        {
            string meshName = string.IsNullOrEmpty(meshes[m].Name) ? "Mesh" : meshes[m].Name;
            for (int s = 0; s < specs.Length; s++)
                if (features[m, s] is Asset feature)
                    ctx.AddSubAsset($"{meshName}_{specs[s].Key}", feature, SubAssetIdentity.Key($"{ownerIdentities[m]}/{specs[s].Key}"));
        }
    }
}
