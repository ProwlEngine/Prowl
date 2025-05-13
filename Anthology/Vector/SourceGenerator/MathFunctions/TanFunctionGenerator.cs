namespace SourceGenerator.MathFunctions
{
    [MathFunction("Tan")]
    public class TanFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Tan";
    }
}