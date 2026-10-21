// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.OrigamiUI;

public static class CodeFolding
{
    /// <summary>
    /// Folds every brace pair spanning more than one line, every #region to its #endregion, and runs of
    /// three or more line comments or using directives. Braces inside strings and comments are skipped by lexing.
    /// </summary>
    public static List<CodeFoldRange> FromBraces(string[] lines, ICodeLexer? lexer, CancellationToken ct)
    {
        var folds = new List<CodeFoldRange>();
        var braces = new Stack<int>();
        var regions = new Stack<(int Line, string Label)>();
        var tokens = new List<CodeToken>();
        int state = 0;
        int commentRun = -1, usingRun = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();

            string line = lines[i];
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("#region", StringComparison.Ordinal))
                regions.Push((i, trimmed));
            else if (trimmed.StartsWith("#endregion", StringComparison.Ordinal) && regions.Count > 0)
            {
                var r = regions.Pop();
                if (i > r.Line) folds.Add(new CodeFoldRange(r.Line, i, r.Label));
            }

            bool isComment = trimmed.StartsWith("//", StringComparison.Ordinal);
            if (isComment) { if (commentRun < 0) commentRun = i; }
            else
            {
                if (commentRun >= 0 && i - commentRun >= 3) folds.Add(new CodeFoldRange(commentRun, i - 1));
                commentRun = -1;
            }

            bool isUsing = trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(';');
            if (isUsing) { if (usingRun < 0) usingRun = i; }
            else if (trimmed.Length > 0)
            {
                if (usingRun >= 0 && i - usingRun >= 3) folds.Add(new CodeFoldRange(usingRun, LastNonBlank(lines, usingRun, i)));
                usingRun = -1;
            }

            if (lexer == null)
            {
                for (int c = 0; c < line.Length; c++) Brace(lines, line[c], i, braces, folds);
                continue;
            }

            tokens.Clear();
            state = lexer.Tokenize(line, state, tokens);
            foreach (var t in tokens)
            {
                if (t.Kind != CodeTokenKind.Punctuation) continue;
                for (int c = t.Start; c < t.End; c++) Brace(lines, line[c], i, braces, folds);
            }
        }

        folds.Sort((a, b) => a.StartLine != b.StartLine ? a.StartLine.CompareTo(b.StartLine) : b.EndLine.CompareTo(a.EndLine));
        return folds;
    }

    private static int LastNonBlank(string[] lines, int from, int before)
    {
        for (int i = before - 1; i > from; i--)
            if (lines[i].Trim().Length > 0) return i;
        return from;
    }

    // An Allman style brace on its own line folds from the declaration above it.
    private static void Brace(string[] lines, char c, int line, Stack<int> braces, List<CodeFoldRange> folds)
    {
        if (c == '{')
        {
            bool alone = lines[line].Trim() == "{";
            braces.Push(alone && line > 0 && lines[line - 1].Trim().Length > 0 ? line - 1 : line);
        }
        else if (c == '}' && braces.Count > 0)
        {
            int open = braces.Pop();
            if (line > open) folds.Add(new CodeFoldRange(open, line));
        }
    }
}
