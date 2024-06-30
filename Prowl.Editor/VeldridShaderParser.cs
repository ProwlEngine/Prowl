using System.Net.WebSockets;
using System.Text;
using Veldrid;
using static Prowl.Runtime.StringTagConverter;

namespace Prowl.Editor.VeldridShaderParser
{
    public class VeldridShaderParser
    {
        private readonly GenericTokenizer<TokenType> _tokenizer;

        private ParsedShader shader;

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

        public ParsedShader Parse()
        {
            shader = new ParsedShader();

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
                        if(shader.Passes.Count > 0)
                            throw new InvalidOperationException("Global state must be defined before passes");
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

        private List<ParsedShaderProperty> ParseProperties()
        {
            var properties = new List<ParsedShaderProperty>();
            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var property = new ParsedShaderProperty();

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

        private ParsedGlobalState ParseGlobal()
        {
            var global = new ParsedGlobalState();
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
                    case "DepthStencil":
                        global.Stencil = ParseStencil();
                        break;
                    case "Cull":
                        ExpectToken(TokenType.Identifier);
                        global.Cull = Enum.Parse<FaceCullMode>(_tokenizer.Token.ToString(), true);
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

        private BlendAttachmentDescription ParseBlend()
        {
            var blend = shader.Global?.Blend ?? BlendAttachmentDescription.AdditiveBlend;
            blend.BlendEnabled = true;

            if (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.OpenBrace)
            {
                string preset = _tokenizer.Token.ToString();
                if (preset.Equals("Additive", StringComparison.OrdinalIgnoreCase))
                    blend = BlendAttachmentDescription.AdditiveBlend;
                else if (preset.Equals("Alpha", StringComparison.OrdinalIgnoreCase))
                    blend = BlendAttachmentDescription.AlphaBlend;
                else if (preset.Equals("Override", StringComparison.OrdinalIgnoreCase))
                    blend = BlendAttachmentDescription.OverrideBlend;
                else
                    throw new InvalidOperationException("Unknown blend preset: " + preset);


                return blend;
            }
            
            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var key = _tokenizer.Token.ToString();
                string target;
                switch (key)
                {
                    case "Src":
                        ExpectToken(TokenType.Identifier);
                        target = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                            blend.SourceColorFactor = Enum.Parse<BlendFactor>(_tokenizer.Token.ToString(), true);
                        else
                            blend.SourceAlphaFactor = Enum.Parse<BlendFactor>(_tokenizer.Token.ToString(), true);

                        break;

                    case "Dest":
                        ExpectToken(TokenType.Identifier);
                        target = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                            blend.DestinationColorFactor = Enum.Parse<BlendFactor>(_tokenizer.Token.ToString(), true);
                        else
                            blend.DestinationAlphaFactor = Enum.Parse<BlendFactor>(_tokenizer.Token.ToString(), true);

                        break;

                    case "Mode":
                        ExpectToken(TokenType.Identifier);
                        target = _tokenizer.Token.ToString();
                        ExpectToken(TokenType.Identifier);
                        if (target.Equals("Color", StringComparison.OrdinalIgnoreCase))
                            blend.ColorFunction = Enum.Parse<BlendFunction>(_tokenizer.Token.ToString(), true);
                        else
                            blend.AlphaFunction = Enum.Parse<BlendFunction>(_tokenizer.Token.ToString(), true);

                        break;

                    case "Mask":
                        ExpectToken(TokenType.Identifier);
                        var mask = _tokenizer.Token.ToString();
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
                                throw new InvalidOperationException("Invalid color write mask: " + mask);
                        }
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown blend key: {key}");
                }
            }

            return blend;
        }

        private DepthStencilStateDescription ParseStencil()
        {
            var stencil = shader.Global.Stencil ?? DepthStencilStateDescription.DepthOnlyLessEqual;

            if (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.OpenBrace)
            {
                string preset = _tokenizer.Token.ToString();
                if (preset.Equals("DepthGreaterEqual", StringComparison.OrdinalIgnoreCase))
                    stencil = DepthStencilStateDescription.DepthOnlyGreaterEqual;
                else if (preset.Equals("DepthLessEqual", StringComparison.OrdinalIgnoreCase))
                    stencil = DepthStencilStateDescription.DepthOnlyLessEqual;
                else if (preset.Equals("DepthGreaterEqualRead", StringComparison.OrdinalIgnoreCase))
                    stencil = DepthStencilStateDescription.DepthOnlyGreaterEqualRead;
                else if (preset.Equals("DepthLessEqualRead", StringComparison.OrdinalIgnoreCase))
                    stencil = DepthStencilStateDescription.DepthOnlyLessEqualRead;
                else
                    throw new InvalidOperationException("Unknown blend preset: " + preset);

                return stencil;
            }


            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var key = _tokenizer.Token.ToString();
                switch (key)
                {
                    case "DepthWrite":
                        ExpectToken(TokenType.Identifier);
                        stencil.DepthWriteEnabled = ConvertToBoolean(_tokenizer.Token.ToString());
                        break;
                    case "DepthTest":
                        ExpectToken(TokenType.Identifier);
                        string kind = _tokenizer.Token.ToString();
                        stencil.DepthTestEnabled = Enum.TryParse<ComparisonKind>(kind, true, out var res);
                        stencil.DepthComparison = res;
                        break;
                    case "Ref":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilReference = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "ReadMask":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilReadMask = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "WriteMask":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilWriteMask = byte.Parse(_tokenizer.Token.ToString());
                        break;
                    case "Comparison":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilFront.Comparison = Enum.Parse<ComparisonKind>(_tokenizer.Token.ToString(), true);
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilBack.Comparison = Enum.Parse<ComparisonKind>(_tokenizer.Token.ToString(), true);
                        break;
                    case "Pass":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilFront.Pass = Enum.Parse<StencilOperation>(_tokenizer.Token.ToString(), true);
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilBack.Pass = Enum.Parse<StencilOperation>(_tokenizer.Token.ToString(), true);
                        break;
                    case "Fail":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilFront.Fail = Enum.Parse<StencilOperation>(_tokenizer.Token.ToString(), true);
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilBack.Fail = Enum.Parse<StencilOperation>(_tokenizer.Token.ToString(), true);
                        break;
                    case "ZFail":
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilFront.DepthFail = Enum.Parse<StencilOperation>(_tokenizer.Token.ToString(), true);
                        ExpectToken(TokenType.Identifier);
                        stencil.StencilBack.DepthFail = Enum.Parse<StencilOperation>(_tokenizer.Token.ToString(), true);
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

        private ParsedPass ParsePass()
        {
            var pass = new ParsedPass();
            ExpectToken(TokenType.Identifier);
            pass.Name = _tokenizer.ParseQuotedStringValue();
            ExpectToken(TokenType.OpenBrace);

            pass.Cull = shader.Global.Cull;

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
                    case "DepthStencil":
                        pass.Stencil = ParseStencil();
                        break;
                    case "Cull":
                        ExpectToken(TokenType.Identifier);
                        pass.Cull = Enum.Parse<FaceCullMode>(_tokenizer.Token.ToString(), true);
                        break;
                    case "Program":
                        pass.Programs.Add(ParseProgram());
                        break;
                }
            }

            return pass;
        }

        private ParsedShaderProgram ParseProgram()
        {
            var program = new ParsedShaderProgram();
            ExpectToken(TokenType.Identifier);
            program.Type = Enum.Parse<ShaderStages>(_tokenizer.Token.ToString(), true);
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

    public class ParsedShader
    {
        public string Name { get; set; }
        public List<ParsedShaderProperty> Properties { get; set; } = new List<ParsedShaderProperty>();
        public ParsedGlobalState Global { get; set; }
        public List<ParsedPass> Passes { get; set; } = new List<ParsedPass>();
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

                sb.AppendLine($"Blend");
                sb.AppendLine("{");
                sb.AppendLine($"Src Color {Global.Blend.Value.SourceColorFactor}");
                sb.AppendLine($"Src Alpha {Global.Blend.Value.SourceColorFactor}");
                sb.AppendLine($"Dst Color {Global.Blend.Value.DestinationColorFactor}");
                sb.AppendLine($"Dst Alpha {Global.Blend.Value.DestinationAlphaFactor}");
                sb.AppendLine($"Mode Color {Global.Blend.Value.ColorFunction}");
                sb.AppendLine($"Mode Alpha {Global.Blend.Value.AlphaFunction}");
                sb.AppendLine($"Mask {Global.Blend.Value.ColorWriteMask}");

                sb.AppendLine("}");

                if (Global.Stencil != null)
                {
                    sb.AppendLine("Stencil");
                    sb.AppendLine("{");
                    sb.AppendLine($"DepthWrite {Global.Stencil.Value.DepthWriteEnabled}");
                    sb.AppendLine($"DepthTest {Global.Stencil.Value.DepthTestEnabled}");
                    sb.AppendLine($"Ref {Global.Stencil.Value.StencilReference}");
                    sb.AppendLine($"ReadMask {Global.Stencil.Value.StencilReadMask}");
                    sb.AppendLine($"WriteMask {Global.Stencil.Value.StencilWriteMask}");
                    sb.AppendLine($"Comparison {Global.Stencil.Value.StencilFront.Comparison} {Global.Stencil.Value.StencilBack.Comparison}");
                    sb.AppendLine($"Pass {Global.Stencil.Value.StencilFront.Pass} {Global.Stencil.Value.StencilBack.Pass}");
                    sb.AppendLine($"Fail {Global.Stencil.Value.StencilFront.Fail} {Global.Stencil.Value.StencilBack.Fail}");
                    sb.AppendLine($"ZFail {Global.Stencil.Value.StencilFront.DepthFail} {Global.Stencil.Value.StencilBack.DepthFail}");
                    sb.AppendLine("}");
                }

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

                sb.AppendLine($"Blend");
                sb.AppendLine("{");
                sb.AppendLine($"Src Color {pass.Blend.Value.SourceColorFactor}");
                sb.AppendLine($"Src Alpha {pass.Blend.Value.SourceColorFactor}");
                sb.AppendLine($"Dst Color {pass.Blend.Value.DestinationColorFactor}");
                sb.AppendLine($"Dst Alpha {pass.Blend.Value.DestinationAlphaFactor}");
                sb.AppendLine($"Mode Color {pass.Blend.Value.ColorFunction}");
                sb.AppendLine($"Mode Alpha {pass.Blend.Value.AlphaFunction}");
                sb.AppendLine($"Mask {pass.Blend.Value.ColorWriteMask}");
                sb.AppendLine("}");

                if(pass.Stencil != null)
                {
                    sb.AppendLine("Stencil");
                    sb.AppendLine("{");
                    sb.AppendLine("{");
                    sb.AppendLine($"DepthWrite {pass.Stencil.Value.DepthWriteEnabled}");
                    sb.AppendLine($"DepthTest {pass.Stencil.Value.DepthTestEnabled}");
                    sb.AppendLine($"Ref {pass.Stencil.Value.StencilReference}");
                    sb.AppendLine($"ReadMask {pass.Stencil.Value.StencilReadMask}");
                    sb.AppendLine($"WriteMask {pass.Stencil.Value.StencilWriteMask}");
                    sb.AppendLine($"Comparison {pass.Stencil.Value.StencilFront.Comparison} {pass.Stencil.Value.StencilBack.Comparison}");
                    sb.AppendLine($"Pass {pass.Stencil.Value.StencilFront.Pass} {pass.Stencil.Value.StencilBack.Pass}");
                    sb.AppendLine($"Fail {pass.Stencil.Value.StencilFront.Fail} {pass.Stencil.Value.StencilBack.Fail}");
                    sb.AppendLine($"ZFail {pass.Stencil.Value.StencilFront.DepthFail} {pass.Stencil.Value.StencilBack.DepthFail}");
                    sb.AppendLine("}");
                }

                sb.AppendLine($"DepthTest {pass.DepthComparison}");
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

    public class ParsedShaderProperty
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
    }

    public class ParsedGlobalState
    {
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        public BlendAttachmentDescription? Blend { get; set; } = null;
        public DepthStencilStateDescription? Stencil { get; set; } = null;
        public FaceCullMode Cull { get; set; } = FaceCullMode.Back;
        public string GlobalInclude { get; set; } = "";
    }

    public class ParsedPass
    {
        public string Name { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
        public BlendAttachmentDescription? Blend { get; set; } = null;
        public DepthStencilStateDescription? Stencil { get; set; } = null;
        public ComparisonKind DepthComparison { get; set; }
        public FaceCullMode Cull { get; set; }
        public List<ParsedShaderProgram> Programs { get; set; } = new List<ParsedShaderProgram>();
    }

    public class ParsedShaderProgram
    {
        public ShaderStages Type { get; set; }
        public string Content { get; set; }
    }
}
