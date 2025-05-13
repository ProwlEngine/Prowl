namespace SourceGenerator.MathFunctions
{
    [MathFunction("Acos")]
    public class AcosFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Acos";
    }
}