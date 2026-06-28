using System;
using System.IO;
using System.Linq;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor;

/// <summary>
/// Maps engine default asset source files (those shipped under a "Defaults" folder) to the
/// deterministic GUIDs that <see cref="BuiltInAssets"/> generates for them. This is what lets
/// <c>Shader.LoadDefault</c> resolve a default shader by its <see cref="DefaultShader"/> GUID:
/// the file is imported like any other asset, but its .meta is forced to the deterministic GUID
/// instead of a random one.
/// </summary>
public static class DefaultAssetGuid
{
    public static bool TryGet(string relativePath, out Guid guid)
    {
        guid = Guid.Empty;

        string norm = relativePath.Replace('\\', '/');

        if (!norm.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!norm.Split('/').Any(seg => seg.Equals("Defaults", StringComparison.OrdinalIgnoreCase)))
            return false;

        string name = Path.GetFileNameWithoutExtension(norm);

        if (!Enum.TryParse(name, out DefaultShader shader) || !Enum.IsDefined(shader))
            return false;

        guid = BuiltInAssets.GuidFor(shader);
        return true;
    }
}
