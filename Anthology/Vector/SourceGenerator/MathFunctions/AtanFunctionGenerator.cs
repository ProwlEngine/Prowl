namespace SourceGenerator.MathFunctions
{
    [MathFunction("Atan")]
    public class AtanFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Atan";
    }
}