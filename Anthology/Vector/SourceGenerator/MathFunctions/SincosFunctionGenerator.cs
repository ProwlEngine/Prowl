using System.Text;

namespace SourceGenerator.MathFunctions;

[MathFunction("Sincos")]
public class SincosFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    protected string GetDocumentation(string type, string functionName, bool isScalar, int dimension)
    {
        if (isScalar)
            return $"Computes the sine and cosine of {type} x, returning them as out parameters.";
        else
        {
            var typeName = GetTypeName(type);
            return $"Computes the componentwise sine and cosine of the {typeName}{dimension} vector x, returning them as out parameters s and c.";
        }
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type); // e.g., Float, Double
        var mathClass = GetMathClass(type); // e.g., MathF, Math
        var functionName = "Sincos"; // From attribute
        string documentation;

        if (dimension == 1) // Scalar
        {
            documentation = GetDocumentation(type, functionName, true, dimension);
            return $@"    /// <summary>{documentation}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void {functionName}({type} x, out {type} s, out {type} c)
    {{
        s = {mathClass}.Sin(x);
        c = {mathClass}.Cos(x);
    }}";
        }
        else // Vector
        {
            var vectorType = $"{typeName}{dimension}"; // e.g., Float2, Double3
            documentation = GetDocumentation(type, functionName, false, dimension);

            var individualSincosCalls = new StringBuilder();
            for (int i = 0; i < dimension; i++)
            {
                // Calls the scalar Sincos for each component
                individualSincosCalls.AppendLine($"        {functionName}(x.{components[i]}, out s.{components[i]}, out c.{components[i]});");
            }

            return $@"    /// <summary>{documentation}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void {functionName}({vectorType} x, out {vectorType} s, out {vectorType} c)
    {{
        // For structs, 's' and 'c' are automatically initialized to their default values (all members zero), So dont need to initialize them here.
{individualSincosCalls.ToString().TrimEnd()}
    }}";
        }
    }
}
