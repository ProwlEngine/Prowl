using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

using Prowl.Slang.Native;


namespace Prowl.Slang;


#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>
/// A structure containing parsed compiler diagnostic information.
/// </summary>
public struct Diagnostic : IEquatable<Diagnostic>
{
    public Severity Severity;
    public int ErrorCode;
    public string Message;

    public string? FilePath;
    public int LineNumber;


    /// <inhertidoc/>
    public override readonly string ToString()
    {
        return $"{FilePath ?? "Unknown file"}({LineNumber}): {Severity} {ErrorCode}: {Message}";
    }


    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is Diagnostic other)
            return Equals(other);

        return false;
    }


    /// <inheritdoc/>
    public readonly bool Equals(Diagnostic other)
    {
        return Severity == other.Severity &&
               LineNumber == other.LineNumber &&
               ErrorCode == other.ErrorCode &&
               Message == other.Message &&
               FilePath == other.FilePath;
    }


    public static bool operator ==(Diagnostic left, Diagnostic right)
    {
        return left.Equals(right);
    }


    public static bool operator !=(Diagnostic left, Diagnostic right)
    {
        return !left.Equals(right);
    }


    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Severity, ErrorCode, Message, FilePath, LineNumber);
    }
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

        Queue<string> lines = new(Message.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

        List<Diagnostic> diagnostics = [];

        while (lines.TryDequeue(out string? line))
        {
            // Start of diagnostic message excluding filepath.
            int diagnosticStart = line.LastIndexOf("): ");
            int diagnosticEnd = line.IndexOf(':', diagnosticStart + 3);

            if (diagnosticStart <= 0)
            {
                if (diagnostics.Count > 0)
                {
                    Diagnostic prev = diagnostics[^1];

                    prev.Message += '\n' + line;

                    diagnostics[^1] = prev;
                }

                continue;
            }

            int lineNumStart = line.LastIndexOf('(', diagnosticStart);

            int lineNumber = int.Parse(line.AsSpan(lineNumStart + 1, diagnosticStart - lineNumStart - 1));

            int severityEnd = Math.Min(line.IndexOf(' ', diagnosticStart + 3), diagnosticEnd);

            Severity severity = Enum.Parse<Severity>(line.AsSpan(diagnosticStart + 3, severityEnd - diagnosticStart - 3), true);

            int code = -1;

            int codeStart = line.LastIndexOf(' ', diagnosticEnd - 1) + 1;

            if (char.IsDigit(line[codeStart]))
                code = int.Parse(line.AsSpan(codeStart, diagnosticEnd - codeStart));

            string path = "";

            if (severity != Severity.Fatal)
                path = line.Substring(0, lineNumStart);

            string message = line.Substring(diagnosticEnd + 2);

            Diagnostic diagnostic = new Diagnostic
            {
                Severity = severity,
                ErrorCode = code,
                Message = message,
                FilePath = path,
                LineNumber = lineNumber
            };

            if (diagnostics.Count > 0)
            {
                Diagnostic last = diagnostics[^1];

                if (diagnostic.Equals(last))
                    continue;
            }

            diagnostics.Add(diagnostic);
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