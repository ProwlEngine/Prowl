// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Utils;
using Prowl.Vector;

using Shader = Prowl.Runtime.Resources.Shader;
using Texture2D = Prowl.Runtime.Resources.Texture2D;
using Texture3D = Prowl.Runtime.Resources.Texture3D;

namespace Prowl.Runtime.AssetImporting;

public static class ShaderParser
{
    private static readonly Regex _preprocessorIncludeRegex = new(@"^\s*#include\s*[""<](.+?)["">]\s*$", RegexOptions.Multiline);

    public enum ShaderStages { Vertex, Geometry, Fragment }

    public enum ShaderToken
    {
        None,
        Identifier,
        OpenSquareBrace,
        CloseSquareBrace,
        OpenCurlBrace,
        CloseCurlBrace,
        OpenParen,
        CloseParen,
        Equals,
        Comma,
        Quote
    }

    static Dictionary<char, Func<Tokenizer, ShaderToken>> symbolHandlers = new()
    {
        {'{', (ctx) => HandleSingleCharToken(ctx, ShaderToken.OpenCurlBrace)},
        {'}', (ctx) => HandleSingleCharToken(ctx, ShaderToken.CloseCurlBrace)},
        {'[', (ctx) => HandleSingleCharToken(ctx, ShaderToken.OpenSquareBrace)},
        {']', (ctx) => HandleSingleCharToken(ctx, ShaderToken.CloseSquareBrace)},
        {'(', (ctx) => HandleSingleCharToken(ctx, ShaderToken.OpenParen)},
        {')', (ctx) => HandleSingleCharToken(ctx, ShaderToken.CloseParen)},
        {'=', (ctx) => HandleSingleCharToken(ctx, ShaderToken.Equals)},
        {',', (ctx) => HandleSingleCharToken(ctx, ShaderToken.Comma)},
    };

