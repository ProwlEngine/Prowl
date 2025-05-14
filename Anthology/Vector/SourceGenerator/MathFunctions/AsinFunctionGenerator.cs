namespace SourceGenerator.MathFunctions;

[MathFunction("Asin")]
public class AsinFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    protected override string MathMethodName => "Asin";
}
