// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Assets;

[Importer("ShaderIcon.png", typeof(ComputeShader), ".compute")]
public class ComputeImporter : ScriptedImporter
{
    public static readonly string[] Supported = [".compute"];

    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        string shaderScript = File.ReadAllText(assetPath.FullName);

        string? relPath = AssetDatabase.GetRelativePath(assetPath.FullName);

        if (relPath == null)
        {
            Debug.LogError("Could not find relative shader path.");
            return;
        }

        DirectoryInfo[] dirs = [];

        if (relPath.StartsWith(Project.Active.AssetDirectory.Name))
            dirs = [Project.Active.AssetDirectory, Project.Active.DefaultsDirectory, Project.Active.PackagesDirectory];
        else if (relPath.StartsWith(Project.Active.DefaultsDirectory.Name))
            dirs = [Project.Active.DefaultsDirectory, Project.Active.AssetDirectory, Project.Active.PackagesDirectory];
        else if (relPath.StartsWith(Project.Active.PackagesDirectory.Name))
            dirs = [Project.Active.PackagesDirectory, Project.Active.AssetDirectory, Project.Active.DefaultsDirectory];

        relPath = relPath.Substring(relPath.IndexOf(Path.DirectorySeparatorChar));

        FileIncluder includer = new FileIncluder(relPath, dirs);

        if (!ComputeParser.ParseShader(assetPath.Name, shaderScript, includer, out ComputeShader? shader))
            return;

        ctx.SetMainObject(shader);
    }
}
