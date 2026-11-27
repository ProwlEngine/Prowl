// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Aperture.Tests;

/// <summary>
/// Centralized accessor for the test-specific files.
/// </summary>
internal static class TestFiles
{
    /// <summary>The full path of the 'TestFiles' directory containing test-specific files.</summary>
    public static string TestFilesRoot { get; } = Resolve("TestFiles");

    /// <summary>Gets the full path of a file in the 'Dds' directory.</summary>
    public static string Dds(string fileName) => Path.Combine(TestFilesRoot, "Dds", fileName);

    private static string Resolve(string fileName)
    {
        string? dir = AppContext.BaseDirectory;
        for (int hops = 0; hops < 10 && dir is not null; hops++)
        {
            string candidate = Path.Combine(dir, fileName);
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{fileName}' walking up from {AppContext.BaseDirectory}. " +
            "Ensure the repo's TestFiles/ folder is committed and present.");
    }
}
