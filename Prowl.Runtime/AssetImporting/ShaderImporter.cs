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
                if (Game.AssetProvider is not BasicAssetProvider provider)
                    return null;

                string assetPath = "$" + includePath;
                if (!Game.AssetProvider.HasAsset(assetPath))
                    return null;

                using var stream = provider.GetEmbeddedResourceStream(includePath);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }
    }
}
