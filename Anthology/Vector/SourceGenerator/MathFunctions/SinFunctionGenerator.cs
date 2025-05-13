namespace SourceGenerator.MathFunctions
{
    [MathFunction("Sin")]
    public class SinFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Sin";
    }
}