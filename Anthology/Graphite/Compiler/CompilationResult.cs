using Prowl.Graphite.Shaders;

namespace Prowl.Graphite.Compiler;


public struct CompilationResult
{
    public VariantSpace[] VariantSpaces;
    public VariantResult[] CompiledVariants;
}


public struct VariantResult
{
    public Keyword[] Variants;

    // One compiled program description per registered backend.
    public (ShaderDescription Description, GraphicsBackend Backend)[] Backends;
}
