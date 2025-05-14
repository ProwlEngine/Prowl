namespace SourceGenerator.MathFunctions;

[MathFunction("Normalize")]
public class NormalizeFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false; // Only makes sense for vectors

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        return $@"        /// <summary>Returns the normalized version of a {vectorType} vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Normalize({vectorType} x) {{ return x.Normalized; }}";
    }
}
