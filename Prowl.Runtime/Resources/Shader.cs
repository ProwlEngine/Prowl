// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;

using GShader = Prowl.Graphite.Rendering.Shader;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A shader in Graphite's ShaderDef format: its source, the parsed definition, and the Graphite shader materials draw
/// with, which declares the material's properties and keyword axes. A source that fails to parse leaves the shader
/// without a Graphite shader, and its materials draw with the Invalid shader.
/// </summary>
public sealed class Shader : Asset, ISerializable
{
    private static readonly ConcurrentDictionary<string, string> s_sourcePaths = new(StringComparer.Ordinal);

    private string _source = "";
    private string _sourcePath = "";
    [NotHeld] private GShader? _gpu;
    private string? _error;

    internal Shader() : base("New Shader") { }

    /// <summary>A shader from ShaderDef source. <paramref name="sourcePath"/> is the file includes resolve against.</summary>
    public static Shader FromSource(string source, string sourcePath = "")
    {
        Shader shader = new();
        shader.SetSource(source, sourcePath);
        return shader;
    }

    /// <summary>The ShaderDef source.</summary>
    public string Source { get { EnsureLoaded(); return _source; } }

    /// <summary>The file the source came from, which relative includes resolve against.</summary>
    public string SourcePath { get { EnsureLoaded(); return _sourcePath; } }

    /// <summary>The Graphite shader, null when the source failed to parse.</summary>
    public GShader? GpuShader { get { EnsureLoaded(); return _gpu; } }

    /// <summary>The Graphite shader without loading, for code that runs every frame.</summary>
    internal GShader? GpuIfLoaded => IsLoaded ? _gpu : null;

    /// <summary>Why the source failed to parse, null when it parsed.</summary>
    public string? Error { get { EnsureLoaded(); return _error; } }

    /// <summary>The properties a material of this shader has, in declaration order.</summary>
    public IReadOnlyList<MaterialProperty> Properties { get { EnsureLoaded(); return (IReadOnlyList<MaterialProperty>?)_gpu?.Properties ?? []; } }

    /// <summary>The keyword axes a material sets, each with its values.</summary>
    public IReadOnlyList<VariantSpace> Keywords { get { EnsureLoaded(); return (IReadOnlyList<VariantSpace>?)_gpu?.MaterialAxes ?? []; } }

    /// <summary>The render queue its materials draw in by default, 2500 and up transparent.</summary>
    public int RenderQueue { get { EnsureLoaded(); return _gpu?.RenderQueue ?? GShader.DefaultRenderQueue; } }

    /// <summary>Whether the shader has a pass of this type, such as ShadowCaster.</summary>
    public bool HasPass(PassType type) { EnsureLoaded(); return _gpu?.HasPass(type) ?? false; }

    public MaterialProperty? FindProperty(string name)
    {
        EnsureLoaded();
        if (_gpu == null) return null;
        int index = _gpu.FindProperty(name);
        return index < 0 ? null : _gpu.Properties[index];
    }

    /// <summary>A shader of Graphite's library, such as "Graphite/Standard/Lit.shader", sharing the renderer's instance.</summary>
    internal static Shader FromLibrary(string libraryPath)
    {
        Shader shader = new();
        shader.SetSource(LibrarySource(libraryPath), libraryPath, Graphics.LibraryShader(libraryPath));
        return shader;
    }

    /// <summary>Replaces the source and parses it again. Materials of the shader pick the new one up.</summary>
    public void SetSource(string source, string sourcePath = "") => SetSource(source, sourcePath, null);

