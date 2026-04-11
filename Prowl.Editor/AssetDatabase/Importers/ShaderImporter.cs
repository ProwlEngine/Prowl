using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;

namespace Prowl.Editor.Importers;

[ImporterFor(".shader")]
public class ShaderImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        string source = File.ReadAllText(ctx.AbsolutePath);
        string dir = Path.GetDirectoryName(ctx.AbsolutePath) ?? "";

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

        if (!ShaderParser.ParseShader(ctx.AbsolutePath, source, IncludeResolver, out var shader) || shader == null)
        {
            Debug.LogError($"Failed to parse shader: {ctx.AbsolutePath}");
            return false;
        }

        shader.Name = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
        ctx.SetMainAsset(shader);
        return true;
    }
}
