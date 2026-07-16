using System;
using System.IO;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

using Prowl.Runtime;
using Prowl.Editor.Projects;


namespace Prowl.Editor;


/// <summary>
/// Compiles a parsed <see cref="ShaderDefinition"/> against the running editor's device (Vulkan only -
/// that is the only backend Graphite exposes). Synchronous: the Slang compiler is not reentrant across
/// concurrent sessions, and shader imports are not hot-path enough to need a background queue.
/// </summary>
public static class CompilationWorker
{
    public static ShaderSnapshot CompileAll(ShaderDefinition definition, string moduleName, string modulePath)
    {
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(new VulkanCompiler("spirv_1_4"));

        try
        {
            compiler.BeginSession([new DirectoryInfo("/")], IncludeResolver);
            definition.Create(Graphics.Device, compiler, CompileMode.All);
            return definition.Snapshot();
        }
        catch (Exception ex)
        {
            Debug.LogException(new Exception($"Failed to compile shader '{moduleName}' ({modulePath}): {ex.Message}", ex));
            throw;
        }
        finally
        {
            compiler.EndSession();
        }
    }


    private static Memory<byte>? IncludeResolver(string includePath)
    {
        // Do this for
        // a. safety reasons so a shader can't include some random system file
        // b. no crazy person can load a shader outside their project.
        if (Project.Current != null)
        {
            string assetsRoot = Path.GetFullPath(Project.Current.AssetsPath);
            string candidate = Path.GetFullPath(
                Path.Combine(assetsRoot, includePath));

            if (candidate.StartsWith(
                    assetsRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    candidate,
                    assetsRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                    return File.ReadAllBytes(candidate);
            }
        }

        string fileName = Path.GetFileName(includePath);
        try
        {
            return Runtime.Resources.EmbeddedResources.ReadAllBytes($"Assets/Defaults/{fileName}");
        }
        catch
        {
            return null;
        }
    }
}