    private static string StripComments(string input)
    {
        StringBuilder result = new StringBuilder(input.Length);
        int i = 0;
        bool inString = false;
        bool escapeNext = false;

        while (i < input.Length)
        {
            char c = input[i];

            // Handle string literals
            if (c == '"' && !escapeNext)
            {
                inString = !inString;
                result.Append(c);
                i++;
                continue;
            }

            // Handle escape sequences
            if (c == '\\' && !escapeNext)
            {
                escapeNext = true;
                result.Append(c);
                i++;
                continue;
            }

            escapeNext = false;

            // Don't strip comments inside strings
            if (inString)
            {
                result.Append(c);
                i++;
                continue;
            }

            // Check for single-line comment: //
            if (i + 1 < input.Length && input[i] == '/' && input[i + 1] == '/')
            {
                // Skip everything until end of line
                i += 2;
                while (i < input.Length && input[i] != '\n' && input[i] != '\r')
                {
                    i++;
                }

                // Preserve the newline
                if (i < input.Length && input[i] == '\r')
                {
                    result.Append('\r');
                    i++;
                }
                if (i < input.Length && input[i] == '\n')
                {
                    result.Append('\n');
                    i++;
                }
                continue;
            }

            // Check for multi-line comment: /* */
            if (i + 1 < input.Length && input[i] == '/' && input[i + 1] == '*')
            {
                // Skip everything until we find */
                i += 2;

                while (i + 1 < input.Length)
                {
                    // Preserve newlines to maintain line numbers
                    if (input[i] == '\n')
                    {
                        result.Append('\n');
                        i++;
                        continue;
                    }
                    if (input[i] == '\r')
                    {
                        result.Append('\r');
                        i++;
                        continue;
                    }

                    // Check for closing */
                    if (input[i] == '*' && input[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }

                    i++;
                }
                continue;
            }

            // Regular character - keep it
            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    private static Tokenizer<ShaderToken> CreateTokenizer(string input)
    {
        return new(
            input.AsMemory(),
            symbolHandlers,
            "{}()=,".Contains,
            ShaderToken.Identifier,
            ShaderToken.None,
            HandleCommentWhitespace
        );
    }

    public static bool ParseShader(string sourceFilePath, string input, out Shader? shader) =>
        ParseShader(sourceFilePath, input, null, out shader);

    public static bool ParseShader(string sourceFilePath, string input, Func<string, string?>? includeResolver, out Shader? shader)
    {
        shader = null;

        // Strip all comments before tokenization
        input = StripComments(input);

        Tokenizer<ShaderToken> tokenizer = CreateTokenizer(input);

        string name = "";

        List<ShaderProperty>? properties = null;
        List<ParsedPass> parsedPasses = [];

        string? fallback = null;

        try
        {
            tokenizer.MoveNext();

            if (!tokenizer.Token.ToString().Equals("Shader", StringComparison.Ordinal))
                throw new ParseException("shader", $"expected top-level 'Shader' declaration, found '{tokenizer.Token}'");

            tokenizer.MoveNext(); // Move to string

            name = tokenizer.ParseQuotedStringValue();

            while (tokenizer.MoveNext())
            {
                switch (tokenizer.Token.ToString())
                {
                    case "Properties":
                        EnsureUndef(properties, "Properties block");
                        properties = ParseProperties(tokenizer);
                        break;

                    case "Pass":
                        parsedPasses.Add(ParsePass(tokenizer));
                        break;

                    case "Fallback":
                        tokenizer.MoveNext(); // Move to string
                        fallback = tokenizer.ParseQuotedStringValue();
                        break;

                    default:
                        throw new ParseException("shader", $"unknown shader token: {tokenizer.Token} On line {tokenizer.CurrentLine} column {tokenizer.CurrentColumn}");
                }
            }
        }
        catch (Exception ex) when (ex is ParseException || ex is InvalidDataException || ex is EndOfStreamException)
        {
            LogCompilationError(sourceFilePath, ex.Message, tokenizer.CurrentLine, tokenizer.CurrentColumn);
            return false;
        }

        ShaderPass[] passes = new ShaderPass[parsedPasses.Count];

        for (int i = 0; i < passes.Length; i++)
        {
            ParsedPass parsedPass = parsedPasses[i];

            StringBuilder sourceBuilder = new(parsedPass.Program);

            string sourceCode = sourceBuilder.ToString();

            // Parse the shader sections
            if (!ExtractShaderSections(sourceFilePath, parsedPass.Program, parsedPass.ProgramStartLine,
                                      out string sharedCode, out string vertexCode, out string fragmentCode))
            {
                return false;
            }

            string vertexShader = sharedCode + "\n" + vertexCode;
            string fragmentShader = sharedCode + "\n" + fragmentCode;

            string ImportReplacer(Match match)
            {
                string relativePath = match.Groups[1].Value + ".glsl";
                string? includeContent = includeResolver != null
                    ? ResolveWithCustomResolver(relativePath)
                    : ResolveFromFileSystem(relativePath);

                if (includeContent == null)
                    return string.Empty;

                // Recursively handle nested imports
                return _preprocessorIncludeRegex.Replace($"\n{RemoveBom(includeContent)}\n", ImportReplacer);
            }

            string? ResolveWithCustomResolver(string relativePath)
            {
                string? resolvedPath = ResolveEmbeddedIncludePath(sourceFilePath, relativePath);
                if (resolvedPath == null)
                {
                    LogCompilationError(sourceFilePath, $"Failed to Import Shader. Include not found: {relativePath}", parsedPass.Line, 0);
                    return null;
                }

                string? content = includeResolver!(resolvedPath);
                if (content == null)
                    LogCompilationError(sourceFilePath, $"Failed to Import Shader. Include not found: {resolvedPath}", parsedPass.Line, 0);

                return content;
            }

            string? ResolveFromFileSystem(string relativePath)
            {
                string absolutePath = Path.GetFullPath(Path.Combine(new FileInfo(sourceFilePath).Directory!.FullName, relativePath));
                if (!File.Exists(absolutePath))
                {
                    LogCompilationError(sourceFilePath, $"Failed to Import Shader. Include not found: {absolutePath}", parsedPass.Line, 0);
                    return null;
                }

                return File.ReadAllText(absolutePath);
            }

            vertexShader = _preprocessorIncludeRegex.Replace(vertexShader, ImportReplacer);
            fragmentShader = _preprocessorIncludeRegex.Replace(fragmentShader, ImportReplacer);

            if (string.IsNullOrEmpty(vertexShader))
            {
                LogCompilationError(sourceFilePath, "Failed to compile shader pass of " + parsedPass.Name + ". Vertex Shader is null or empty.", parsedPass.Line, 0);
                return false;
            }

            if (string.IsNullOrEmpty(fragmentShader))
            {
                LogCompilationError(sourceFilePath, "Failed to compile shader pass of " + parsedPass.Name + ". Fragment Shader is null or empty.", parsedPass.Line, 0);
                return false;
            }

            passes[i] = new ShaderPass(parsedPass.Name, parsedPass.Tags, parsedPass.TagSortOffsets, parsedPass.State, vertexShader, fragmentShader, fallback, parsedPass.GrabTextureName ?? "");
        }

        shader = new Shader(name, [.. properties ?? []], passes);

        return true;
    }

    private static string RemoveBom(string content)
    {
        // Remove BOM if present
        if (content.Length > 0 && content[0] == '\uFEFF')
            return content.Substring(1);
        return content;
    }

    private static bool ExtractShaderSections(string sourceFilePath, string program, int startLine,
                                     out string sharedCode, out string vertexCode, out string fragmentCode)
    {
        sharedCode = "";
        vertexCode = "";
        fragmentCode = "";
        try
        {
            // Extract sections using balanced brace matching
            sharedCode = ExtractSectionContent(program, "Shared");
            vertexCode = ExtractSectionContent(program, "Vertex");
            fragmentCode = ExtractSectionContent(program, "Fragment");

            if (string.IsNullOrEmpty(vertexCode))
            {
                LogCompilationError(sourceFilePath, "Missing VertexShader section", startLine, 0);
                return false;
            }

            if (string.IsNullOrEmpty(fragmentCode))
            {
                LogCompilationError(sourceFilePath, "Missing FragmentShader section", startLine, 0);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogCompilationError(sourceFilePath, $"Error parsing shader sections: {ex.Message}", startLine, 0);
            return false;
        }
    }

    private static string ExtractSectionContent(string program, string sectionName)
    { // Look for the exact section name followed by opening brace
        string pattern = @"\b" + sectionName + @"\s*\{";
        Match match = Regex.Match(program, pattern, RegexOptions.IgnoreCase);

        if (!match.Success)
            return "";

        int openBracePos = match.Index + match.Length - 1;
        int braceCount = 1;
        int contentStart = openBracePos + 1;
        int contentEnd = contentStart;

        // Track string literals to avoid counting braces inside strings
        bool inString = false;
        bool escapeNext = false;

        while (contentEnd < program.Length && braceCount > 0)
        {
            char c = program[contentEnd];

            // Handle string literals
            if (c == '"' && !escapeNext)
                inString = !inString;

            // Only count braces outside of string literals
            if (!inString)
            {
                if (c == '{') braceCount++;
                else if (c == '}') braceCount--;
            }

            // Handle escape sequences in strings
            escapeNext = (c == '\\' && !escapeNext);

            contentEnd++;
        }

        if (braceCount == 0)
        {
            return program.Substring(contentStart, contentEnd - contentStart - 1).Trim();
        }

        return "";
    }

    private static void LogCompilationError(string sourceFilePath, string message, int line, int column)
    {
        DebugStackFrame frame = new(sourceFilePath, line, column);
        DebugStackTrace trace = new(frame);

        Debug.Log("Error compiling shader: " + message, LogSeverity.Error, trace);
    }

    private static bool HandleCommentWhitespace(char c, Tokenizer tokenizer)
    {
        // Use explicit whitespace check to avoid culture-dependent behavior
        return c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }


    private static ShaderToken HandleSingleCharToken(Tokenizer tokenizer, ShaderToken tokenType)
    {
        tokenizer.TokenMemory = tokenizer.Input.Slice(tokenizer.TokenPosition, 1);
        tokenizer.IncrementInputPosition();

        return tokenType;
    }


    private static List<ShaderProperty> ParseProperties(Tokenizer<ShaderToken> tokenizer)
    {
        List<ShaderProperty> properties = [];

        ExpectToken("properties", tokenizer, ShaderToken.OpenCurlBrace);

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            if (tokenizer.TokenType == ShaderToken.Equals)
            {
                if (properties.Count == 0)
                    throw new ParseException("properties", tokenizer, ShaderToken.Identifier);

                ShaderProperty last = properties[^1];

                ShaderProperty def = ParseDefault(tokenizer, last.PropertyType);

                def.Name = last.Name;
                def.DisplayName = last.DisplayName;

                properties[^1] = def;

                tokenizer.MoveNext();

                if (tokenizer.TokenType == ShaderToken.CloseCurlBrace)
                    break;
            }

            string name = tokenizer.Token.ToString();

            ExpectToken("property", tokenizer, ShaderToken.OpenParen);

            ExpectToken("property", tokenizer, ShaderToken.Identifier);
            string displayName = tokenizer.ParseQuotedStringValue();

            ExpectToken("property", tokenizer, ShaderToken.Comma);
            ExpectToken("property", tokenizer, ShaderToken.Identifier);

            ShaderPropertyType type = EnumParse<ShaderPropertyType>(tokenizer.Token.ToString(), "property type");

            ExpectToken("property", tokenizer, ShaderToken.CloseParen);

            ShaderProperty property = type switch
            {
                ShaderPropertyType.Float => 0,
                ShaderPropertyType.Vector2 => Float2.Zero,
                ShaderPropertyType.Vector3 => Float3.Zero,
                ShaderPropertyType.Vector4 => Float4.Zero,
                ShaderPropertyType.Color => Color.White,
                ShaderPropertyType.Matrix => Float4x4.Identity,
                ShaderPropertyType.Texture2D => Texture2D.White,
                ShaderPropertyType.Texture3D => Texture3D.White,
                _ => throw new Exception($"Invalid property type") // Should never execute unless EnumParse() breaks.
            };

            property.Name = name;
            property.DisplayName = displayName;

            properties.Add(property);
        }

        return properties;
    }


    private static ShaderProperty ParseDefault(Tokenizer<ShaderToken> tokenizer, ShaderPropertyType type)
    {
        switch (type)
        {
            case ShaderPropertyType.Float:
                ExpectToken("property", tokenizer, ShaderToken.Identifier);
                return DoubleParse(tokenizer.Token, "decimal value");

            case ShaderPropertyType.Vector2:
                float[] v2 = VectorParse(tokenizer, 2);
                return new Float2(v2[0], v2[1]);

            case ShaderPropertyType.Vector3:
                float[] v3 = VectorParse(tokenizer, 3);
                return new Float3(v3[0], v3[1], v3[2]);

            case ShaderPropertyType.Color:
                float[] col = VectorParse(tokenizer, 4);
                return new Color((float)col[0], (float)col[1], (float)col[2], (float)col[3]);

            case ShaderPropertyType.Vector4:
                float[] v4 = VectorParse(tokenizer, 4);
                return new Float4(v4[0], v4[1], v4[2], v4[3]);

            case ShaderPropertyType.Matrix:
                throw new ParseException("property", "matrix properties are only assignable programatically and cannot be assigned defaults");

            case ShaderPropertyType.Texture2D:
                ExpectToken("property", tokenizer, ShaderToken.Identifier);
                return Texture2DParse(tokenizer.ParseQuotedStringValue());

            case ShaderPropertyType.Texture3D:
                ExpectToken("property", tokenizer, ShaderToken.Identifier);
                return Texture3DParse(tokenizer.ParseQuotedStringValue());
        }

        throw new Exception($"Invalid property type");
    }

    private static ParsedPass ParsePass(Tokenizer<ShaderToken> tokenizer)
    {
        var pass = new ParsedPass();

        pass.Line = tokenizer.CurrentLine;

        if (tokenizer.MoveNext() && tokenizer.TokenType == ShaderToken.Identifier)
        {
            pass.Name = tokenizer.ParseQuotedStringValue();
            ExpectToken("pass", tokenizer, ShaderToken.OpenCurlBrace);
        }
        else if (tokenizer.TokenType != ShaderToken.OpenCurlBrace)
        {
            throw new ParseException("pass", $"{ShaderToken.OpenCurlBrace} or {ShaderToken.Identifier}", tokenizer.TokenType);
        }

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            switch (tokenizer.Token.ToString())
            {
                case "Tags":
                    EnsureUndef(pass.Tags, "'Tags' in pass");
                    (pass.Tags, pass.TagSortOffsets) = ParseTags(tokenizer);
                    break;

                case "GrabTexture":
                    EnsureUndef(pass.GrabTextureName, "'GrabTexture' in pass");
                    ExpectToken("grabtexture", tokenizer, ShaderToken.Identifier);
                    pass.GrabTextureName = tokenizer.ParseQuotedStringValue();
                    break;

                case "Blend":
                    ParseBlendState(tokenizer, ref pass.State);
                    break;

                case "Cull":
                    ExpectToken("cull", tokenizer, ShaderToken.Identifier);
                    pass.State.CullFace = ParseCullMode(tokenizer.Token.ToString());
                    break;

                case "ZTest":
                    ExpectToken("ztest", tokenizer, ShaderToken.Identifier);
                    if (tokenizer.Token.ToString().Equals("Off", StringComparison.OrdinalIgnoreCase))
                    {
                        pass.State.DepthTest = false;
                    }
                    else
                    {
                        pass.State.DepthTest = true;
                        pass.State.Depth = EnumParse<RasterizerState.DepthMode>(tokenizer.Token.ToString(), "Z test");
                    }
                    break;

                case "ZWrite":
                    ExpectToken("zwrite", tokenizer, ShaderToken.Identifier);
                    pass.State.DepthWrite = BoolParse(tokenizer.Token, "Z write");
                    break;

                case "Winding":
                    ExpectToken("winding", tokenizer, ShaderToken.Identifier);
                    pass.State.Winding = EnumParse<RasterizerState.WindingOrder>(tokenizer.Token.ToString(), "winding order");
                    break;

                case "GLSLPROGRAM":
                    pass.ProgramStartLine = tokenizer.CurrentLine;
                    EnsureUndef(pass.Program, "'GLSLPROGRAM' in pass");
                    SliceTo(tokenizer, "ENDGLSL");
                    pass.Program = tokenizer.Token.ToString();
                    pass.ProgramLines = (tokenizer.CurrentLine - pass.ProgramStartLine) + 1;
                    break;

                default:
                    throw new ParseException("pass", $"unknown pass token: {tokenizer.Token}");
            }
        }

        if (pass.Program == null)
            throw new ParseException("pass", "pass does not contain a program");

        return pass;
    }

    private static (Dictionary<string, string>, Dictionary<string, int>) ParseTags(Tokenizer<ShaderToken> tokenizer)
    {
        var tags = new Dictionary<string, string>();
        var tagSortOffsets = new Dictionary<string, int>();
        ExpectToken("tags", tokenizer, ShaderToken.OpenCurlBrace);

        //while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
        while (true)
        {
            ExpectToken("tags", tokenizer, ShaderToken.Identifier);
            string key = tokenizer.ParseQuotedStringValue();

            ExpectToken("tags", tokenizer, ShaderToken.Equals);
            ExpectToken("tags", tokenizer, ShaderToken.Identifier);

            string rawValue = tokenizer.ParseQuotedStringValue();

            // Parse sort offset from value (e.g., "Transparent+1000" -> "Transparent", offset=1000)
            string value = rawValue;
            int sortOffset = 0;

            // Look for + or - followed by a number
            int plusIndex = rawValue.LastIndexOf('+');
            int minusIndex = rawValue.LastIndexOf('-');
            int offsetIndex = Maths.Max(plusIndex, minusIndex);

            if (offsetIndex > 0) // Must be after at least one character
            {
                string offsetStr = rawValue.Substring(offsetIndex);
                if (int.TryParse(offsetStr, out sortOffset))
                {
                    // Successfully parsed offset, extract base value
                    value = rawValue.Substring(0, offsetIndex);
                    tagSortOffsets[key] = sortOffset;
                }
            }

            tags[key] = value;

            // Next token should either be a comma or a closing brace
            // if it's a comma there's another tag so continue, if not break
            tokenizer.MoveNext();

            if (tokenizer.TokenType == ShaderToken.Comma)
                continue;

            if (tokenizer.TokenType == ShaderToken.CloseCurlBrace)
                break;

            throw new ParseException("tags", $"{ShaderToken.Comma} or {ShaderToken.CloseCurlBrace}", tokenizer.TokenType);
        }

        return (tags, tagSortOffsets);
    }

    private static void ParseBlendState(Tokenizer<ShaderToken> tokenizer, ref RasterizerState state)
    {
        // Enable blending by default
        state.DoBlend = true;

        if (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.OpenCurlBrace)
        {
            string preset = tokenizer.Token.ToString();

            if (preset.Equals("Additive", StringComparison.OrdinalIgnoreCase))
            {
                state.BlendSrc = RasterizerState.Blending.One;
                state.BlendDst = RasterizerState.Blending.One;
                state.Blend = RasterizerState.BlendMode.Add;
            }
            else if (preset.Equals("Alpha", StringComparison.OrdinalIgnoreCase))
            {
                state.BlendSrc = RasterizerState.Blending.SrcAlpha;
                state.BlendDst = RasterizerState.Blending.OneMinusSrcAlpha;
                state.Blend = RasterizerState.BlendMode.Add;
            }
            else if (preset.Equals("Override", StringComparison.OrdinalIgnoreCase))
            {
                state.BlendSrc = RasterizerState.Blending.One;
                state.BlendDst = RasterizerState.Blending.Zero;
                state.Blend = RasterizerState.BlendMode.Add;
            }
            else if (preset.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                state.DoBlend = false;
            }
            else
                throw new ParseException("blend state", $"unknown blend preset '{preset}' (valid presets: Additive, Alpha, Override, Off)");

            return;
        }

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            string key = tokenizer.Token.ToString();

            switch (key)
            {
                case "Src":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    state.BlendSrc = EnumParse<RasterizerState.Blending>(tokenizer.Token.ToString(), "Src blend factor");
                    break;

                case "Dst":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    state.BlendDst = EnumParse<RasterizerState.Blending>(tokenizer.Token.ToString(), "Dst blend factor");
                    break;

                case "Mode":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    state.Blend = EnumParse<RasterizerState.BlendMode>(tokenizer.Token.ToString(), "blend mode");
                    break;

                default:
                    throw new ParseException("blend state", $"unknown blend key: {key}");
            }
        }
    }

    private static RasterizerState.PolyFace ParseCullMode(string cullMode)
    {
        if (cullMode.Equals("Off", StringComparison.OrdinalIgnoreCase))
            return RasterizerState.PolyFace.None;

        return EnumParse<RasterizerState.PolyFace>(cullMode, "cull mode", "Off");
    }

    private static void ExpectToken(string type, Tokenizer<ShaderToken> tokenizer, ShaderToken expectedType)
    {
        tokenizer.MoveNext();

        if (tokenizer.TokenType != expectedType)
            throw new ParseException(type, expectedType, $"{tokenizer.TokenType} ('{tokenizer.Token}')");
    }


    public static bool SliceTo(Tokenizer tokenizer, string token)
    {
        int startPos = tokenizer.InputPosition;

        while (tokenizer.MoveNext())
        {
            if (tokenizer.Token.ToString().Equals(token, StringComparison.Ordinal))
            {
                tokenizer.TokenMemory = tokenizer.Input.Slice(startPos, tokenizer.InputPosition - tokenizer.Token.Length - startPos);

                return true;
            }
        }
        return false;
    }


    private static void EnsureUndef(object? value, string property)
    {
        if (value != null)
            throw new ParseException(property, $"redefinition of {property}");
    }


    private static T EnumParse<T>(ReadOnlySpan<char> text, string fieldName, params string[] extraValues) where T : struct, Enum
    {
        if (Enum.TryParse(text, true, out T value))
            return value;

        List<string> values = [.. Enum.GetNames<T>()];
        values.AddRange(extraValues);

        throw new ParseException(fieldName, $"unknown value '{text.ToString()}' (possible values: [{string.Join(", ", values)}])");
    }

    private static float DoubleParse(ReadOnlySpan<char> text, string fieldName)
    {
        try
        {
            return float.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            throw new ParseException(fieldName, $"incorrect format. Attempted to parse '{text.ToString()}' as a number");
        }
        catch (OverflowException)
        {
            throw new ParseException(fieldName, $"value is too large. Attempted to parse '{text.ToString()}'");
        }
    }


    private static float[] VectorParse(Tokenizer<ShaderToken> tokenizer, int dimensions)
    {
        ExpectToken("vector", tokenizer, ShaderToken.OpenParen);

        float[] vector = new float[dimensions];
        int count = 0;

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseParen)
        {
            try
            {
                vector[count] = DoubleParse(tokenizer.Token, $"vector element at index {count}");
            }
            catch (ParseException ex)
            {
                throw new ParseException("vector", $"failed to parse element {count} of {dimensions}-dimensional vector. {ex.Message}");
            }

            if (count != dimensions - 1)
                ExpectToken("vector", tokenizer, ShaderToken.Comma);

            if (count >= dimensions)
                throw new ParseException("vector", $"too many elements: expected {dimensions}, got {count + 1}+");

            count++;
        }

        if (count < dimensions - 1)
            throw new ParseException("vector", $"not enough elements: expected {dimensions}, got {count}");

        return vector;
    }


