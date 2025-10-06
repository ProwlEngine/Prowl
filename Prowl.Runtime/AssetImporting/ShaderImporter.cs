using System;
using System.IO;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.AssetImporting
{
    public class ShaderImporter
    {
        public Shader Import(FileInfo assetPath) =>
            ImportShader(assetPath.FullName, File.ReadAllText(assetPath.FullName), null);

        public Shader Import(Stream stream, string virtualPath)
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
            return ImportShader(virtualPath, reader.ReadToEnd(), ResolveEmbeddedInclude);
        }

        private Shader ImportShader(string path, string script, Func<string, string?>? includeResolver)
        {
            if (ShaderParser.ParseShader(path, script, includeResolver, out var shader))
                return shader;

            throw new Exception($"Failed to parse shader {path}. Please check the syntax and try again.");
        }

        private string? ResolveEmbeddedInclude(string includePath)
        {
            try
            {
                // Try to convert include path to DefaultShaderInclude enum
                if (Shader.TryGetDefaultInclude(includePath, out var includeEnum))
                {
                    return Shader.LoadDefaultInclude(includeEnum);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
