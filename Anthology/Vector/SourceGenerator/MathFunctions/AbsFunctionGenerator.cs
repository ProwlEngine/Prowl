namespace SourceGenerator.MathFunctions;

// Simple math functions using the new base class
[MathFunction("Abs")]
public class AbsFunctionGenerator : SimpleMathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double", "int" };
    protected override string MathMethodName => "Abs";
}
