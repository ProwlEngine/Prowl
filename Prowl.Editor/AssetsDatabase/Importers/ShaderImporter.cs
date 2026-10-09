using System.IO;

using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;

namespace Prowl.Editor.Importers;

/// <summary> Imports .shader files into runtime Shader assets, parsing GLSL source and resolving #include directives. </summary>
[ImporterFor(".shader")]
public class ShaderImporter : AssetImporter
{
    public override int Version => 1;

    /// <summary> Parses the shader source file, resolves #include directives from the file directory, Assets root, and built-in engine defaults, then sets the resulting Shader as the main asset. </summary>
    public override bool Import(ImportContext ctx)
    {
        string source = File.ReadAllText(ctx.AbsolutePath);
        string dir = Path.GetDirectoryName(ctx.AbsolutePath) ?? "";

        if (!ShaderParser.ParseShader(ctx.AbsolutePath, source, path => ResolveInclude(dir, path), out var shader) || shader == null)
        {
            Debug.LogError($"Failed to parse shader: {ctx.AbsolutePath}");
            return false;
        }

        shader.Name = ctx.FileName;
        ctx.SetMainAsset(shader);
        return true;
    }

    /// <summary>
    /// Finds an included file: beside the shader first, then from the project's Assets root, then among the built-in
    /// engine includes (ProwlCG.glsl, PBR.glsl, Lighting.glsl and so on).
    /// </summary>
    internal static string? ResolveInclude(string dir, string includePath)
    {
        string fullPath = Path.Combine(dir, includePath);
        if (File.Exists(fullPath))
            return File.ReadAllText(fullPath);

        if (Project.Current != null)
        {
            string assetsPath = Path.Combine(Project.Current.AssetsPath, includePath);
            if (File.Exists(assetsPath))
                return File.ReadAllText(assetsPath);
        }

        // The path may be a full absolute path like "C:/.../Assets/Fragment.glsl", so only its file name is tried here
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
}
}
