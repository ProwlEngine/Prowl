namespace SourceGenerator.MathFunctions;

[MathFunction("Rsqrt")]
public class RsqrtFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    protected string GetDocumentation(string type, string functionName, bool isScalar, int dimension)
    {
        if (isScalar)
            return $"Returns the reciprocal square root (1/sqrt) of {type} x.";
        else
        {
            var typeName = GetTypeName(type);
            return $"Returns the componentwise reciprocal square root (1/sqrt) of the {typeName}{dimension} vector.";
        }
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var mathClass = GetMathClass(type);

        var functionName = "Rsqrt";
        string oneLiteral = type == "float" ? "1.0f" : "1.0";
        string documentation;

        if (dimension == 1) // Scalar
        {
            documentation = GetDocumentation(type, functionName, true, dimension);
            return $@"    /// <summary>{documentation}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {type} {functionName}({type} x) {{ return {oneLiteral} / {mathClass}.Sqrt(x); }}";
        }
        else // Vector
        {
            var returnType = $"{typeName}{dimension}"; // e.g., Float2, Double3
            documentation = GetDocumentation(type, functionName, false, dimension);
            string componentExpressions = string.Join(", ", components.Select(c => $"{functionName}(x.{c})"));

            return $@"    /// <summary>{documentation}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {returnType} {functionName}({returnType} x) {{ return new {returnType}({componentExpressions}); }}";
        }
    }
}
