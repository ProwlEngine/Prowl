using Prowl.Runtime.Utils;

namespace Prowl.Runtime
{
    public interface IVariantCompiler
    {
        public ShaderVariant CompileVariant(ShaderSource[] sources, KeyGroup<string, string> keywords);
    }
}