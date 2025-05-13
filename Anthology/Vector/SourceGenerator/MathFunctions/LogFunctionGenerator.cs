namespace SourceGenerator.MathFunctions
{
    [MathFunction("Log")]
    public class LogFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Log";
        protected override string GetDocumentation(string type, string functionName, bool isScalar) =>
            isScalar ? $"Returns the natural logarithm of x."
                     : "Returns the componentwise natural logarithm.";
    }
}