    private void SetSource(string source, string sourcePath, GShader? built)
    {
        _source = source ?? "";
        _sourcePath = sourcePath ?? "";
        _error = null;
        _gpu = null;
        try
        {
            ShaderDefinition definition = ShaderParser.Parse(_source);
            if (definition.Name is { Length: > 0 } declared)
            {
                Name = declared;
                if (_sourcePath.Length > 0) s_sourcePaths[declared] = _sourcePath;
            }
            _gpu = built ?? Graphics.Renderer?.LoadShader(definition) ?? new GShader(definition, Graphics.Compiler);
        }
        catch (Exception e)
        {
            _error = e.Message;
            Debug.LogError($"Shader '{(_sourcePath.Length > 0 ? _sourcePath : Name)}' failed to parse: {e.Message}");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex s_include = new(@"^[ \t]*#include[ \t]+""([^""]+)""[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// Inlines the includes of a source that are neither Graphite's nor Prowl's library through <paramref name="resolve"/>,
    /// emitting <c>#line</c> directives so errors inside includes and later lines of the shader report their own file and line.
    /// A file pasted more than once goes inside a guard named after its text, so every stage the compiler builds keeps one
    /// copy of it, whichever blocks name it and however it is spelled. Library includes stay for the compiler. An include it
    /// cannot find stays as written.
    /// </summary>
    public static string ExpandIncludes(string source, Func<string, string?> resolve)
        => ExpandIncludes(source, resolve, null);

    internal static string ExpandIncludes(string source, Func<string, string?> resolve, string? sourceName)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        string rootFile = ReadDeclaredPath(source) ?? sourceName ?? "shader";
        return new IncludeExpander(resolve).Expand(source, rootFile);
    }

    private readonly record struct GlslProgramBlock(int Start, int End, bool IsPassProgram, string FileName);

    private static List<GlslProgramBlock> FindGlslPrograms(string source, string shaderName)
    {
        List<GlslProgramBlock> blocks = [];
        int braceDepth = 0;
        bool inProperties = false;
        int propertiesDepth = -1;
        bool inPass = false;
        int passDepth = -1;
        string passName = "";
        bool expectPassName = false;
        bool inSurface = false;
        int surfaceDepth = -1;

        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? source.Length : close + 2;
                continue;
            }

            if (c == '"')
            {
                int start = i + 1;
                i = SkipString(source, i);
                if (expectPassName && inPass && braceDepth == passDepth)
                {
                    int end = i > start && source[i - 1] == '"' ? i - 1 : i;
                    passName = source[start..end];
                    expectPassName = false;
                }
                continue;
            }

            expectPassName = false;

            if (c == '{')
            {
                braceDepth++;
                if (inProperties && propertiesDepth < 0)
                    propertiesDepth = braceDepth;
                else if (inPass && passDepth < 0)
                    passDepth = braceDepth;
                else if (inSurface && surfaceDepth < 0)
                    surfaceDepth = braceDepth;
                i++;
                continue;
            }

            if (c == '}')
            {
                braceDepth--;
                if (inProperties && braceDepth < propertiesDepth)
                {
                    inProperties = false;
                    propertiesDepth = -1;
                }
                if (inPass && braceDepth < passDepth)
                {
                    inPass = false;
                    passDepth = -1;
                    passName = "";
                }
                if (inSurface && braceDepth < surfaceDepth)
                {
                    inSurface = false;
                    surfaceDepth = -1;
                }
                i++;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                    i++;
                string word = source[start..i];

                if (braceDepth == 1 && !inProperties)
                {
                    if (string.Equals(word, "Properties", StringComparison.OrdinalIgnoreCase))
                    {
                        inProperties = true;
                        propertiesDepth = -1;
                    }
                    else if (string.Equals(word, "Pass", StringComparison.OrdinalIgnoreCase))
                    {
                        inPass = true;
                        passDepth = -1;
                        passName = "";
                    }
                    else if (string.Equals(word, "Surface", StringComparison.OrdinalIgnoreCase))
                    {
                        inSurface = true;
                        surfaceDepth = -1;
                    }
                }
                else if (inPass && braceDepth == passDepth && string.Equals(word, "Name", StringComparison.OrdinalIgnoreCase))
                {
                    expectPassName = true;
                }

                if ((inPass || inSurface) && string.Equals(word, "GLSLPROGRAM", StringComparison.Ordinal))
                {
                    int end = source.IndexOf("ENDGLSL", i, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        bool isPass = inPass && !inSurface;
                        string file = isPass && passName.Length > 0 ? $"{shaderName}/{passName}" : shaderName;
                        blocks.Add(new GlslProgramBlock(i, end, isPass, file));
                        i = end + "ENDGLSL".Length;
                    }
                }
                continue;
            }

            i++;
        }

        return blocks;
    }

