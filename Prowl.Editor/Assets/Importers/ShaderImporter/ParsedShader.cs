using System.Collections.Immutable;
using System.Text;
using Veldrid;

namespace Prowl.Editor.ShaderParser
{
    public class ParsedShader
    {
        public string Name { get; set; }
        public List<Runtime.ShaderProperty> Properties { get; set; } = new List<Runtime.ShaderProperty>();

        public ParsedGlobalState Global { get; set; }
        public List<ParsedPass> Passes { get; set; } = new List<ParsedPass>();
        
        public string Fallback { get; set; }
    }

    public class ParsedInputs
    {
        public Runtime.MeshResource[] Inputs { get; set; }= [ ];
        public List<Runtime.ShaderResource[]> Resources { get; set; } = new();
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
        public FaceCullMode Cull { get; set; }

        public ParsedInputs Inputs { get; set; }
        public Dictionary<string, ImmutableHashSet<string>> Keywords { get; set; } = new Dictionary<string, ImmutableHashSet<string>>();
        public List<Runtime.ShaderSource> Programs { get; set; } = new List<Runtime.ShaderSource>();
    }
}
