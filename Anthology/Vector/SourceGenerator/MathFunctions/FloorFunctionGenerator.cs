namespace SourceGenerator.MathFunctions;

[MathFunction("Floor")]
public class FloorFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Floor";
}
