namespace SourceGenerator.MathFunctions
{
    [MathFunction("Atan2")]
    public class Atan2FunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        protected override string MathMethodName => "Atan2";
        protected override bool RequiresTwoParameters => true;
        protected override string GetDocumentation(string type, string functionName, bool isScalar) =>
            isScalar ? $"Returns the arctangent of y/x in radians."
                     : "Returns the componentwise arctangent of y/x for each component.";
    }
}