    private sealed class IncludeExpander(Func<string, string?> resolve)
    {
        private readonly Func<string, string?> _resolve = resolve;
        private readonly Dictionary<string, string?> _read = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _pastes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _open = new(StringComparer.Ordinal);

        public string Expand(string source, string rootFile)
        {
            List<GlslProgramBlock> programs = FindGlslPrograms(source, rootFile);

            if (programs.Count == 0)
            {
                ExpandGlsl(source, 1, rootFile, 0, false);
                if (_pastes.Count == 0)
                    return source;

                return ExpandGlsl(source, 1, rootFile, 0, true);
            }

            // Walk once to count how often each include is pasted across every GLSL block.
            foreach (GlslProgramBlock block in programs)
            {
                string blockText = source[block.Start..block.End];
                if (block.IsPassProgram)
                {
                    bool unusedShift = false;
                    ExpandPassProgram(blockText, 1, block.FileName, 0, false, ref unusedShift, false);
                }
                else
                {
                    ExpandGlsl(blockText, 1, block.FileName, 0, false);
                }
            }

            if (_pastes.Count == 0)
                return source;

            StringBuilder result = new(source.Length + 256);
            int cursor = 0;
            bool lineShifted = false;
            foreach (GlslProgramBlock block in programs)
            {
                result.Append(source, cursor, block.Start - cursor);
                int blockBaseLine = 1 + CountNewlines(source, 0, block.Start);
                string blockText = source[block.Start..block.End];
                if (block.IsPassProgram)
                {
                    result.Append(ExpandPassProgram(blockText, blockBaseLine, block.FileName, 0, true, ref lineShifted, false));
                }
                else
                {
                    string expanded = ExpandGlsl(blockText, blockBaseLine, block.FileName, 0, true);
                    if (CountNewlines(expanded, 0, expanded.Length) != CountNewlines(blockText, 0, blockText.Length))
                        lineShifted = true;
                    result.Append(expanded);
                }
                cursor = block.End;
            }
            result.Append(source, cursor, source.Length - cursor);
            return result.ToString();
        }

        private string ExpandPassProgram(
            string text,
            int baseLine,
            string passFile,
            int depth,
            bool write,
            ref bool lineShifted,
            bool forceStageLine)
        {
            StringBuilder? sb = write ? new StringBuilder(text.Length) : null;
            int copiedUpTo = 0;
            int i = 0;
            bool atLineStart = true;

            while (i < text.Length)
            {
                char c = text[i];
                if (c is '\n' or '\r')
                {
                    i = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1;
                    atLineStart = true;
                    continue;
                }

                if (c is ' ' or '\t' || char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                        i++;
                    atLineStart = false;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? text.Length : close + 2;
                    atLineStart = false;
                    continue;
                }

                // Section-level #include directives must not emit #line or #ifndef outside { ... },
                // because GlslSection.Split rejects preprocessor directives between sections.
                if (atLineStart && c == '#')
                {
                    int lineStart = i;
                    while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t')
                        lineStart--;
                    int lineEnd = i;
                    while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
                        lineEnd++;
                    int nextStart = NextLineStart(text, lineEnd);

                    Match match = s_include.Match(text, lineStart, lineEnd - lineStart);
                    if (match.Success)
                    {
                        string name = match.Groups[1].Value;
                        if (TryResolve(name, depth, out string included, out string guard))
                        {
                            if (!_open.Add(guard))
                            {
                                if (write)
                                {
                                    sb!.Append(text, copiedUpTo, lineStart - copiedUpTo);
                                    sb.Append(text, lineEnd, nextStart - lineEnd);
                                    copiedUpTo = nextStart;
                                }
                                i = nextStart;
                                atLineStart = true;
                                continue;
                            }

                            if (!write)
                                _pastes[guard] = _pastes.GetValueOrDefault(guard) + 1;

                            bool nestedShift = false;
                            string body = ExpandPassProgram(included, 1, name, depth + 1, write, ref nestedShift, true);
                            _open.Remove(guard);

                            if (CountNewlines(body, 0, body.Length) != 0 || nestedShift)
                                lineShifted = true;

                            if (write)
                            {
                                sb!.Append(text, copiedUpTo, lineStart - copiedUpTo);
                                sb.Append(body);
                                sb.Append(text, lineEnd, nextStart - lineEnd);
                                copiedUpTo = nextStart;
                            }

                            i = nextStart;
                            atLineStart = true;
                            continue;
                        }
                    }

                    i = lineEnd;
                    atLineStart = false;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        i++;
                    ReadOnlySpan<char> word = text.AsSpan(start, i - start);
                    if (word is "Shared" or "Vertex" or "Fragment" or "Compute")
                    {
                        int after = SkipSectionTrivia(text, i);
                        if (after < text.Length && text[after] == '{')
                        {
                            int close = MatchGlslBrace(text, after);
                            if (close < 0)
                                break;

                            int openLine = baseLine + CountNewlines(text, 0, after);
                            string inner = text[(after + 1)..close];
                            string expandedInner = ExpandGlsl(inner, openLine, passFile, depth, write);
                            bool emitStageLine = forceStageLine || lineShifted;
                            if (emitStageLine || CountNewlines(expandedInner, 0, expandedInner.Length) != CountNewlines(inner, 0, inner.Length))
                                lineShifted = true;

                            if (write)
                            {
                                sb!.Append(text, copiedUpTo, after + 1 - copiedUpTo);
                                if (emitStageLine)
                                {
                                    sb.Append('\n');
                                    AppendLineDirective(sb, openLine, forceStageLine ? passFile : null);
                                    sb.Append('\n');
                                }
                                sb.Append(expandedInner);
                                sb.Append('}');
                                copiedUpTo = close + 1;
                            }

                            i = close + 1;
                            atLineStart = false;
                            continue;
                        }
                    }
                    atLineStart = false;
                    continue;
                }

                i++;
                atLineStart = false;
            }

            if (!write)
                return "";

            sb!.Append(text, copiedUpTo, text.Length - copiedUpTo);
            return sb.ToString();
        }

