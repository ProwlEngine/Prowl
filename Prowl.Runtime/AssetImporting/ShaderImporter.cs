using System;
using System.IO;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.AssetImporting
{
    public class ShaderImporter
    {
        public Shader Import(FileInfo assetPath)
        {
            string shaderScript = File.ReadAllText(assetPath.FullName);

            if(ShaderParser.ParseShader(assetPath.FullName, shaderScript, out var shader))
                return shader;

            throw new Exception($"Failed to parse shader {assetPath.FullName}. Please check the syntax and try again.");
        }

        public Shader Import(Stream stream, string virtualPath)
        {
            string shaderScript;
            using (StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true))
            {
                shaderScript = reader.ReadToEnd();
            }

            // Create an include resolver that loads from embedded resources
            // Note: virtualPath doesn't have $ prefix - it was stripped by BasicAssetProvider
            string? IncludeResolver(string includePath)
            {
                try
                {
                    var provider = Game.AssetProvider as BasicAssetProvider;
                    if (provider == null)
                        return null;

                    // includePath is like "Assets/Defaults/Fragment.glsl" (no $ prefix)
                    // We need to check with $ prefix for HasAsset
                    string assetPath = "$" + includePath;
                    if (!Game.AssetProvider.HasAsset(assetPath))
                        return null;

                    // GetEmbeddedResourceStream expects path without $
                    using (var includeStream = provider.GetEmbeddedResourceStream(includePath))
                    using (var reader = new StreamReader(includeStream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch
                {
                    return null;
                }
            }

            if(ShaderParser.ParseShader(virtualPath, shaderScript, IncludeResolver, out var shader))
                return shader;

            throw new Exception($"Failed to parse shader {virtualPath}. Please check the syntax and try again.");
        }

    }
}
