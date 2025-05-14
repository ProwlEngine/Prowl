namespace SourceGenerator.MathFunctions;

[MathFunction("Project")]
public class ProjectFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}{dimension}";

        return $@"        /// <summary>Projects vector a onto vector b.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Project({vectorType} a, {vectorType} b)
        {{
            {type} denominator = Dot(b, b);
            if (denominator <= Epsilon)
                return {vectorType}.Zero;
            return b * (Dot(a, b) / denominator);
        }}";
    }
}