        private string ExpandGlsl(string text, int baseLine, string parentFile, int depth, bool write)
        {
            StringBuilder? sb = write ? new StringBuilder(text.Length) : null;
            int line = baseLine;
            bool inBlockComment = false;
            bool pendingResume = false;
            List<bool> condStack = [];

            int pos = 0;
            while (pos < text.Length)
            {
                int lineStart = pos;
                while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r')
                    pos++;
                int lineEnd = pos;
                int nextStart = NextLineStart(text, lineEnd);

                bool handled = false;
                if (!inBlockComment && !pendingResume)
                {
                    int scan = lineStart;
                    while (scan < lineEnd && text[scan] is ' ' or '\t')
                        scan++;
                    if (scan < lineEnd && text[scan] == '#')
                    {
                        scan++;
                        while (scan < lineEnd && text[scan] is ' ' or '\t')
                            scan++;
                        int wordStart = scan;
                        while (scan < lineEnd && (char.IsLetterOrDigit(text[scan]) || text[scan] == '_'))
                            scan++;
                        ReadOnlySpan<char> directive = text.AsSpan(wordStart, scan - wordStart);

                        if (directive is "include")
                        {
                            Match match = s_include.Match(text, lineStart, lineEnd - lineStart);
                            if (match.Success)
                            {
                                string name = match.Groups[1].Value;
                                if (TryResolve(name, depth, out string included, out string guard))
                                {
                                    handled = true;
                                    if (!_open.Add(guard))
                                    {
                                        if (write)
                                            sb!.Append(text, lineEnd, nextStart - lineEnd);
                                    }
                                    else
                                    {
                                        if (!write)
                                            _pastes[guard] = _pastes.GetValueOrDefault(guard) + 1;

                                        string body = ExpandGlsl(included, 1, name, depth + 1, write);
                                        _open.Remove(guard);

                                        if (condStack.Count > 0)
                                            condStack[^1] = true;

                                        if (write)
                                        {
                                            StringBuilder builder = sb!;
                                            bool guarded = _pastes[guard] > 1;
                                            if (guarded)
                                                builder.Append("#ifndef ").Append(guard).Append("\n#define ").Append(guard).Append('\n');
                                            AppendLineDirective(builder, 1, name);
                                            builder.Append('\n').Append(body);
                                            if (body.Length > 0 && body[^1] is not ('\n' or '\r'))
                                                builder.Append('\n');
                                            if (guarded)
                                                builder.Append("#endif\n");
                                            AppendLineDirective(builder, line + 1, parentFile);
                                            builder.Append(text, lineEnd, nextStart - lineEnd);
                                        }
                                    }
                                }
                            }
                        }
                        else if (directive is "if" or "ifdef" or "ifndef")
                        {
                            condStack.Add(false);
                        }
                        else if (directive is "else" or "elif")
                        {
                            if (condStack.Count > 0 && condStack[^1])
                                pendingResume = true;
                        }
                        else if (directive is "endif")
                        {
                            if (condStack.Count > 0)
                            {
                                bool hadInclude = condStack[^1];
                                condStack.RemoveAt(condStack.Count - 1);
                                if (hadInclude)
                                {
                                    if (condStack.Count > 0)
                                        condStack[^1] = true;
                                    pendingResume = true;
                                }
                            }
                        }
                    }
                }

                if (!handled)
                {
                    if (write)
                        sb!.Append(text, lineStart, nextStart - lineStart);
                    inBlockComment = UpdateBlockComment(text, lineStart, lineEnd, inBlockComment);
                    if (pendingResume && !EndsWithBackslash(text, lineStart, lineEnd))
                    {
                        pendingResume = false;
                        if (write && nextStart < text.Length)
                        {
                            AppendLineDirective(sb!, line + 1, parentFile);
                            sb!.Append('\n');
                        }
                    }
                }

                line++;
                pos = nextStart;
            }

            return write ? sb!.ToString() : "";
        }

