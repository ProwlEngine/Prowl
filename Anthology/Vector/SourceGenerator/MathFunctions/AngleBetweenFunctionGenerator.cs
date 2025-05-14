namespace SourceGenerator.MathFunctions;

[MathFunction("AngleBetween")]
public class AngleBetweenFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";
        var mathClass = GetMathClass(type);

        return $@"        /// <summary>Returns the angle in radians between two vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} AngleBetween({vectorType} a, {vectorType} b)
        {{
            {type} dot = Dot(Normalize(a), Normalize(b));
            return {mathClass}.Acos(Clamp(dot, -1{(type == "float" ? "f" : "")}, 1{(type == "float" ? "f" : "")}));
        }}";
    }
}
