// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#pragma warning disable

using System;
using System.Diagnostics;

using Glslang.NET;

namespace Prowl.Editor;

public class FileIncluder
{
    public string SourceFile;
    public string SourceFilePath => Path.Join(_searchDirectories[0].FullName, SourceFile);

    private static readonly string s_qualifiedPrefix = Path.GetFullPath("/");

    private readonly DirectoryInfo[] _searchDirectories;


    public FileIncluder(string sourceFile, DirectoryInfo[] searchDirectories)
    {
        SourceFile = sourceFile;
        _searchDirectories = searchDirectories;
    }


    public IncludeResult Include(string headerName, string includerName, uint includeDepth, bool isSystemFile)
    {
        if (includeDepth > 150)
        {
            return new IncludeResult()
            {
                headerName = headerName,
                headerData = ""
            };
        }

        if (string.IsNullOrWhiteSpace(includerName))
            includerName = Path.GetDirectoryName(SourceFile) ?? "";

        Runtime.Debug.Log($"Including {headerName} from: {includerName}");

        IncludeResult result = new IncludeResult();

        string fullPath = ResolveHeader(headerName, includerName, out string parsedHeader);

        result.headerName = parsedHeader;
        result.headerData = File.ReadAllText(GetFullFilePath(includerName));

        return result;
    }


    public string Include(string file)
    {
        return File.ReadAllText(file);
    }


    public string ResolveHeader(string rawHeader, string fullIncluder, out string parsedHeader)
    {
        string filePath = rawHeader;

        // Not an absolute/full path - resolve to project-relative 'full path'
        if (!rawHeader.StartsWith('/'))
            filePath = Path.GetFullPath(rawHeader, s_qualifiedPrefix + fullIncluder)[s_qualifiedPrefix.Length..];

        parsedHeader = Path.GetDirectoryName(filePath);

        return GetFullFilePath(filePath);
    }


    public string GetFullFilePath(string projectRelativePath)
    {
        string? resultPath = null;

        for (int i = 0; i < _searchDirectories.Length; i++)
        {
            DirectoryInfo directory = _searchDirectories[i];

            string relativePath = Path.Join(directory.FullName, projectRelativePath);

            if (File.Exists(relativePath))
            {
                resultPath = relativePath;
                break;
            }
        }

        if (resultPath == null)
            throw new FileNotFoundException($"Could not resolve include path: {projectRelativePath}");

        return resultPath;
    }
}
