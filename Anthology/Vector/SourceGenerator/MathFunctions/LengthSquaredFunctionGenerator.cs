namespace SourceGenerator.MathFunctions;

[MathFunction("LengthSquared")]
public class LengthSquaredFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        return $@"        /// <summary>Returns the squared length of a {vectorType} vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} LengthSquared({vectorType} x) {{ return x.LengthSquared; }}";
    }
}
