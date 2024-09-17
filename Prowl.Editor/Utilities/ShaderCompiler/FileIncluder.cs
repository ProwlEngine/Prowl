// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#pragma warning disable

namespace Prowl.Editor.Utilities;

public class FileIncluder
{
    public readonly string SourceFile;
    public string SourceFilePath => Path.Join(_searchDirectories[0].FullName, SourceFile);

    private readonly string _relativeDirectory;
    private readonly int _qualifiedPrefixLength;

    private readonly DirectoryInfo[] _searchDirectories;


    public FileIncluder(string sourceFile, DirectoryInfo[] searchDirectories)
    {
        SourceFile = sourceFile;
        _relativeDirectory = Path.GetDirectoryName(_relativeDirectory) ?? "";

        string fullyQualifiedPath = Path.GetFullPath("/");

        _qualifiedPrefixLength = fullyQualifiedPath.Length;
        _relativeDirectory = fullyQualifiedPath + _relativeDirectory;
        _searchDirectories = searchDirectories;
    }


    public string Include(string file)
    {
        string filePath = Path.GetFullPath(file, _relativeDirectory)[_qualifiedPrefixLength..];

        string? resultPath = null;

        for (int i = 0; i < _searchDirectories.Length; i++)
        {
            DirectoryInfo directory = _searchDirectories[i];

            string relativePath = Path.Join(directory.FullName, filePath);

            if (File.Exists(relativePath))
            {
                resultPath = relativePath;
                break;
            }
        }

        if (resultPath == null)
            throw new FileNotFoundException($"Could not resolve include path: {file}");

        return File.ReadAllText(resultPath);
    }
}
