namespace SourceGenerator.MathFunctions;

[MathFunction("DistanceSquared")]
public class DistanceSquaredFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        return $@"        /// <summary>Returns the squared distance between two {vectorType} points.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} DistanceSquared({vectorType} x, {vectorType} y)
        {{
            return LengthSquared(x - y);
        }}";
    }
}
