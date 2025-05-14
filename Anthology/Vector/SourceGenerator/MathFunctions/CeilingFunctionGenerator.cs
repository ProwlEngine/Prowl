namespace SourceGenerator.MathFunctions;

[MathFunction("Ceiling")]
public class CeilingFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Ceiling";
}
