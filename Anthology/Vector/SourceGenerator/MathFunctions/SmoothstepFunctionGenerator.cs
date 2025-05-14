namespace SourceGenerator.MathFunctions;

[MathFunction("Smoothstep")]
public class SmoothstepFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => true;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

        if (dimension == 1)
        {
            return $@"        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 when edge0 < x < edge1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} Smoothstep({type} edge0, {type} edge1, {type} x)
        {{
            {type} t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3{(type == "float" ? "f" : "")} - 2{(type == "float" ? "f" : "")} * t);
        }}";
        }
        else
        {
            var componentExpressions = string.Join(", ", components.Select(c => $"Smoothstep(edge0.{c}, edge1.{c}, x.{c})"));
            return $@"        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} Smoothstep({returnType} edge0, {returnType} edge1, {returnType} x)
        {{
            return new {returnType}({componentExpressions});
        }}";
        }
    }
}
