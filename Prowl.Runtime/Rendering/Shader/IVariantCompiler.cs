using Prowl.Runtime.Utils;

namespace Prowl.Runtime
{
    public interface IVariantCompiler
    {
        public ShaderVariant CompileVariant(KeywordState keywords);
    }
}