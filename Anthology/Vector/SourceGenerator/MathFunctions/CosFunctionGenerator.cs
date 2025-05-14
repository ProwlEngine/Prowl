namespace SourceGenerator.MathFunctions;

[MathFunction("Cos")]
public class CosFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Cos";
}
