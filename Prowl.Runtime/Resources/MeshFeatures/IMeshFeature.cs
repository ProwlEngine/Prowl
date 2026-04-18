// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Prowl.Runtime.MeshFeatures;

/// <summary>
/// Marker interface for generated data that extends a <see cref="Prowl.Runtime.Resources.Mesh"/>.
/// Implementations (SDF, BVH, Prism shells, ...) are produced by the asset pipeline
/// from a source mesh and stored as read-only sub-assets of the mesh's parent asset.
/// </summary>
/// <remarks>
/// Mesh features are never modified after generation. To change a feature, change the
/// parent asset's importer settings and reimport — the feature is regenerated.
/// </remarks>
public interface IMeshFeature : ISerializable
{
}
