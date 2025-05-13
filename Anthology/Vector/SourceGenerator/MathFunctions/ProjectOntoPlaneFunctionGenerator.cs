namespace SourceGenerator.MathFunctions
{
    [MathFunction("ProjectOntoPlane")]
    public class ProjectOntoPlaneFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => false;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var vectorType = $"{typeName}{dimension}";

            return $@"        /// <summary>Projects a vector onto a plane defined by a normal vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {vectorType} ProjectOntoPlane({vectorType} vector, {vectorType} planeNormal)
        {{
            return vector - Project(vector, planeNormal);
        }}";
        }
    }
}