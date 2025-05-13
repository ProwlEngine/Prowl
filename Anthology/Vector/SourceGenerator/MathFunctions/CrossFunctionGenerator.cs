namespace SourceGenerator.MathFunctions
{
    [MathFunction("Cross")]
    public class CrossFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override int[] SupportedDimensions => new[] { 3 }; // Cross product only makes sense for 3D
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            if (dimension != 3)
                throw new InvalidOperationException("Cross product is only defined for 3D vectors");

            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}3";

            return $@"        /// <summary>Returns the cross product of two {vectorType} vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} Cross({vectorType} x, {vectorType} y)
        {{
            return new {vectorType}(
                x.Y * y.Z - x.Z * y.Y,
                x.Z * y.X - x.X * y.Z,
                x.X * y.Y - x.Y * y.X
            );
        }}";
        }
    }
}