// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Importers;

/// <summary> Imports an Echo-serialized EngineObject whose type carries CreateAssetMenuAttribute but has no dedicated importer. The concrete type is read from the file itself, so a single importer handles all custom asset types. </summary>
public class CustomAssetImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx) => ImportHelper.ImportEchoObject(ctx, "custom asset");
}