        private bool TryResolve(string name, int depth, out string included, out string guard)
        {
            included = "";
            guard = "";
            if (name.StartsWith("Graphite/", StringComparison.Ordinal) || name.StartsWith(BuiltInShaders.Prefix, StringComparison.Ordinal) || depth > 16)
                return false;

            if (!_read.TryGetValue(name, out string? raw))
            {
                raw = _resolve(name);
                if (raw != null && raw.Length > 0 && raw[0] == '\uFEFF')
                    raw = raw[1..];
                _read[name] = raw;
            }

            if (raw == null)
                return false;

            included = raw;
            // Hashed the same every run, as the expanded source keys the compiled variant caches
            guard = "PROWL_INCLUDED_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(included)), 0, 8);
            return true;
        }
    }

    private static void AppendLineDirective(StringBuilder sb, int line, string? file)
    {
        sb.Append("#line ").Append(line);
        if (file == null)
            return;

        sb.Append(" \"");
        foreach (char c in file)
        {
            if (c == '\\')
                sb.Append('/');
            else if (c == '"')
                sb.Append("\\\"");
            else
                sb.Append(c);
        }
        sb.Append('"');
    }

    private static int NextLineStart(string text, int lineEnd)
    {
        if (lineEnd >= text.Length)
            return lineEnd;
        return text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
    }

