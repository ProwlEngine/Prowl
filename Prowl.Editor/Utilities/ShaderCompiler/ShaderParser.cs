// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

using Veldrid;

using Debug = Prowl.Runtime.Debug;

namespace Prowl.Editor.Utilities
{
    public static partial class ShaderParser
    {
        [GeneratedRegex(@"//.*")]
        private static partial Regex CommentRegex();

        [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
        private static partial Regex MultilineCommentRegex();

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
            Quote,
        }


        private static Tokenizer<ShaderToken> CreateTokenizer(string input)
        {
            string noComments = CommentRegex().Replace(input, "");
            string noMultilineComments = MultilineCommentRegex().Replace(noComments, "");

            Dictionary<char, Func<Tokenizer, ShaderToken>> symbolHandlers = new()
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

            return new(
                noMultilineComments.AsMemory(),
                symbolHandlers,
                "{}()=,".Contains,
                ShaderToken.Identifier,
                ShaderToken.None
            );
        }


        public static bool ParseShader(string input, FileIncluder includer, out Runtime.Shader? shader)
        {
            shader = null;

            Tokenizer<ShaderToken> tokenizer = CreateTokenizer(input);

            string name = "";

            List<ShaderProperty> properties = [];
            ParsedPass? globalDefaults = null;
            List<ParsedPass> parsedPasses = [];

            string? fallback = null;

            try
            {
                tokenizer.MoveNext();
                if (tokenizer.Token.ToString() != "Shader")
                    throw new ParseException($"Expected top-level 'Shader' declaration, found '{tokenizer.Token}'");

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
                            throw new ParseException($"Unknown shader token: {tokenizer.Token}");
                    }
                }
            }
            catch (ParseException ex)
            {
                LogCompilationError(ex.Message, includer, tokenizer.CurrentLine, tokenizer.CurrentColumn);
                return false;
            }

            ShaderPass[] passes = new ShaderPass[parsedPasses.Count];

            int globalLen = globalDefaults != null && globalDefaults.Program != null ? globalDefaults.Program.Length : 0;
            int globalPos = globalDefaults != null && globalDefaults.Program != null ? globalDefaults.ProgramStartLine : 0;

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

                if (!ParseProgramInfo(sourceCode, out EntryPoint[]? entrypoints, out (int, int)? shaderModel, out CompilationMessage? message))
                {
                    LogCompilationError(message.Value.message, includer, message.Value.line, message.Value.column);
                    return false;
                }

                if (!Array.Exists(entrypoints, x => x.Stage == ShaderStages.Vertex))
                {
                    LogCompilationError($"Pass {i} does not contain a vertex stage", includer, parsedPass.Line, 0);
                    return false;
                }

                if (!Array.Exists(entrypoints, x => x.Stage == ShaderStages.Fragment))
                {
                    LogCompilationError($"Pass {i} does not contain a fragment stage.", includer, parsedPass.Line, 0);
                    return false;
                }

                ShaderCreationArgs args;
                args.entryPoints = entrypoints;
                args.combinations = passDesc.Keywords ?? new() { { "", [""] } };
                args.shaderModel = shaderModel ?? (6, 0);
                args.sourceCode = sourceCode;

                List<CompilationMessage> compilerMessages = [];

                ShaderVariant[] variants = ShaderCompiler.GenerateVariants(args, includer, compilerMessages);

                if (LogCompilationMessages(compilerMessages, includer, parsedPass.ProgramStartLine, globalLen, globalPos))
                    return false;

