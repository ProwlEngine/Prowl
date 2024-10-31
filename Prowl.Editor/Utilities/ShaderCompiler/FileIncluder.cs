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


        public IncludeResult Include(string includeText, string includerPath, uint includeDepth, bool isSystemFile)
        {
            if (includeDepth > 150)
            {
                return new IncludeResult()
                {
                    headerName = includeText,
                    headerData = ""
                };
            }

            bool fromSource = false;
            if (string.IsNullOrWhiteSpace(includerPath))
            {
                fromSource = true;
                includerPath = parentIncluder.SourceFile;
            }

            IncludeResult result = new IncludeResult();

            result.headerData = " ";
            result.headerName = " ";

            if (!parentIncluder.ResolveHeader(includeText, includerPath, out string fullPath, out string parsedHeader))
            {
                CompilationMessage msg = new CompilationMessage()
                {
                    severity = LogSeverity.Error,
                    message = $"Failed to open source file: {includeText}",
                };

                if (parentIncluder.GetFullFilePath(includerPath, out string? sourceFile))
                {
                    msg.file = new CompilationFile()
                    {
                        isSourceFile = fromSource,
                        filename = sourceFile,
                        line = 1,
                        column = 1
                    };
                }

                messages.Add(msg);

                return result;
            }

            result.headerName = parsedHeader;
            result.headerData = File.ReadAllText(fullPath);

            return result;
        }
    }


    public Glslang.NET.FileIncluder GetIncluder(List<CompilationMessage> messages)
    {
        IncludeContext ctx;
        ctx.parentIncluder = this;
        ctx.messages = messages;

        return ctx.Include;
    }


    public bool ResolveHeader(string rawHeader, string includer, out string fullPath, out string parsedHeader)
    {
        string filePath = rawHeader;

        if (!Path.IsPathRooted(rawHeader))
            filePath = Path.GetFullPath(rawHeader, s_qualifiedPrefix + Path.GetDirectoryName(includer))[(s_qualifiedPrefix.Length - 1)..];

        parsedHeader = filePath;

        return GetFullFilePath(filePath, out fullPath);
    }


    public bool GetFullFilePath(string projectRelativePath, out string? resultPath)
    {
        resultPath = null;

        for (int i = 0; i < _searchDirectories.Length; i++)
        {
            DirectoryInfo directory = _searchDirectories[i];

            string relativePath = Path.Join(directory.FullName, projectRelativePath);

            if (File.Exists(relativePath))
            {
                resultPath = relativePath;
                return true;
            }
        }

        return false;
    }
}
