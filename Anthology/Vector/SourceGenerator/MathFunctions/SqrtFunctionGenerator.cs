namespace SourceGenerator.MathFunctions;

[MathFunction("Sqrt")]
public class SqrtFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Sqrt";
}
