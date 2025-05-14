namespace SourceGenerator.MathFunctions;

[MathFunction("Round")]
public class RoundFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Round";
}
