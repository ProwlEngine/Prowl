// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;
using System.Linq;

using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;

namespace Prowl.Editor.Importers;

/// <summary>
/// Tracks assembly definition files (.asmdef). The file (consumed by the compiler) is the source of
/// truth; this importer just produces a lightweight marker asset so the .asmdef gets the normal
/// type-keyed inspector. A freshly created (empty) asmdef is seeded with a default named after the
/// file. Changes trigger a recompile. The asset is editor-only and never shipped.
/// </summary>
[ImporterFor(AssemblyDefinitionDatabase.Extension)]
public class AssemblyDefinitionImporter : AssetImporter
{
    public override int Version => 1;

    public override bool IsEditorOnlyAsset => true;

    public override bool Import(ImportContext ctx)
    {
        // Seed a newly created (empty) asmdef with a sensible default.
        if (File.Exists(ctx.AbsolutePath) && new FileInfo(ctx.AbsolutePath).Length == 0)
        {
            var def = new AssemblyDefinition { Name = SanitizeName(Path.GetFileNameWithoutExtension(ctx.AbsolutePath)) };
            def.WriteToFile(ctx.AbsolutePath);
        }

        if (Project.Current != null)
            ScriptAssemblyManager.RequestRecompile();

        // Marker asset so the inspector resolves the editor by type (data stays in the file).
        ctx.SetMainAsset(new AssemblyDefinitionAsset { Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath) });
        return true;
    }

    private static string SanitizeName(string raw)
    {
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_').ToArray();
        string name = new string(chars).Trim('.', '_');
        return string.IsNullOrEmpty(name) ? "NewAssembly" : name;
    }
}
