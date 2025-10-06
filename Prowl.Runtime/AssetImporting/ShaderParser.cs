// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Utils;

using Shader = Prowl.Runtime.Resources.Shader;
using Texture2D = Prowl.Runtime.Resources.Texture2D;

namespace Prowl.Runtime.AssetImporting;

public static class ShaderParser
{
    private static readonly Regex _preprocessorIncludeRegex = new Regex(@"^\s*#include\s*[""<](.+?)["">]\s*$", RegexOptions.Multiline);

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

    public static bool ParseShader(string sourceFilePath, string input, out Shader? shader)
    {
        shader = null;

        Tokenizer<ShaderToken> tokenizer = CreateTokenizer(input);

        string name = "";

        List<ShaderProperty>? properties = null;
        List<ParsedPass> parsedPasses = [];

        string? fallback = null;

        try
        {
            tokenizer.MoveNext();

            if (tokenizer.Token.ToString() != "Shader")
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
                var relativePath = match.Groups[1].Value + ".glsl";

                var combined = Path.Combine(new FileInfo(sourceFilePath).Directory!.FullName, relativePath);
                string absolutePath = Path.GetFullPath(combined);
                if (!File.Exists(absolutePath))
                {
                    LogCompilationError(sourceFilePath, "Failed to Import Shader. Include not found: " + absolutePath, parsedPass.Line, 0);
                    return string.Empty;
                }

                // Recursively handle Imports
                var includeScript = _preprocessorIncludeRegex.Replace("\n" + RemoveBom(File.ReadAllText(absolutePath)) + "\n", ImportReplacer);
                return includeScript;
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

            passes[i] = new ShaderPass(parsedPass.Name, parsedPass.Tags, parsedPass.State, vertexShader, fragmentShader, fallback);
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
        if (char.IsWhiteSpace(c))
            return true;

        if (c != '/')
            return false;

        if (tokenizer.InputPosition + 1 >= tokenizer.Input.Length)
            return false;

        // Look ahead
        char next = tokenizer.Input.Span[tokenizer.InputPosition + 1];

        if (next == '/')
        {
            int line = tokenizer.CurrentLine;

            while (line == tokenizer.CurrentLine)
                tokenizer.IncrementInputPosition();

            return true;
        }

        if (next == '*')
        {
            while (tokenizer.InputPosition + 2 < tokenizer.Input.Length)
            {
                if (tokenizer.Input.Slice(tokenizer.InputPosition, 2).ToString() == "*/")
                    break;

                tokenizer.IncrementInputPosition();
            }

            // Skip the last '*/'
            tokenizer.IncrementInputPosition();
            tokenizer.IncrementInputPosition();

            return true;
        }

        return false;
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

            ShaderProperty property = type switch {
                ShaderPropertyType.Float => 0,
                ShaderPropertyType.Vector2 => Vector2.zero,
                ShaderPropertyType.Vector3 => Vector3.zero,
                ShaderPropertyType.Vector4 => Vector4.zero,
                ShaderPropertyType.Color => Color.white,
                ShaderPropertyType.Matrix => Matrix4x4.Identity,
                ShaderPropertyType.Texture2D => Texture2D.White,
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
                double[] v2 = VectorParse(tokenizer, 2);
                return new Vector2(v2[0], v2[1]);

            case ShaderPropertyType.Vector3:
                double[] v3 = VectorParse(tokenizer, 3);
                return new Vector3(v3[0], v3[1], v3[2]);

            case ShaderPropertyType.Color:
                double[] col = VectorParse(tokenizer, 4);
                return new Color((float)col[0], (float)col[1], (float)col[2], (float)col[3]);

            case ShaderPropertyType.Vector4:
                double[] v4 = VectorParse(tokenizer, 4);
                return new Vector4(v4[0], v4[1], v4[2], v4[3]);

            case ShaderPropertyType.Matrix:
                throw new ParseException("property", "matrix properties are only assignable programatically and cannot be assigned defaults");

            case ShaderPropertyType.Texture2D:
                ExpectToken("property", tokenizer, ShaderToken.Identifier);
                return Texture2DParse(tokenizer.ParseQuotedStringValue());
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
                    pass.Tags = ParseTags(tokenizer);
                    break;

                case "Blend":
                    ParseBlendState(tokenizer, pass.State);
                    break;

                case "Cull":
                    ExpectToken("cull", tokenizer, ShaderToken.Identifier);
                    pass.State.cullFace = ParseCullMode(tokenizer.Token.ToString());
                    break;

                case "ZTest":
                    ExpectToken("ztest", tokenizer, ShaderToken.Identifier);
                    if (tokenizer.Token.ToString().Equals("Off", StringComparison.OrdinalIgnoreCase))
                    {
                        pass.State.depthTest = false;
                    }
                    else
                    {
                        pass.State.depthTest = true;
                        pass.State.depthMode = EnumParse<RasterizerState.DepthMode>(tokenizer.Token.ToString(), "Z test");
                    }
                    break;

                case "ZWrite":
                    ExpectToken("zwrite", tokenizer, ShaderToken.Identifier);
                    pass.State.depthWrite = BoolParse(tokenizer.Token, "Z write");
                    break;

                case "Winding":
                    ExpectToken("winding", tokenizer, ShaderToken.Identifier);
                    pass.State.winding = EnumParse<RasterizerState.WindingOrder>(tokenizer.Token.ToString(), "winding order");
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


    private static Dictionary<string, string> ParseTags(Tokenizer<ShaderToken> tokenizer)
    {
        var tags = new Dictionary<string, string>();
        ExpectToken("tags", tokenizer, ShaderToken.OpenCurlBrace);

        //while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
        while (true)
        {
            ExpectToken("tags", tokenizer, ShaderToken.Identifier);
            string key = tokenizer.ParseQuotedStringValue();

            ExpectToken("tags", tokenizer, ShaderToken.Equals);
            ExpectToken("tags", tokenizer, ShaderToken.Identifier);

            string value = tokenizer.ParseQuotedStringValue();
            tags[key] = value;

            // Next token should either be a comma or a closing brace
            // if its a comma theres another tag so continue, if not break
            tokenizer.MoveNext();

            if (tokenizer.TokenType == ShaderToken.Comma)
                continue;

            if (tokenizer.TokenType == ShaderToken.CloseCurlBrace)
                break;

            throw new ParseException("tags", $"{ShaderToken.Comma} or {ShaderToken.CloseCurlBrace}", tokenizer.TokenType);
        }

        return tags;
    }

    private static void ParseBlendState(Tokenizer<ShaderToken> tokenizer, RasterizerState state)
    {
        // Enable blending by default
        state.doBlend = true;

        if (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.OpenCurlBrace)
        {
            string preset = tokenizer.Token.ToString();

            if (preset.Equals("Additive", StringComparison.OrdinalIgnoreCase))
            {
                state.blendSrc = RasterizerState.Blending.One;
                state.blendDst = RasterizerState.Blending.One;
                state.blendMode = RasterizerState.BlendMode.Add;
            }
            else if (preset.Equals("Alpha", StringComparison.OrdinalIgnoreCase))
            {
                state.blendSrc = RasterizerState.Blending.SrcAlpha;
                state.blendDst = RasterizerState.Blending.OneMinusSrcAlpha;
                state.blendMode = RasterizerState.BlendMode.Add;
            }
            else if (preset.Equals("Override", StringComparison.OrdinalIgnoreCase))
            {
                state.blendSrc = RasterizerState.Blending.One;
                state.blendDst = RasterizerState.Blending.Zero;
                state.blendMode = RasterizerState.BlendMode.Add;
            }
            else if (preset.Equals("Off", StringComparison.OrdinalIgnoreCase))
            {
                state.doBlend = false;
            }
            else
                throw new ParseException("blend state", "unknown blend preset: " + preset);

            return;
        }

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            var key = tokenizer.Token.ToString();

            switch (key)
            {
                case "Src":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    state.blendSrc = EnumParse<RasterizerState.Blending>(tokenizer.Token.ToString(), "Src blend factor");
                    break;

                case "Dst":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    state.blendDst = EnumParse<RasterizerState.Blending>(tokenizer.Token.ToString(), "Dst blend factor");
                    break;

                case "Mode":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    state.blendMode = EnumParse<RasterizerState.BlendMode>(tokenizer.Token.ToString(), "blend mode");
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
            throw new ParseException(type, expectedType, tokenizer.TokenType);
    }


    public static bool SliceTo(Tokenizer tokenizer, string token)
    {
        int startPos = tokenizer.InputPosition;

        while (tokenizer.MoveNext())
        {
            if (tokenizer.Token.ToString() == token)
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

        throw new ParseException(fieldName, $"unknown value (possible values: [{string.Join(", ", values)}])");
    }

    private static double DoubleParse(ReadOnlySpan<char> text, string fieldName)
    {
        try
        {
            return double.Parse(text);
        }
        catch (FormatException)
        {
            throw new ParseException(fieldName, "incorrect format");
        }
        catch (OverflowException)
        {
            throw new ParseException(fieldName, "value is too large");
        }
    }


    private static double[] VectorParse(Tokenizer<ShaderToken> tokenizer, int dimensions)
    {
        ExpectToken("vector", tokenizer, ShaderToken.OpenParen);

        double[] vector = new double[dimensions];
        int count = 0;

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseParen)
        {
            vector[count] = DoubleParse(tokenizer.Token, "vector element");

            if (count != dimensions - 1)
                ExpectToken("vector", tokenizer, ShaderToken.Comma);

            if (count >= dimensions)
                throw new ParseException("vector", dimensions, $"{count}+");

            count++;
        }

        if (count < dimensions - 1)
            throw new ParseException("vector", dimensions, $"{count}+");

        return vector;
    }


    private static AssetRef<Texture2D> Texture2DParse(string texture)
    {
        return texture switch {
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

    private static bool BoolParse(ReadOnlySpan<char> text, string fieldName)
    {
        text = text.Trim();

        if (text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new ParseException(fieldName, "incorrect format");
    }

    public class ParsedPass
    {
        public int Line;
        public string Name = "";

        public Dictionary<string, string>? Tags = null;
        public RasterizerState State = new();

        public int ProgramStartLine;
        public int ProgramLines;
        public string? Program;
    }

    public struct EntryPoint(ShaderStages stages, string name)
    {
        public ShaderStages Stage = stages;
        public string Name = name;
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
