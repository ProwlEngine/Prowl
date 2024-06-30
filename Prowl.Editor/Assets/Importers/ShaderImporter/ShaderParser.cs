using Veldrid;
using static Prowl.Runtime.StringTagConverter;

namespace Prowl.Editor.ShaderParser
{
    public class ShaderParser
    {
        private readonly GenericTokenizer<TokenType> _tokenizer;

        private ParsedShader shader;

        public ShaderParser(string input)
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

        private List<Runtime.ShaderProperty> ParseProperties()
        {
            var properties = new List<Runtime.ShaderProperty>();
            ExpectToken(TokenType.OpenBrace);

            while (_tokenizer.MoveNext() && _tokenizer.TokenType != TokenType.CloseBrace)
            {
                var property = new Runtime.ShaderProperty();

                property.Name = _tokenizer.Token.ToString();
                ExpectToken(TokenType.OpenParen);
                ExpectToken(TokenType.Identifier);
                property.DisplayName = _tokenizer.ParseQuotedStringValue();
                ExpectToken(TokenType.Comma);
                ExpectToken(TokenType.Identifier);
                property.PropertyType = Enum.Parse<Runtime.ShaderPropertyType>(_tokenizer.Token.ToString(), true);
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

                    case "Inputs":
                        pass.Inputs = ParseInputs();
                    break;

                    case "Features":
                        pass.Keywords = ParseKeywords();
                    break;

                    case "Program":
                        pass.Programs.Add(ParseProgram());
                    break;
                }
            }

            return pass;
        }

        #warning TODO
        private ParsedInputs ParseInputs()
        {
            var inputs = new ParsedInputs();

            return inputs;
        }
        
        #warning TODO
        private Dictionary<string, HashSet<string>> ParseKeywords()
        {
            var dict = new Dictionary<string, HashSet<string>>();

            return dict;
        }

        private Runtime.ShaderSource ParseProgram()
        {
            var program = new Runtime.ShaderSource();
            ExpectToken(TokenType.Identifier);
            program.Stage = Enum.Parse<ShaderStages>(_tokenizer.Token.ToString(), true);
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

            program.SourceCode = content;

            return program;
        }

        private void ExpectToken(TokenType expectedType)
        {
            _tokenizer.MoveNext();

            if (_tokenizer.TokenType != expectedType)
                throw new InvalidOperationException($"Expected {expectedType}, but got {_tokenizer.TokenType}");
        }

        // Convert string ("false", "0", "off", "no") or ("true", "1", "on", "yes") to boolean
        private static bool ConvertToBoolean(string input)
        {
            input = input.Trim();
            input = input.ToLower();
            
            return 
                input.Equals("true", StringComparison.OrdinalIgnoreCase) ||
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
}
