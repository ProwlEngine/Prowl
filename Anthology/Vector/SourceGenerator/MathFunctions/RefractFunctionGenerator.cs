namespace SourceGenerator.MathFunctions
{
    [MathFunction("Refract")]
    public class RefractFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false; // Only for vectors

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";
            var zeroVector = string.Join(", ", components.Select(c => "0"));

            return $@"        /// <summary>Calculates the refraction direction for an incident vector and surface normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Refract({vectorType} incident, {vectorType} normal, {type} eta)
        {{
            {type} dotNI = Dot(normal, incident);
            {type} k = 1{(type == "float" ? "f" : "")} - eta * eta * (1{(type == "float" ? "f" : "")} - dotNI * dotNI);
            return k < 0{(type == "float" ? "f" : "")} ? new {vectorType}({zeroVector}) : eta * incident - (eta * dotNI + Sqrt(k)) * normal;
        }}";
        }
    }
}