namespace SourceGenerator.MathFunctions
{
    [MathFunction("SignedAngleBetween")]
    public class SignedAngleBetweenFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false;
        public override int[] SupportedDimensions => new[] { 2, 3 }; // 2D and 3D only

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";
            var mathClass = GetMathClass(type);

            if (dimension == 2)
            {
                return $@"        /// <summary>Returns the signed angle in radians between two 2D vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} SignedAngleBetween({vectorType} a, {vectorType} b)
        {{
            return {mathClass}.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);
        }}";
            }
            else if (dimension == 3)
            {
                return $@"        /// <summary>Returns the signed angle in radians between two 3D vectors around a reference axis.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} SignedAngleBetween({vectorType} a, {vectorType} b, {vectorType} axis)
        {{
            {type} angle = AngleBetween(a, b);
            {vectorType} cross = Cross(a, b);
            {type} sign = Dot(cross, axis) < 0{(type == "float" ? "f" : "")} ? -1{(type == "float" ? "f" : "")} : 1{(type == "float" ? "f" : "")};
            return angle * sign;
        }}";
            }
            return string.Empty;
        }
    }
}