    private static int CountNewlines(string text, int start, int end)
    {
        int count = 0;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (c == '\n')
                count++;
            else if (c == '\r' && (i + 1 >= end || text[i + 1] != '\n'))
                count++;
        }
        return count;
    }

    private static bool EndsWithBackslash(string text, int lineStart, int lineEnd)
    {
        int i = lineEnd - 1;
        while (i >= lineStart && text[i] is ' ' or '\t')
            i--;
        return i >= lineStart && text[i] == '\\';
    }

    private static int SkipString(string source, int quoteIndex)
    {
        int i = quoteIndex + 1;
        while (i < source.Length)
        {
            char c = source[i++];
            if (c == '\\' && i < source.Length)
                i++;
            else if (c is '"' or '\n' or '\r')
                break;
        }
        return i;
    }

    private static int SkipSectionTrivia(string text, int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
                i++;
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                    i++;
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
            }
            else
                break;
        }
        return i;
    }

    private static int MatchGlslBrace(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                    i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    return -1;
                i = end + 1;
                continue;
            }

            if (c == '"')
            {
                i = SkipString(text, i) - 1;
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static bool UpdateBlockComment(string text, int start, int end, bool inBlockComment)
    {
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < end && text[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }
            }
            else if (c == '/' && i + 1 < end && text[i + 1] == '/')
            {
                break;
            }
            else if (c == '/' && i + 1 < end && text[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
            }
            else if (c == '"')
            {
                while (i + 1 < end)
                {
                    char next = text[++i];
                    if (next == '\\' && i + 1 < end)
                        i++;
                    else if (next == '"')
                        break;
                }
            }
        }
        return inBlockComment;
    }

    /// <summary>The source file of a shader by the name it declares, for includes relative to it.</summary>
    internal static string? SourcePathOf(string shaderName) => s_sourcePaths.TryGetValue(shaderName, out string? path) ? path : null;

    /// <summary>Loads a shader from a ShaderDef file.</summary>
    public static Shader LoadFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException($"Shader file not found: {filePath}");
        return FromSource(System.IO.File.ReadAllText(filePath), filePath);
    }

    /// <summary>The shared instance of a built in shader.</summary>
    public static Shader LoadDefault(DefaultShader shader) => BuiltInAssets.Load<Shader>(BuiltInAssets.GuidFor(shader));

    /// <summary>The built-in shader without loading it, for code that runs while another asset loads.</summary>
    internal static Shader GetDefault(DefaultShader shader) => AssetDatabase.Get<Shader>(BuiltInAssets.GuidFor(shader))!;

    /// <summary>Parses a built in shader, called by <see cref="BuiltInAssets"/> on its first use.</summary>
    internal static Shader ParseDefault(DefaultShader shader)
    {
        if (LibraryPathOf(shader) is { } library)
            return FromLibrary(library);
        string path = $"Assets/Defaults/Shaders/{shader}.shader";
        return FromSource(EmbeddedResources.ReadAllText(path), path);
    }

    /// <summary>Raw source text of a built in shader.</summary>
    public static string GetDefaultSource(DefaultShader shader)
        => LibraryPathOf(shader) is { } library ? LibrarySource(library) : EmbeddedResources.ReadAllText($"Assets/Defaults/Shaders/{shader}.shader");

    // The compiler registers Graphite's library on first use
    private static string LibrarySource(string libraryPath)
    {
        _ = Graphics.Compiler;
        return ShaderLibrary.Get(libraryPath) ?? throw new ArgumentException($"Graphite's library has no shader {libraryPath}.");
    }

    private static string? LibraryPathOf(DefaultShader shader) => shader switch
    {
        DefaultShader.Lit => "Graphite/Standard/Lit.shader",
        DefaultShader.Unlit => "Graphite/Standard/Unlit.shader",
        DefaultShader.ProceduralSky => "Graphite/Skies/Procedural.shader",
        DefaultShader.GradientSky => "Graphite/Skies/Gradient.shader",
        DefaultShader.CubemapSky => "Graphite/Skies/Cubemap.shader",
        DefaultShader.PanoramaSky => "Graphite/Skies/Panorama.shader",
        DefaultShader.SixSidedSky => "Graphite/Skies/SixSided.shader",
        _ => null,
    };

    /// <summary>
    /// Pulls the declared name out of the <c>Shader "Some/Path"</c> line at the top of a shader source, without parsing
    /// it. Leading comments and blank lines are skipped. Null when no declaration is found.
    /// </summary>
    public static string? ReadDeclaredPath(string source)
    {
        if (string.IsNullOrEmpty(source)) return null;

        int i = 0;
        const char byteOrderMark = (char)0xFEFF;
        while (i < source.Length && source[i] == byteOrderMark) i++;

        while (i < source.Length)
        {
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                i = end + 2;
                continue;
            }
            break;
        }

        const string keyword = "Shader";
        if (i + keyword.Length >= source.Length) return null;
        if (string.CompareOrdinal(source, i, keyword, 0, keyword.Length) != 0) return null;
        i += keyword.Length;

        while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
        if (i >= source.Length || source[i] != '"') return null;

        int start = ++i;
        while (i < source.Length && source[i] != '"') i++;
        return i < source.Length ? source[start..i] : null;
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Source", new(_source));
        compoundTag.Add("SourcePath", new(_sourcePath));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);
        SetSource(value.Get("Source")?.StringValue ?? "", value.Get("SourcePath")?.StringValue ?? "");
    }

    protected override void OnUnload() => _gpu = null;
}
