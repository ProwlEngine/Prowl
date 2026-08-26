// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports an Echo-serialized <see cref="Prowl.Runtime.EngineObject"/> whose type carries
/// <see cref="Prowl.Runtime.CreateAssetMenuAttribute"/> but has no importer of its own.
///
/// Without this, creating a custom asset writes a file that nothing can read back: the extension
/// falls through to <see cref="DefaultImporter"/>, which tracks the file and produces no asset, so
/// the thing never loads and the inspector has nothing to show. The concrete type comes from the
/// file itself, so one importer serves every custom asset type.
/// </summary>
public class CustomAssetImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx) => ImportHelper.ImportEchoObject(ctx, "custom asset");
}
