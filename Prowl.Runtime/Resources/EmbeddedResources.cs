// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.Resources
{
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
            using var stream = GetStream(resourcePath);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Checks if an embedded resource exists
        /// </summary>
        public static bool Exists(string resourcePath)
        {
            return TryGetResourceName(resourcePath, out _);
        }

        private static bool TryGetResourceName(string resourcePath, out string? resourceName)
        {
            resourceName = null;

            // Normalize path separators
            resourcePath = resourcePath.Replace('\\', '/');

            string[] resourceNames = RuntimeAssembly.GetManifestResourceNames();

            // Try exact match first
            resourceName = resourceNames.FirstOrDefault(r => r.Replace('\\', '/').EndsWith(resourcePath, StringComparison.OrdinalIgnoreCase))
                        ?? resourceNames.FirstOrDefault(r => r.EndsWith(Path.GetFileName(resourcePath), StringComparison.OrdinalIgnoreCase));

            return resourceName != null;
        }
    }
}
