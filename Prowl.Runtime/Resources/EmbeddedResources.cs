// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.Resources;

/// <summary>
/// Helper class for loading embedded resources from the Prowl.Runtime assembly
/// </summary>
internal static class EmbeddedResources
{
    private static readonly Assembly RuntimeAssembly = Assembly.GetExecutingAssembly();

    /// <summary>
    /// Gets an embedded resource stream by path
    /// </summary>
    public static Stream GetStream(string resourcePath)
    {
        if (!TryGetResourceName(resourcePath, out string? resourceName) || resourceName == null)
            throw new FileNotFoundException($"Embedded resource '{resourcePath}' not found.");

        return RuntimeAssembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Failed to load embedded resource '{resourceName}'.");
    }

    /// <summary>
    /// Reads an embedded resource as text
    /// </summary>
    public static string ReadAllText(string resourcePath)
    {
        using Stream stream = GetStream(resourcePath);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }


    /// <summary>
    /// Reads an embedded resource as text
    /// </summary>
    public static byte[] ReadAllBytes(string resourcePath)
    {
        using Stream stream = GetStream(resourcePath);
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Checks if an embedded resource exists
    /// </summary>
    public static bool Exists(string resourcePath)
    {
        return TryGetResourceName(resourcePath, out _);
    }

    /// <summary>
    /// Enumerates the file names of every resource embedded from the engine's Assets/Defaults
    /// folder. The folder is flat, so each returned name is the verbatim file name (including any
    /// spaces or dots, e.g. "Standard Terrain.mat"). Combine with "Assets/Defaults/" to read.
    /// </summary>
    public static IEnumerable<string> EnumerateDefaultFileNames()
    {
        const string marker = "Assets.Defaults.";
        foreach (string name in RuntimeAssembly.GetManifestResourceNames())
        {
            int idx = name.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
                yield return name[(idx + marker.Length)..];
        }
    }

    private static bool TryGetResourceName(string resourcePath, out string? resourceName)
    {
        resourceName = null;

        // Normalize path separators
        resourcePath = resourcePath.Replace('\\', '/');

        string[] resourceNames = RuntimeAssembly.GetManifestResourceNames();

        // Try exact match first
        resourceName = resourceNames.FirstOrDefault(r => r.Replace('\\', '/').EndsWith(resourcePath, StringComparison.OrdinalIgnoreCase) ||
                                                         r.Replace('\\', '/').EndsWith("." + resourcePath.Split('/').Last(), StringComparison.OrdinalIgnoreCase))
                    ?? resourceNames.FirstOrDefault(r => r.EndsWith(Path.GetFileName(resourcePath), StringComparison.OrdinalIgnoreCase));

        return resourceName != null;
    }
}
