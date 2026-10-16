using System.IO;

using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary> Imports .shader files into runtime Shader assets, with their project includes inlined. </summary>
[ImporterFor(".shader")]
public class ShaderImporter : AssetImporter
{
    public override int Version => 2;

    public override bool Import(ImportContext ctx)
    {
        string dir = Path.GetDirectoryName(ctx.AbsolutePath) ?? "";
        string source = Shader.ExpandIncludes(File.ReadAllText(ctx.AbsolutePath), path => ResolveInclude(dir, path));

        Shader shader = Shader.FromSource(source, ctx.AbsolutePath);
        if (shader.Error != null)
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
    /// engine includes.
    /// </summary>
    internal static string? ResolveInclude(string dir, string includePath)
        => Find(dir, includePath) ?? (Path.HasExtension(includePath) ? null : Find(dir, includePath + ".glsl"));

    private static string? Find(string dir, string includePath)
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
            return EmbeddedResources.ReadAllText($"Assets/Defaults/Shaders/{fileName}");
        }
        catch
        {
            return null;
        }
    }
}

/// <summary> Imports .compute files into ComputeShader assets, with their includes resolved like a shader's. </summary>
[ImporterFor(".compute")]
public class ComputeShaderImporter : AssetImporter
{
    public override int Version => 2;

    public override bool Import(ImportContext ctx)
    {
        string dir = Path.GetDirectoryName(ctx.AbsolutePath) ?? "";
        string source = Shader.ExpandIncludes(File.ReadAllText(ctx.AbsolutePath), path => ShaderImporter.ResolveInclude(dir, path));

        try
        {
            ctx.SetMainAsset(ComputeShader.FromSource(ctx.FileName, source));
            return true;
        }
        catch (System.ArgumentException ex)
        {
            Debug.LogError($"Failed to import compute shader {ctx.AbsolutePath}: {ex.Message}");
            return false;
        }
    }
}