                passes[i] = new ShaderPass(parsedPass.Name, passDesc, variants);
            }

            shader = new Runtime.Shader(name, [.. properties], passes);

            return true;
        }


        private static void LogCompilationError(string message, FileIncluder includer, int line, int column)
        {
            DebugStackFrame frame = new(line, column, includer.SourceFilePath);
            DebugStackTrace trace = new(frame);

            Debug.Log("Error compiling shader: " + message, ConsoleColor.Red, LogSeverity.Error, trace);
        }


        private static bool LogCompilationMessages(List<CompilationMessage> messages, FileIncluder includer, int programStartLine, int globalOffset, int globalStartLine)
        {
            bool hasErrors = false;

            foreach (CompilationMessage message in messages)
            {
                if (message.severity == LogSeverity.Error || message.severity == LogSeverity.Exception)
                    hasErrors = true;

                bool isGlobal = message.line - globalOffset < 0;

                int line = isGlobal ? globalStartLine + message.line : programStartLine + (message.line - globalOffset);

                ConsoleColor col = message.severity switch
                {
                    LogSeverity.Normal => ConsoleColor.White,
                    LogSeverity.Warning => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red,
                };

                DebugStackFrame frame = new(line, message.column, includer.SourceFilePath);
                DebugStackTrace trace = new(frame);

                Debug.Log(message.message, col, message.severity, trace);
            }

            return hasErrors;
        }


        private static ShaderToken HandleSingleCharToken(Tokenizer tokenizer, ShaderToken tokenType)
        {
            tokenizer.TokenMemory = tokenizer.Input.Slice(tokenizer.TokenPosition, 1);
            tokenizer.InputPosition++;

            return tokenType;
        }


        private static List<ShaderProperty> ParseProperties(Tokenizer<ShaderToken> tokenizer)
        {
            List<ShaderProperty> properties = [];

            ExpectToken(tokenizer, ShaderToken.OpenCurlBrace);

            while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
            {
                ShaderProperty property;

                property.DefaultProperty = "";
                property.Name = tokenizer.Token.ToString();

                ExpectToken(tokenizer, ShaderToken.OpenParen);

                ExpectToken(tokenizer, ShaderToken.Identifier);
                property.DisplayName = tokenizer.ParseQuotedStringValue();

                ExpectToken(tokenizer, ShaderToken.Comma);
                ExpectToken(tokenizer, ShaderToken.Identifier);
                property.PropertyType = EnumParse<ShaderPropertyType>(tokenizer.Token.ToString(), "property type");

                ExpectToken(tokenizer, ShaderToken.CloseParen);

                properties.Add(property);
            }

            return properties;
        }


        private static ParsedPass ParseGlobal(Tokenizer<ShaderToken> tokenizer)
        {
            ParsedPass parsedGlobal = new();

            ExpectToken(tokenizer, ShaderToken.OpenCurlBrace);

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
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        parsedGlobal.Description.CullingMode = EnumParse<FaceCullMode>(tokenizer.Token.ToString(), "Cull");
                        break;

                    case "HLSLINCLUDE":
                        parsedGlobal.ProgramStartLine = tokenizer.CurrentLine;
                        EnsureUndef(parsedGlobal.Program, "'HLSLINCLUDE' in Global block");

                        SliceTo(tokenizer, "ENDHLSL");
                        parsedGlobal.Program = tokenizer.Token.ToString();
                        break;

                    default:
                        throw new ParseException($"Unknown global token: {tokenizer.Token}");
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
                ExpectToken(tokenizer, ShaderToken.OpenCurlBrace);
            }
            else if (tokenizer.TokenType != ShaderToken.OpenCurlBrace)
            {
                throw new ParseException($"Expected {ShaderToken.OpenCurlBrace} or {ShaderToken.Identifier}, but got {tokenizer.TokenType}");
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
                        ExpectToken(tokenizer, ShaderToken.Identifier);
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
                        break;

                    default:
                        throw new ParseException($"Unknown pass token: {tokenizer.Token}");
                }
            }

            if (pass.Program == null)
                throw new ParseException("Pass does not contain a program");

            return pass;
        }


        private static Dictionary<string, string> ParseTags(Tokenizer<ShaderToken> tokenizer)
        {
            var tags = new Dictionary<string, string>();
            ExpectToken(tokenizer, ShaderToken.OpenCurlBrace);

            //while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            while (true)
            {
                ExpectToken(tokenizer, ShaderToken.Identifier);
                var key = tokenizer.ParseQuotedStringValue();

                ExpectToken(tokenizer, ShaderToken.Equals);
                ExpectToken(tokenizer, ShaderToken.Identifier);

                var value = tokenizer.ParseQuotedStringValue();
                tags[key] = value;

                // Next token should either be a comma or a closing brace
                // if its a comma theres another tag so continue, if not break
                tokenizer.MoveNext();

                if (tokenizer.TokenType == ShaderToken.Comma)
                    continue;

                if (tokenizer.TokenType == ShaderToken.CloseCurlBrace)
                    break;

                throw new ParseException($"Expected comma or closing brace, but got {tokenizer.TokenType}");
            }

            return tags;
        }


        private static BlendAttachmentDescription ParseBlend(Tokenizer<ShaderToken> tokenizer)
        {
            var blend = BlendAttachmentDescription.AdditiveBlend;
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
                    throw new ParseException("Unknown blend preset: " + preset);

                return blend;
            }

            while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
            {
                var key = tokenizer.Token.ToString();
                string target;
                switch (key)
                {
                    case "Src":
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        target = tokenizer.Token.ToString();
                        ExpectToken(tokenizer, ShaderToken.Identifier);

                        if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                            blend.SourceColorFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Src");
                        else
                            blend.SourceAlphaFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Src");
                        break;

                    case "Dest":
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        target = tokenizer.Token.ToString();
                        ExpectToken(tokenizer, ShaderToken.Identifier);

                        if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                            blend.DestinationColorFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Dest");
                        else
                            blend.DestinationAlphaFactor = EnumParse<BlendFactor>(tokenizer.Token.ToString(), "Dest");
                        break;

                    case "Mode":
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        target = tokenizer.Token.ToString();
                        ExpectToken(tokenizer, ShaderToken.Identifier);

                        if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                            blend.ColorFunction = EnumParse<BlendFunction>(tokenizer.Token.ToString(), "Mode");
                        else
                            blend.AlphaFunction = EnumParse<BlendFunction>(tokenizer.Token.ToString(), "Mode");
                        break;

                    case "Mask":
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        var mask = tokenizer.Token.ToString();
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
                                throw new ParseException("Invalid color write mask: " + mask);
                        }
                        break;

                    default:
                        throw new ParseException($"Unknown blend key: {key}");
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
                    _ => throw new ParseException($"Unknown blend preset: {tokenizer.Token}"),
                };
            }

            var depthStencil = DepthStencilStateDescription.DepthOnlyLessEqual;

            // Open brace was detected, parse depth stencil settings
            while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
            {
                switch (tokenizer.Token)
                {
                    case "DepthWrite":
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.DepthWriteEnabled = ConvertToBoolean(tokenizer.Token.ToString());
                        break;

                    case "DepthTest":
                        ExpectToken(tokenizer, ShaderToken.Identifier);

                        if (tokenizer.Token.Equals("Off", StringComparison.OrdinalIgnoreCase))
                            depthStencil.DepthTestEnabled = false;
                        else
                            depthStencil.DepthComparison = EnumParse<ComparisonKind>(tokenizer.Token, "DepthTest", "Off");

                        break;

                    case "Ref":
                        depthStencil.StencilTestEnabled = true;
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilReference = ByteParse(tokenizer.Token, "Ref");
                        break;

                    case "ReadMask":
                        depthStencil.StencilTestEnabled = true;
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilReadMask = ByteParse(tokenizer.Token, "ReadMask");
                        break;

                    case "WriteMask":
                        depthStencil.StencilTestEnabled = true;
                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilWriteMask = ByteParse(tokenizer.Token, "WriteMask");
                        break;

                    case "Comparison":
                        depthStencil.StencilTestEnabled = true;

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilFront.Comparison = EnumParse<ComparisonKind>(tokenizer.Token, "Comparison");

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilBack.Comparison = EnumParse<ComparisonKind>(tokenizer.Token, "Comparison");
                        break;

                    case "Pass":
                        depthStencil.StencilTestEnabled = true;

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilFront.Pass = EnumParse<StencilOperation>(tokenizer.Token, "Pass");

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilBack.Pass = EnumParse<StencilOperation>(tokenizer.Token, "Pass");
                        break;

                    case "Fail":
                        depthStencil.StencilTestEnabled = true;

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilFront.Fail = EnumParse<StencilOperation>(tokenizer.Token, "Fail");

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilBack.Fail = EnumParse<StencilOperation>(tokenizer.Token, "Fail");
                        break;

                    case "ZFail":
                        depthStencil.StencilTestEnabled = true;

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilFront.DepthFail = EnumParse<StencilOperation>(tokenizer.Token, "ZFail");

                        ExpectToken(tokenizer, ShaderToken.Identifier);
                        depthStencil.StencilBack.DepthFail = EnumParse<StencilOperation>(tokenizer.Token, "ZFail");
                        break;

                    default:
                        throw new ParseException($"Unknown depth stencil key: {tokenizer.Token}");
                }
            }

            return depthStencil;
        }


        private static Dictionary<string, HashSet<string>> ParseKeywords(Tokenizer<ShaderToken> tokenizer)
        {
            Dictionary<string, HashSet<string>> keywords = new();
            ExpectToken(tokenizer, ShaderToken.OpenCurlBrace);

            while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseCurlBrace)
            {
                string name = tokenizer.Token.ToString();

                HashSet<string> values = [];

                ExpectToken(tokenizer, ShaderToken.OpenSquareBrace);

                while (tokenizer.MoveNext() && tokenizer.TokenType != ShaderToken.CloseSquareBrace)
                    values.Add(tokenizer.Token.ToString());

                keywords.Add(name, values);
            }

            return keywords;
        }


        private static bool ParseProgramInfo(string program, out EntryPoint[]? entrypoints, out (int, int)? shaderModel, out CompilationMessage? message)
        {
            List<EntryPoint> entrypointsList = [];
            entrypoints = null;
            shaderModel = null;
            message = null;

            void AddEntrypoint(ShaderStages stage, string name, string idType)
            {
                if (!entrypointsList.Exists(x => x.Stage == stage))
                    entrypointsList.Add(new EntryPoint(stage, name));
                else
                    throw new ParseException($"Duplicate entrypoints defined for {idType}.");
            }

            using StringReader sr = new(program);

            string? line;
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
                            if (shaderModel != null)
                                throw new ParseException($"Duplicate shader model targets defined.");

                            try
                            {
                                int major = (int)char.GetNumericValue(linesSplit[2][0]);

                                if (linesSplit[2][1] != '.')
                                    throw new Exception();

                                int minor = (int)char.GetNumericValue(linesSplit[2][2]);

                                if (major < 0 || minor < 0)
                                    throw new Exception();

                                shaderModel = (major, minor);
                            }
                            catch
                            {
                                throw new ParseException($"Invalid shader model: {linesSplit[2]}");
                            }
                            break;
                    }
                }
                catch (ParseException ex)
                {
                    message = new()
                    {
                        severity = LogSeverity.Error,
                        line = lineNumber,
                        column = line.IndexOf("#pragma") + 7,
                        message = ex.Message
                    };

                    return false;
                }
            }

            entrypoints = [.. entrypointsList];
            return true;
        }


        private static void ExpectToken(Tokenizer<ShaderToken> tokenizer, ShaderToken expectedType)
        {
            tokenizer.MoveNext();

            if (tokenizer.TokenType != expectedType)
                throw new ParseException($"Expected {expectedType}, but got {tokenizer.TokenType}");
        }


        public static bool SliceTo(Tokenizer<ShaderToken> tokenizer, string token)
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
                throw new ParseException($"Redefinition of {property}");
        }


        private static T EnumParse<T>(ReadOnlySpan<char> text, string fieldName, params string[] extraValues) where T : struct, Enum
        {
            if (Enum.TryParse(text, true, out T value))
                return value;

            List<string> values = [..Enum.GetNames<T>()];
            values.AddRange(extraValues);

            throw new ParseException($"Error parsing {fieldName}. Possible values: [{string.Join(", ", values)}]");
        }


        private static byte ByteParse(ReadOnlySpan<char> text, string fieldName)
        {
            if (byte.TryParse(text, out byte value))
                return value;

            throw new ParseException($"Error parsing {fieldName}.");
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
        public string? Program;
    }

    public struct EntryPoint(ShaderStages stages, string name)
    {
        public ShaderStages Stage = stages;
        public string Name = name;
    }


    internal class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
    }
}
