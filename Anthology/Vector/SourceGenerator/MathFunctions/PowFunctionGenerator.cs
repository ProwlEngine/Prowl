namespace SourceGenerator.MathFunctions
{
    [MathFunction("Pow")]
    public class PowFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Pow";
        protected override bool RequiresTwoParameters => true;
        protected override string GetDocumentation(string type, string functionName, bool isScalar) =>
            isScalar ? $"Returns x raised to the power of y."
                     : "Returns the componentwise power of x raised to y.";
    }
}