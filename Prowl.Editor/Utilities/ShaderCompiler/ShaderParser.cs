// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Prowl.Runtime.Rendering;

using Veldrid;

using Debug = Prowl.Runtime.Debug;
using Shader = Prowl.Runtime.Shader;

namespace Prowl.Editor;

public static class ShaderParser
{
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


    public static bool ParseShader(string input, FileIncluder includer, out Shader? shader)
    {
        shader = null;

        Tokenizer<ShaderToken> tokenizer = CreateTokenizer(input);

        string name = "";

        List<ShaderProperty>? properties = null;
        ParsedPass? globalDefaults = null;
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

                    case "Global":
                        EnsureUndef(globalDefaults, "Global block");
                        globalDefaults = ParseGlobal(tokenizer);
                        break;

                    case "Pass":
                        parsedPasses.Add(ParsePass(tokenizer));
                        break;

                    case "Fallback":
                        tokenizer.MoveNext(); // Move to string
                        fallback = tokenizer.ParseQuotedStringValue();
                        break;

                    default:
                        throw new ParseException("shader", $"unknown shader token: {tokenizer.Token}");
                }
            }
        }
        catch (Exception ex) when (ex is ParseException || ex is InvalidDataException || ex is EndOfStreamException)
        {
            LogCompilationError(ex.Message, includer, tokenizer.CurrentLine, tokenizer.CurrentColumn);
            return false;
        }

        ShaderPass[] passes = new ShaderPass[parsedPasses.Count];

        for (int i = 0; i < passes.Length; i++)
        {
            ParsedPass parsedPass = parsedPasses[i];

            ShaderPassDescription passDesc = parsedPass.Description;
            StringBuilder sourceBuilder = new(parsedPass.Program);

            if (globalDefaults != null)
            {
                passDesc.ApplyDefaults(globalDefaults.Description);

                sourceBuilder.Insert(0, '\n');
                sourceBuilder.Insert(0, globalDefaults.Program);
            }

            string sourceCode = sourceBuilder.ToString();

            ShaderCreationArgs args;
            args.combinations = passDesc.Keywords ?? new() { { "", [""] } };
            args.sourceCode = sourceCode;

            if (!ParseProgramInfo(sourceCode, includer, parsedPass.ProgramStartLine, out args.entryPoints, out args.shaderModel))
                return false;

            if (!Array.Exists(args.entryPoints, x => x.Stage == ShaderStages.Vertex))
            {
                LogCompilationError($"Pass {i} does not contain a vertex stage", includer, parsedPass.Line, 0);
                return false;
            }

            if (!Array.Exists(args.entryPoints, x => x.Stage == ShaderStages.Fragment))
            {
                LogCompilationError($"Pass {i} does not contain a fragment stage.", includer, parsedPass.Line, 0);
                return false;
            }

            List<CompilationMessage> compilerMessages = [];

            ShaderVariant[] variants = ShaderCompiler.GenerateVariants(args, includer, compilerMessages);

            LogCompilationMessages(name, compilerMessages, includer, parsedPass.ProgramStartLine, globalDefaults);

            foreach (ShaderVariant variant in variants)
            {
                if (variant == null)
                    return false;
            }

            passes[i] = new ShaderPass(parsedPass.Name, passDesc, variants);
        }

        shader = new Shader(name, [.. properties ?? []], passes);

        return true;
    }


    private static void LogCompilationError(string message, FileIncluder includer, int line, int column)
    {
        DebugStackFrame frame = new(includer.SourceFilePath, line, column);
        DebugStackTrace trace = new(frame);

        Debug.Log("Error compiling shader: " + message, LogSeverity.Error, trace);
    }


    private static void LogCompilationMessages(string shaderName, List<CompilationMessage> messages, FileIncluder includer, int programStartLine, ParsedPass? global)
    {
        foreach (CompilationMessage message in messages)
        {
            int GetLine(int line)
            {
                int msgLine = line - 1;

                line = programStartLine + msgLine; // Offset program-relative line to file line

                if (global != null && global.Program != null) // Global block was added at start
                {
                    if (msgLine - global.ProgramStartLine < 0)
                        line = global!.ProgramStartLine + msgLine; // Line is inside global block - make global-relative
                    else
                        line -= global.ProgramLines; // Line is outside global block - remove the added global block lines
                }

                return line;
            }

            DebugStackTrace? trace = null;

            if (message.file != null)
            {
                CompilationFile file = message.file.Value;

                trace = new(new DebugStackFrame(
                    file.filename ?? "<Unknown File>",
                    file.isSourceFile ? GetLine(file.line) : file.line,
                    file.column
                ));
            }

            string prefix = message.severity switch
            {
                LogSeverity.Normal => "Info",
                LogSeverity.Warning => "Warning",
                _ => "Error",
            };

            prefix += $" compiling {shaderName}: ";

            Debug.Log(prefix + message.message, message.severity, trace);
        }
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

                Debug.Log($"Parsing default {def.Name}");

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
                ShaderPropertyType.Vector2 => Vector2.zero,
                ShaderPropertyType.Vector3 => Vector3.zero,
                ShaderPropertyType.Vector4 => Vector4.zero,
                ShaderPropertyType.Color => Color.white,
                ShaderPropertyType.Matrix => Matrix4x4.Identity,
                ShaderPropertyType.Texture2D => Texture2D.White,
                ShaderPropertyType.Texture3D => new AssetRef<Texture3D>(Texture3D.White),
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
            case ShaderPropertyType.Vector4:
                double[] v4 = VectorParse(tokenizer, 4);
                return new Vector4(v4[0], v4[1], v4[2], v4[3]);

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


    private static ParsedPass ParseGlobal(Tokenizer<ShaderToken> tokenizer)
    {
        ParsedPass parsedGlobal = new();

        ExpectToken("global defaults", tokenizer, ShaderToken.OpenCurlBrace);

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            switch (tokenizer.Token.ToString())
            {
                case "Tags":
                    EnsureUndef(parsedGlobal.Description.Tags, "'Tags' in Global block");
                    parsedGlobal.Description.Tags = ParseTags(tokenizer);
                    break;

                case "Blend":
                    EnsureUndef(parsedGlobal.Description.BlendState, "'Blend' in Global block");
                    parsedGlobal.Description.BlendState = new() { AttachmentStates = [ParseBlend(tokenizer)] };
                    break;

                case "DepthStencil":
                    EnsureUndef(parsedGlobal.Description.DepthStencilState, "'DepthStencil' in Global block");
                    parsedGlobal.Description.DepthStencilState = ParseDepthStencil(tokenizer);
                    break;

                case "Cull":
                    EnsureUndef(parsedGlobal.Description.CullingMode, "'Cull' in Global block");
                    ExpectToken("cull", tokenizer, ShaderToken.Identifier);
                    parsedGlobal.Description.CullingMode = EnumParse<FaceCullMode>(tokenizer.Token.ToString(), "Cull");
                    break;

                case "HLSLINCLUDE":
                    parsedGlobal.ProgramStartLine = tokenizer.CurrentLine;
                    EnsureUndef(parsedGlobal.Program, "'HLSLINCLUDE' in Global block");
                    SliceTo(tokenizer, "ENDHLSL");
                    parsedGlobal.Program = tokenizer.Token.ToString();
                    parsedGlobal.ProgramLines = (tokenizer.CurrentLine - parsedGlobal.ProgramStartLine) + 1;
                    break;

                default:
                    throw new ParseException("global defaults", $"unknown global token: {tokenizer.Token}");
            }
        }

        return parsedGlobal;
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
                    EnsureUndef(pass.Description.Tags, "'Tags' in pass");
                    pass.Description.Tags = ParseTags(tokenizer);
                    break;

                case "Blend":
                    EnsureUndef(pass.Description.BlendState, "'Blend' in pass");
                    pass.Description.BlendState = new() { AttachmentStates = [ParseBlend(tokenizer)] };
                    break;

                case "DepthStencil":
                    EnsureUndef(pass.Description.DepthStencilState, "'DepthStencil' in pass");
                    pass.Description.DepthStencilState = ParseDepthStencil(tokenizer);
                    break;

                case "Cull":
                    EnsureUndef(pass.Description.CullingMode, "'Cull' in pass");
                    ExpectToken("cull", tokenizer, ShaderToken.Identifier);
                    pass.Description.CullingMode = EnumParse<FaceCullMode>(tokenizer.Token.ToString(), "Cull");
                    break;

                case "Features":
                    EnsureUndef(pass.Description.Keywords, "'Features' in pass");
                    pass.Description.Keywords = ParseKeywords(tokenizer);
                    break;

                case "HLSLPROGRAM":
                    pass.ProgramStartLine = tokenizer.CurrentLine;
                    EnsureUndef(pass.Program, "'HLSLPROGRAM' in pass");
                    SliceTo(tokenizer, "ENDHLSL");
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


    private static BlendAttachmentDescription ParseBlend(Tokenizer<ShaderToken> tokenizer)
    {
        BlendAttachmentDescription blend = BlendAttachmentDescription.AdditiveBlend;
        blend.BlendEnabled = true;

        if (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.OpenCurlBrace)
        {
            string preset = tokenizer.Token.ToString();

            if (preset.Equals("Additive", StringComparison.OrdinalIgnoreCase))
                blend = BlendAttachmentDescription.AdditiveBlend;
            else if (preset.Equals("Alpha", StringComparison.OrdinalIgnoreCase))
                blend = BlendAttachmentDescription.AlphaBlend;
            else if (preset.Equals("Override", StringComparison.OrdinalIgnoreCase))
                blend = BlendAttachmentDescription.OverrideBlend;
            else
                throw new ParseException("blend state", "unknown blend preset: " + preset);

            return blend;
        }

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            var key = tokenizer.Token.ToString();
            string target;
            switch (key)
            {
                case "Src":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    target = tokenizer.Token.ToString();
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);

                    if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                        blend.SourceColorFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Src");
                    else
                        blend.SourceAlphaFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Src");
                    break;

                case "Dest":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    target = tokenizer.Token.ToString();
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);

                    if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                        blend.DestinationColorFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Dest");
                    else
                        blend.DestinationAlphaFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Dest");
                    break;

                case "Mode":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    target = tokenizer.Token.ToString();
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);

                    if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                        blend.ColorFunction = EnumParse<BlendFunction>(tokenizer.Token.ToString(), "Mode");
                    else
                        blend.AlphaFunction = EnumParse<BlendFunction>(tokenizer.Token.ToString(), "Mode");
                    break;

                case "Mask":
                    ExpectToken("blend state", tokenizer, ShaderToken.Identifier);
                    string mask = tokenizer.Token.ToString();
                    if (mask.Equals("None", StringComparison.OrdinalIgnoreCase))
                    {
                        blend.ColorWriteMask = ColorWriteMask.None;
                    }
                    else
                    {
                        if (mask.Contains('R')) blend.ColorWriteMask = ColorWriteMask.Red;
                        if (mask.Contains('G')) blend.ColorWriteMask |= ColorWriteMask.Green;
                        if (mask.Contains('B')) blend.ColorWriteMask |= ColorWriteMask.Blue;
                        if (mask.Contains('A')) blend.ColorWriteMask |= ColorWriteMask.Alpha;

                        if (blend.ColorWriteMask == 0)
                            throw new ParseException("blend state", "Invalid color write mask: " + mask);
                    }
                    break;

                default:
                    throw new ParseException("blend state", $"unknown blend key: {key}");
            }
        }

        return blend;
    }


    private static DepthStencilStateDescription ParseDepthStencil(Tokenizer<ShaderToken> tokenizer)
    {
        // No open brace, use a preset
        if (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.OpenCurlBrace)
        {
            return tokenizer.Token switch
            {
                "DepthGreaterEqual" => DepthStencilStateDescription.DepthOnlyGreaterEqual,
                "DepthLessEqual" => DepthStencilStateDescription.DepthOnlyLessEqual,
                "DepthGreaterEqualRead" => DepthStencilStateDescription.DepthOnlyGreaterEqualRead,
                "DepthLessEqualRead" => DepthStencilStateDescription.DepthOnlyLessEqualRead,
                _ => throw new ParseException("depth stencil", $"unknown blend preset: {tokenizer.Token}"),
            };
        }

        DepthStencilStateDescription depthStencil = DepthStencilStateDescription.DepthOnlyLessEqual;

        // Open brace was detected, parse depth stencil settings
        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            switch (tokenizer.Token)
            {
                case "DepthWrite":
                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.DepthWriteEnabled = ConvertToBoolean(tokenizer.Token.ToString());
                    break;

                case "DepthTest":
                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);

                    if (tokenizer.Token.Equals("Off", StringComparison.OrdinalIgnoreCase))
                        depthStencil.DepthTestEnabled = false;
                    else
                        depthStencil.DepthComparison = EnumParse<ComparisonKind>(tokenizer.Token, "DepthTest", "Off");

                    break;

                case "Ref":
                    depthStencil.StencilTestEnabled = true;
                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilReference = ByteParse(tokenizer.Token, "Ref");
                    break;

                case "ReadMask":
                    depthStencil.StencilTestEnabled = true;
                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilReadMask = ByteParse(tokenizer.Token, "ReadMask");
                    break;

                case "WriteMask":
                    depthStencil.StencilTestEnabled = true;
                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilWriteMask = ByteParse(tokenizer.Token, "WriteMask");
                    break;

                case "Comparison":
                    depthStencil.StencilTestEnabled = true;

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilFront.Comparison = EnumParse<ComparisonKind>(tokenizer.Token, "Comparison");

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilBack.Comparison = EnumParse<ComparisonKind>(tokenizer.Token, "Comparison");
                    break;

                case "Pass":
                    depthStencil.StencilTestEnabled = true;

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilFront.Pass = EnumParse<StencilOperation>(tokenizer.Token, "Pass");

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilBack.Pass = EnumParse<StencilOperation>(tokenizer.Token, "Pass");
                    break;

                case "Fail":
                    depthStencil.StencilTestEnabled = true;

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilFront.Fail = EnumParse<StencilOperation>(tokenizer.Token, "Fail");

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilBack.Fail = EnumParse<StencilOperation>(tokenizer.Token, "Fail");
                    break;

                case "ZFail":
                    depthStencil.StencilTestEnabled = true;

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilFront.DepthFail = EnumParse<StencilOperation>(tokenizer.Token, "ZFail");

                    ExpectToken("depth stencil", tokenizer, ShaderToken.Identifier);
                    depthStencil.StencilBack.DepthFail = EnumParse<StencilOperation>(tokenizer.Token, "ZFail");
                    break;

                default:
                    throw new ParseException("depth stencil", $"unknown depth stencil key: {tokenizer.Token}");
            }
        }

        return depthStencil;
    }


    private static Dictionary<string, HashSet<string>> ParseKeywords(Tokenizer<ShaderToken> tokenizer)
    {
        Dictionary<string, HashSet<string>> keywords = new();
        ExpectToken("keywords", tokenizer, ShaderToken.OpenCurlBrace);

        while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
        {
            string name = tokenizer.Token.ToString();

            HashSet<string> values = [];

            ExpectToken("keyword", tokenizer, ShaderToken.OpenSquareBrace);

            while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseSquareBrace)
                values.Add(tokenizer.Token.ToString());

            keywords.Add(name, values);
        }

        return keywords;
    }


    private static bool ParseProgramInfo(string program, FileIncluder includer, int programLine, out EntryPoint[] entrypoints, out (int, int) shaderModel)
    {
        List<EntryPoint> entrypointsList = [];
        entrypoints = [];
        shaderModel = (6, 0);

        void AddEntrypoint(ShaderStages stage, string name, string idType)
        {
            if (!entrypointsList.Exists(x => x.Stage == stage))
                entrypointsList.Add(new EntryPoint(stage, name));
            else
                throw new ParseException("entrypoint", $"duplicate entrypoints defined for {idType}.");
        }

        using StringReader sr = new(program);

        string? line;
        bool hasModel = false;
        int lineNumber = 0;
        while ((line = sr.ReadLine()) != null)
        {
            lineNumber++;
            string[] linesSplit = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (linesSplit.Length < 3)
                continue;

            if (linesSplit[0] != "#pragma")
                continue;

            try
            {
                switch (linesSplit[1])
                {
                    case "vertex":
                        AddEntrypoint(ShaderStages.Vertex, linesSplit[2], "vertex");
                        break;

                    case "geometry":
                        AddEntrypoint(ShaderStages.Geometry, linesSplit[2], "geometry");
                        break;

                    case "tesscontrol":
                        AddEntrypoint(ShaderStages.TessellationControl, linesSplit[2], "tesscontrol");
                        break;

                    case "tessevaluation":
                        AddEntrypoint(ShaderStages.TessellationEvaluation, linesSplit[2], "tessevaluation");
                        break;

                    case "fragment":
                        AddEntrypoint(ShaderStages.Fragment, linesSplit[2], "fragment");
                        break;

                    case "target":
                        if (hasModel)
                            throw new ParseException("target", "duplicate shader model targets defined.");

                        try
                        {
                            int major = (int)char.GetNumericValue(linesSplit[2][0]);

                            if (linesSplit[2][1] != '.')
                                throw new Exception();

                            int minor = (int)char.GetNumericValue(linesSplit[2][2]);

                            if (major < 0 || minor < 0)
                                throw new Exception();

                            shaderModel = (major, minor);
                            hasModel = true;
                        }
                        catch
                        {
                            throw new ParseException("shader model", $"invalid shader model: {linesSplit[2]}");
                        }
                        break;
                }
            }
            catch (ParseException ex)
            {
                LogCompilationError(ex.Message, includer, programLine + lineNumber, line.IndexOf("#pragma") + 7);
                return false;
            }
        }

        entrypoints = [.. entrypointsList];
        return true;
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


    private static byte ByteParse(ReadOnlySpan<char> text, string fieldName)
    {
        try
        {
            return byte.Parse(text);
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
        return texture switch
        {
            "white" => Texture2D.White,
            "gray" or "grey" => Texture2D.Gray,
            "grid" => Texture2D.Grid,
            "black" => Texture2D.Black,
            "red" => Texture2D.Red,
            "normal" => Texture2D.Normal,
            "surface" => Texture2D.Surface,
            _ => throw new ParseException("texture 2d", $"unknown texture default: {texture}")
        };
    }


    private static AssetRef<Texture3D> Texture3DParse(string texture)
    {
        return texture switch
        {
            "white" => Texture3D.White,
            "gray" or "grey" => Texture3D.White,
            _ => throw new ParseException("texture 3d", $"unknown texture default: {texture}")
        };
    }


    // Convert string ("false", "0", "off", "no") or ("true", "1", "on", "yes") to boolean
    private static bool ConvertToBoolean(string input)
    {
        input = input.Trim();

        return
            input.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            !input.Equals("0", StringComparison.OrdinalIgnoreCase) || // For numerical booleans, anything other than 0 should be true.
            input.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

public class ParsedPass
{
    public int Line;
    public string Name = "";
    public ShaderPassDescription Description;

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
