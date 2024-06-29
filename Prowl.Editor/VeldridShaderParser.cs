using System.Text;
using static Prowl.Runtime.StringTagConverter;

namespace Prowl.Editor.VeldridShaderParser
{
    public class VeldridShaderParser
    {
        private readonly GenericTokenizer<TokenType> _tokenizer;

        public VeldridShaderParser(string input)
        {
            var symbolHandlers = new Dictionary<char, Func<TokenType>>
            {
                {'{', () => HandleSingleCharToken(TokenType.OpenBrace)},
                {'}', () => HandleSingleCharToken(TokenType.CloseBrace)},
                {'(', () => HandleSingleCharToken(TokenType.OpenParen)},
                {')', () => HandleSingleCharToken(TokenType.CloseParen)},
                {'=', () => HandleSingleCharToken(TokenType.Equals)},
                {',', () => HandleSingleCharToken(TokenType.Comma)},
            };

            _tokenizer = new GenericTokenizer<TokenType>(
                input.AsMemory(),
                symbolHandlers,
                c => "{}()=,".Contains(c),
                TokenType.Identifier,
                TokenType.None
            );
        }

        private TokenType HandleSingleCharToken(TokenType tokenType)
        {
            _tokenizer.TokenMemory = _tokenizer.Input.Slice(_tokenizer.TokenPosition, 1);
            _tokenizer.InputPosition++;
            return tokenType;
        }

        public Shader Parse()
        {
            var shader = new Shader();

            while (_tokenizer.MoveNext())
            {
                switch (_tokenizer.Token.ToString())
                {
                    case "Shader":
                        _tokenizer.MoveNext(); // Move to string
                        shader.Name = _tokenizer.ParseQuotedStringValue();
                        break;
                    case "Properties":
                        shader.Properties = ParseProperties();
                        break;
                    case "Global":
                        shader.Global = ParseGlobal();
                        break;
                    case "Pass":
                        shader.Passes.Add(ParsePass());
                        break;
                    case "Fallback":
                        _tokenizer.MoveNext(); // Move to string
                        shader.Fallback = _tokenizer.ParseQuotedStringValue();
                        break;
                }
            }

            return shader;
        }

        private List<ShaderProperty> ParseProperties()
        {
            var properties = new List<ShaderProperty>();
            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var property = new ShaderProperty();

                property.Name = _tokenizer.Token.ToString();
                ExpectToken(TokenType.OpenParen);
                ExpectToken(TokenType.Identifier);
                property.DisplayName = _tokenizer.ParseQuotedStringValue();
                ExpectToken(TokenType.Comma);
                ExpectToken(TokenType.Identifier);
                property.Type = _tokenizer.Token.ToString();
                ExpectToken(TokenType.CloseParen);

                properties.Add(property);
            }

            return properties;
        }

        private GlobalState ParseGlobal()
        {
            var global = new GlobalState();
            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                switch (_tokenizer.Token.ToString())
                {
                    case "Tags":
                        global.Tags = ParseTags();
                        break;
                    case "Blend":
                        global.Blend = ParseBlend();
                        break;
                    case "Stencil":
                        global.Stencil = ParseStencil();
                        break;
                    case "DepthWrite":
                        ExpectToken(TokenType.Identifier);
                        global.DepthWrite = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "DepthTest":
                        ExpectToken(TokenType.Identifier);
                        global.DepthTest = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "Cull":
                        ExpectToken(TokenType.Identifier);
                        global.Cull = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "GlobalInclude":
                        global.GlobalInclude = ParseGlobalInclude();
                        break;
                }
            }

