using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Slang.Native;


namespace Prowl.Slang;


#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public readonly record struct Diagnostic
{
    public Diagnostic(Severity severity,
        int errorCode = -1,
        string? message = null,
        string? filePath = null,
        int lineNumber = -1)
    {
        Severity = severity;
        ErrorCode = errorCode;
        Message = message ?? string.Empty;

        FilePath = filePath;
        LineNumber = lineNumber;
    }

    public readonly Severity Severity;
    public readonly int ErrorCode;
    public readonly string Message;

    public readonly string? FilePath;
    public readonly int LineNumber;
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member


/// <summary>
/// Slang diagnostic information.
/// </summary>
public struct DiagnosticInfo
{
    internal DiagnosticInfo(string? message)
    {
        Message = message;
    }


    internal unsafe DiagnosticInfo(ISlangBlob* blobPtr)
    {
        if (blobPtr == null)
        {
            Message = null;
            return;
        }

        Message = NativeComProxy.Create(blobPtr).GetString();
    }


    /// <summary>
    /// The diagnostic message.
    /// </summary>
    public string? Message;


    /// <summary>
    /// Gets a parsed list of diagnostics from the diagnostic message.
    /// </summary>
    /// <returns></returns>
    public List<Diagnostic> GetDiagnostics()
    {
        if (Message == null)
            return [];

        string[] lines = Message.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        List<Diagnostic> diagnostics = new();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            int infoStart = line.IndexOf("): ");

        ReadNewLine:

            if (infoStart <= 0)
                continue;

            int lineNumStart = line.LastIndexOf('(', infoStart);

            infoStart += 3;

            int infoEnd = line.IndexOf(":", infoStart);

            string subsec = line.Substring(infoStart, infoEnd - infoStart);
            string[] parts = subsec.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            Severity severity = Enum.Parse<Severity>(parts[0], true);
            int code = int.Parse(parts[^1]);
            string path = line.Substring(0, lineNumStart);
            int lineNumber = int.Parse(line.Substring(lineNumStart + 1, infoStart - lineNumStart - 4));

            string message = line.Substring(infoEnd + 1);

            while (++i < lines.Length)
            {
                string nextLine = lines[i];
                int index = nextLine.IndexOf("): ");

                if (index > 0)
                {
                    infoStart = index;
                    line = nextLine;
                    diagnostics.Add(new Diagnostic(severity, code, message, path, lineNumber));

                    goto ReadNewLine;
                }

                message += '\n' + nextLine;
            }
        }

        return diagnostics;
    }


    /// <summary>
    /// Gets the message of this diagnostic as a parsed exception.
    /// </summary>
    public readonly Exception? GetException()
    {
        return Message == null ? null : new CompilationException(this, Message);
    }
}