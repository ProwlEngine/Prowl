using System;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;

namespace Prowl.Editor.Importers;

[ImporterFor(".shader")]
public class ShaderImporter : AssetImporter
{
    public override int Version => 1;

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        string source = File.ReadAllText(absolutePath);
        string dir = Path.GetDirectoryName(absolutePath) ?? "";

        // Resolve #include directives relative to the shader file's directory
        string? IncludeResolver(string includePath)
        {
            string fullPath = Path.Combine(dir, includePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);

            // Try Assets root
            if (Project.Current != null)
            {
                string assetsPath = Path.Combine(Project.Current.AssetsPath, includePath);
                if (File.Exists(assetsPath))
                    return File.ReadAllText(assetsPath);
            }

            return null;
        }

        if (!ShaderParser.ParseShader(absolutePath, source, IncludeResolver, out var shader) || shader == null)
        {
            Debug.LogError($"Failed to parse shader: {absolutePath}");
            return new ImportResult();
        }

        shader.Name = Path.GetFileNameWithoutExtension(absolutePath);
        return new ImportResult { MainAsset = shader };
    }
}