    private static Texture2D Texture2DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture2D.White,
            "gray" or "grey" => Texture2D.Gray,
            "grid" => Texture2D.Grid,
            "black" or "emission" => Texture2D.Emission,
            "normal" => Texture2D.Normal,
            "surface" => Texture2D.Surface,
            "noise" => Texture2D.Noise,
            _ => throw new ParseException("texture 2d", $"unknown texture default: {texture}")
        };
    }


    private static Texture3D Texture3DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture3D.White,
            _ => throw new ParseException("texture 3d", $"unknown texture default: {texture}")
        };
    }

    private static bool BoolParse(ReadOnlySpan<char> text, string fieldName)
    {
        text = text.Trim();

        if (text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new ParseException(fieldName, $"incorrect format. Attempted to parse '{text.ToString()}' as boolean (expected 'On' or 'Off')");
    }

    public class ParsedPass
    {
        public int Line;
        public string Name = "";

        public Dictionary<string, string>? Tags = null;
        public Dictionary<string, int>? TagSortOffsets = null;
        public RasterizerState State = new();

        public int ProgramStartLine;
        public int ProgramLines;
        public string? Program;

        public string? GrabTextureName = null;
    }

    public struct EntryPoint(ShaderStages stages, string name)
    {
        public ShaderStages Stage = stages;
        public string Name = name;
    }

    private static string? ResolveEmbeddedIncludePath(string sourceFilePath, string relativePath)
    {
        string sourceDir = Path.GetDirectoryName(sourceFilePath) ?? "";
        return Path.Combine(sourceDir, relativePath).Replace('\\', '/');
    }

    internal class ParseException : Exception
    {
        public ParseException(string type, object message) :
            base($"Error parsing {type}: {message}")
        { }

        public ParseException(string type, object expected, object found) :
            base($"Error parsing {type}: expected {expected}, found {found}.")
        { }
    }
}
