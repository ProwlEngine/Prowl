using System.IO;

using Prowl.Editor.Projects;
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

        // Resolve #include directives:
        // 1. Relative to the shader file's directory
        // 2. Relative to the project's Assets root
        // 3. Built-in engine includes (Fragment.glsl, PBR.glsl, Lighting.glsl, etc.)
        string? IncludeResolver(string includePath)
        {
            // 1. Relative to shader file
            string fullPath = Path.Combine(dir, includePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);

            // 2. Relative to Assets root
            if (Project.Current != null)
            {
                string assetsPath = Path.Combine(Project.Current.AssetsPath, includePath);
                if (File.Exists(assetsPath))
                    return File.ReadAllText(assetsPath);
            }

            // 3. Built-in engine includes (embedded resources)
            // The includePath may be a full absolute path like "C:/.../Assets/Fragment.glsl"
            // Extract just the filename and try loading from the built-in defaults
            string fileName = Path.GetFileName(includePath);
            try
            {
                return Runtime.Resources.EmbeddedResources.ReadAllText($"Assets/Defaults/{fileName}");
            }
            catch
            {
                return null;
            }
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
