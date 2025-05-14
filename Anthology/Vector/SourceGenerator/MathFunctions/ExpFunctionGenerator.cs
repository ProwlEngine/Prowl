namespace SourceGenerator.MathFunctions;

[MathFunction("Exp")]
public class ExpFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Exp";
    protected override string GetDocumentation(string type, string functionName, bool isScalar) =>
        isScalar ? $"Returns e raised to the power of x."
                 : "Returns the componentwise exponential function.";
}
