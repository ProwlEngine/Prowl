// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

#pragma warning disable

using System;
using Prowl.Runtime;

using Glslang.NET;
using Prowl.Runtime.Rendering;

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


    struct IncludeContext
    {
        public FileIncluder parentIncluder;
        public List<CompilationMessage> messages;
        public string entrypoint;
        public KeywordState? keywords;


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
                includerName = parentIncluder.SourceFile;

            IncludeResult result = new IncludeResult();

            result.headerData = " ";
            result.headerName = " ";

            try
            {
                string fullPath = parentIncluder.ResolveHeader(headerName, includerName, out string parsedHeader);

                result.headerName = parsedHeader;
                result.headerData = File.ReadAllText(fullPath);
            }
            catch (FileNotFoundException)
            {
                messages.Add(new CompilationMessage()
                {
                    severity = LogSeverity.Error,
                    message = $"Failed to open source file: {headerName}",
                    entrypoint = entrypoint,
                    keywords = keywords
                });

            }

            return result;
        }
    }


    public Glslang.NET.FileIncluder GetIncluder(List<CompilationMessage> messages, string entrypoint, KeywordState? keywords)
    {
        IncludeContext ctx;
        ctx.parentIncluder = this;
        ctx.messages = messages;
        ctx.entrypoint = entrypoint;
        ctx.keywords = keywords;

        return ctx.Include;
    }


    public string ResolveHeader(string rawHeader, string includer, out string parsedHeader)
    {
        string filePath = rawHeader;

        if (!Path.IsPathRooted(rawHeader))
            filePath = Path.GetFullPath(rawHeader, s_qualifiedPrefix + Path.GetDirectoryName(includer))[(s_qualifiedPrefix.Length - 1)..];

        parsedHeader = filePath;

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
            throw new FileNotFoundException();

        return resultPath;
    }
}
