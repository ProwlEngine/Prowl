using System.Text;
using Veldrid;

namespace Prowl.Editor.ShaderParser
{
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

    public class ParsedInputs
    {

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

        public ParsedInputs Inputs { get; set; }
        public Dictionary<string, HashSet<string>> Keywords { get; set; } = new Dictionary<string, HashSet<string>>();
        public List<ParsedShaderProgram> Programs { get; set; } = new List<ParsedShaderProgram>();
    }

    public class ParsedShaderProgram
    {
        public ShaderStages Type { get; set; }
        public string Content { get; set; }
    }
}
