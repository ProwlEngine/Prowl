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

    }
}