            return global;
        }

        private Dictionary<string, string> ParseTags()
        {
            var tags = new Dictionary<string, string>();
            ExpectToken(TokenType.OpenBrace);

            //while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            while (true)
            {
                ExpectToken(TokenType.Identifier);
                var key = _tokenizer.ParseQuotedStringValue();
                ExpectToken(TokenType.Equals);
                ExpectToken(TokenType.Identifier);
                var value = _tokenizer.ParseQuotedStringValue();
                tags[key] = value;

                // Next token should either be a comma or a closing brace
                // if its a comma theres another tag so continue, if not break
                _tokenizer.MoveNext();
                if (_tokenizer.TokenType == TokenType.Comma)
                    continue;
                if (_tokenizer.TokenType == TokenType.CloseBrace)
                    break;

                throw new InvalidOperationException($"Expected comma or closing brace, but got {_tokenizer.TokenType}");
            }

            return tags;
        }

        private BlendState ParseBlend()
        {
            var blend = new BlendState();
            if (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.OpenBrace)
            {
                blend.Preset = _tokenizer.Token.ToString();
                return blend;
            }
            
            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var key = _tokenizer.Token.ToString();
                switch (key)
                {
                    case "Target":
                        ExpectToken(TokenType.Identifier);
                        blend.Target = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "Src":
                        ExpectToken(TokenType.Identifier);
                        blend.SourceTarget = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        blend.SourceMode = _tokenizer.Token.ToString();
                        break;
                    case "Dest":
                        ExpectToken(TokenType.Identifier);
                        blend.DestTarget = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        blend.DestMode = _tokenizer.Token.ToString();
                        break;
                    case "Mode":
                        ExpectToken(TokenType.Identifier);
                        blend.ModeTarget = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        blend.Mode = _tokenizer.Token.ToString();
                        break;
                    case "Mask":
                        ExpectToken(TokenType.Identifier);
                        blend.Mask = _tokenizer.Token.ToString();
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown blend key: {key}");
                }
            }

            return blend;
        }

        private StencilState ParseStencil()
        {
            var stencil = new StencilState();
            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var key = _tokenizer.Token.ToString();
                switch (key)
                {
                    case "Ref":
                        ExpectToken(TokenType.Identifier);
                        stencil.Ref = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "ReadMask":
                        ExpectToken(TokenType.Identifier);
                        stencil.ReadMask = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "WriteMask":
                        ExpectToken(TokenType.Identifier);
                        stencil.WriteMask = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "Comparison":
                        ExpectToken(TokenType.Identifier);
                        stencil.Comparison = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        stencil.ComparisonMode = _tokenizer.Token.ToString();
                        break;
                    case "Pass":
                        ExpectToken(TokenType.Identifier);
                        stencil.Pass = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        stencil.PassMode = _tokenizer.Token.ToString();
                        break;
                    case "Fail":
                        ExpectToken(TokenType.Identifier);
                        stencil.Fail = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        stencil.FailMode = _tokenizer.Token.ToString();
                        break;
                    case "ZFail":
                        ExpectToken(TokenType.Identifier);
                        stencil.ZFail = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        stencil.ZFailMode = _tokenizer.Token.ToString();
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown stencil key: {key}");
                }
            }

            return stencil;
        }

        private string ParseGlobalInclude()
        {
            ExpectToken(TokenType.OpenBrace);
            var content = "";
            int openBraces = 1;
            while (_tokenizer.MoveNext() && openBraces > 0)
            {
                if (_tokenizer.TokenType == TokenType.OpenBrace)
                    openBraces++;
                if (_tokenizer.TokenType == TokenType.CloseBrace)
                    openBraces--;

                if (openBraces > 0)
                    content += _tokenizer.Token.ToString() + " ";
            }
            return content;
        }

        private Pass ParsePass()
        {
            var pass = new Pass();
            ExpectToken(TokenType.Identifier);
            pass.Name = _tokenizer.ParseQuotedStringValue();
            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                switch (_tokenizer.Token.ToString())
                {
                    case "Tags":
                        pass.Tags = ParseTags();
                        break;
                    case "Blend":
                        pass.Blend = ParseBlend();
                        break;
                    case "Stencil":
                        pass.Stencil = ParseStencil();
                        break;
                    case "DepthWrite":
                        ExpectToken(TokenType.Identifier);
                        pass.DepthWrite = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "DepthTest":
                        ExpectToken(TokenType.Identifier);
                        pass.DepthTest = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "Cull":
                        ExpectToken(TokenType.Identifier);
                        pass.Cull = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "Program":
                        pass.Programs.Add(ParseProgram());
                        break;
                }
            }

            return pass;
        }

        private ShaderProgram ParseProgram()
        {
            var program = new ShaderProgram();
            ExpectToken(TokenType.Identifier);
            program.Type = _tokenizer.Token.ToString();
            ExpectToken(TokenType.OpenBrace);

            var content = "";
            int openBraces = 1;
            while (_tokenizer.MoveNext() && openBraces > 0)
            {
                if (_tokenizer.TokenType == TokenType.OpenBrace)
                    openBraces++;
                if (_tokenizer.TokenType == TokenType.CloseBrace)
                    openBraces--;

                if (openBraces > 0)
                    content += _tokenizer.Token.ToString() + " ";
            }
            program.Content = content;

            return program;
        }

        private void ExpectToken(TokenType expectedType)
        {
            _tokenizer.MoveNext();
            if (_tokenizer.TokenType != expectedType)
            {
                throw new InvalidOperationException($"Expected {expectedType}, but got {_tokenizer.TokenType}");
            }
        }

        // Convert string ("false", "0", "off", "no") or ("true", "1", "on", "yes") to boolean
        private static bool ConvertToBoolean(string input)
        {
            input = input.Trim();
            input = input.ToLower();
            return input.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   input.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   input.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                   input.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum TokenType
    {
        None,
        Identifier,
        OpenBrace,
        CloseBrace,
        OpenParen,
        CloseParen,
        Equals,
        Comma,
        Quote,
    }

    public class Shader
    {
        public string Name { get; set; }
        public List<ShaderProperty> Properties { get; set; } = new List<ShaderProperty>();
        public GlobalState Global { get; set; }
        public List<Pass> Passes { get; set; } = new List<Pass>();
        public string Fallback { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Shader \"{Name}\"");
            sb.AppendLine("{");

            {
                sb.AppendLine("Properties");
                sb.AppendLine("{");
                foreach (var property in Properties)
                {
                    sb.AppendLine($"\"{property.Name}\" = \"{property.DisplayName}\" {property.Type}");
                }
                sb.AppendLine("}");
            }

            {
                sb.AppendLine("Global");
                sb.AppendLine("{");

                sb.Append("Tags { ");
                foreach (var tag in Global.Tags)
                {
                    sb.Append($"\"{tag.Key}\" = \"{tag.Value}\", ");
                }
                sb.AppendLine("}");

                if (Global.Blend != null)
                {
                    sb.AppendLine($"Blend {Global.Blend.Preset}");
                    if (string.IsNullOrEmpty(Global.Blend.Preset))
                    {
                        sb.AppendLine("{");
                        sb.AppendLine($"Src {Global.Blend.SourceTarget} {Global.Blend.SourceMode}");
                        sb.AppendLine($"Dst {Global.Blend.DestTarget} {Global.Blend.DestMode}");
                        sb.AppendLine($"Mode {Global.Blend.ModeTarget} {Global.Blend.Mode}");
                        sb.AppendLine($"Mask {Global.Blend.Mask}");
                        sb.AppendLine("}");
                    }
                }

                if (Global.Stencil != null)
                {
                    sb.AppendLine("Stencil");
                    sb.AppendLine("{");
                    sb.AppendLine($"Ref {Global.Stencil.Ref}");
                    sb.AppendLine($"ReadMask {Global.Stencil.ReadMask}");
                    sb.AppendLine($"WriteMask {Global.Stencil.WriteMask}");
                    sb.AppendLine($"Comparison {Global.Stencil.Comparison} {Global.Stencil.ComparisonMode}");
                    sb.AppendLine($"Pass {Global.Stencil.Pass} {Global.Stencil.PassMode}");
                    sb.AppendLine($"Fail {Global.Stencil.Fail} {Global.Stencil.FailMode}");
                    sb.AppendLine($"ZFail {Global.Stencil.ZFail} {Global.Stencil.ZFailMode}");
                    sb.AppendLine("}");
                }

                sb.AppendLine($"DepthWrite {Global.DepthWrite}");
                sb.AppendLine($"DepthTest {Global.DepthTest}");
                sb.AppendLine($"Cull {Global.Cull}");

                sb.AppendLine($"GlobalInclude");
                sb.AppendLine("{");
                sb.AppendLine(Global.GlobalInclude);
                sb.AppendLine("}");

                sb.AppendLine("}");
            }

            foreach (var pass in Passes)
            {
                sb.AppendLine($"Pass \"{pass.Name}\"");
                sb.AppendLine("{");

                sb.Append("Tags { ");
                foreach (var tag in pass.Tags)
                {
                    sb.Append($"\"{tag.Key}\" = \"{tag.Value}\", ");
                }
                sb.AppendLine("}");

                if (pass.Blend != null)
                {
                    sb.AppendLine($"Blend {pass.Blend.Preset}");
                    if (string.IsNullOrEmpty(pass.Blend.Preset))
                    {
                        sb.AppendLine("{");
                        sb.AppendLine($"Src {pass.Blend.SourceTarget} {pass.Blend.SourceMode}");
                        sb.AppendLine($"Dst {pass.Blend.DestTarget} {pass.Blend.DestMode}");
                        sb.AppendLine($"Mode {pass.Blend.ModeTarget} {pass.Blend.Mode}");
                        sb.AppendLine($"Mask {pass.Blend.Mask}");
                        sb.AppendLine("}");
                    }
                }

                if(pass.Stencil != null)
                {
                    sb.AppendLine("Stencil");
                    sb.AppendLine("{");
                    sb.AppendLine($"Ref {pass.Stencil.Ref}");
                    sb.AppendLine($"ReadMask {pass.Stencil.ReadMask}");
                    sb.AppendLine($"WriteMask {pass.Stencil.WriteMask}");
                    sb.AppendLine($"Comparison {pass.Stencil.Comparison} {pass.Stencil.ComparisonMode}");
                    sb.AppendLine($"Pass {pass.Stencil.Pass} {pass.Stencil.PassMode}");
                    sb.AppendLine($"Fail {pass.Stencil.Fail} {pass.Stencil.FailMode}");
                    sb.AppendLine($"ZFail {pass.Stencil.ZFail} {pass.Stencil.ZFailMode}");
                    sb.AppendLine("}");
                }

                sb.AppendLine($"DepthWrite {pass.DepthWrite}");
                sb.AppendLine($"DepthTest {pass.DepthTest}");
                sb.AppendLine($"Cull {pass.Cull}");
                foreach (var program in pass.Programs)
                {
                    sb.AppendLine($"Program {program.Type}");
                    sb.AppendLine("{");
                    sb.AppendLine(program.Content);
                    sb.AppendLine("}");
                }
                sb.AppendLine("}");
            }
            sb.AppendLine($"Fallback \"{Fallback}\"");

            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    public class ShaderProperty
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
    }

    public class GlobalState
    {
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        public BlendState? Blend { get; set; }
        public StencilState? Stencil { get; set; }
        public bool DepthWrite { get; set; }
        public bool DepthTest { get; set; }
        public bool Cull { get; set; }
        public string GlobalInclude { get; set; }
    }

    public class BlendState
    {
        public string Preset { get; set; }

        public byte Target { get; set; }

        public string SourceTarget { get; set; }
        public string SourceMode { get; set; }

        public string DestTarget { get; set; }
        public string DestMode { get; set; }

        public string ModeTarget { get; set; }
        public string Mode { get; set; }

        public string Mask { get; set; }
    }

    public class StencilState
    {
        public byte Ref { get; set; }
        public byte ReadMask { get; set; }
        public byte WriteMask { get; set; }

        public string Comparison { get; set; }
        public string ComparisonMode { get; set; }

        public string Pass { get; set; }
        public string PassMode { get; set; }

        public string Fail { get; set; }
        public string FailMode { get; set; }

        public string ZFail { get; set; }
        public string ZFailMode { get; set; }
    }

    public class Pass
    {
        public string Name { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        public BlendState? Blend { get; set; }
        public StencilState? Stencil { get; set; }
        public bool DepthWrite { get; set; }
        public bool DepthTest { get; set; }
        public bool Cull { get; set; }
        public List<ShaderProgram> Programs { get; set; } = new List<ShaderProgram>();
    }

    public class ShaderProgram
    {
        public string Type { get; set; }
        public string Content { get; set; }
    }
}
