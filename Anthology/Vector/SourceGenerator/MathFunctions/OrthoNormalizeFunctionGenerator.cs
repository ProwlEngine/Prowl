namespace SourceGenerator.MathFunctions;

[MathFunction("OrthoNormalize")]
public class OrthoNormalizeFunctionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override bool SupportsScalars => false;
    public override int[] SupportedDimensions => new[] { 3 }; // Typically used for 3D

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        if (dimension != 3)
            return string.Empty; // Skip non-3D implementations

        var typeName = GetTypeName(type);
        var vectorType = $"{typeName}3";

        return $@"        /// <summary>Orthonormalizes a set of three vectors using Gram-Schmidt process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OrthoNormalize(ref {vectorType} normal, ref {vectorType} tangent, ref {vectorType} binormal)
        {{
            normal = Normalize(normal);
            tangent = Normalize(tangent - Project(tangent, normal));
            binormal = Cross(normal, tangent);
        }}

        /// <summary>Orthonormalizes two vectors using Gram-Schmidt process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void OrthoNormalize(ref {vectorType} normal, ref {vectorType} tangent)
        {{
            normal = Normalize(normal);
            tangent = Normalize(tangent - Project(tangent, normal));
        }}";
    }
}
