namespace SourceGenerator.MathFunctions
{
    [MathFunction("Reflect")]
    public class ReflectFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            return $@"        /// <summary>Reflects a vector off a normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Reflect({vectorType} vector, {vectorType} normal)
        {{
            {type} dot = Dot(vector, normal);
            return vector - 2 * dot * normal;
        }}";
        }
    }
}