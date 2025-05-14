namespace SourceGenerator.MathFunctions;

[MathFunction("IsParallel")]
public class IsParallelFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";
        var mathClass = GetMathClass(type);
        var tolerance = type == "float" ? "1e-6f" : "1e-15";

        return $@"        /// <summary>Checks if two vectors are parallel within a tolerance.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsParallel({vectorType} a, {vectorType} b, {type} tolerance = {tolerance})
        {{
            {type} normalizedDot = {mathClass}.Abs(Dot(Normalize(a), Normalize(b)));
            return normalizedDot >= 1{(type == "float" ? "f" : "")} - tolerance;
        }}";
    }
}
