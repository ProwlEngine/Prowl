namespace SourceGenerator.MathFunctions;

[MathFunction("Frac")]
public class FracFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

        if (dimension == 1)
        {
            return $@"        /// <summary>Returns the fractional part of a number.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Frac({type} x) {{ return x - Floor(x); }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"Frac(x.{c})"));
            return $@"        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Frac({returnType} x) {{ return new {returnType}({componentExpressions}); }}";
        }
    }
}
