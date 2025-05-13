namespace SourceGenerator.MathFunctions
{
    [MathFunction("MoveTowardsAngle")]
    public class MoveTowardsAngleFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Moves an angle towards a target angle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} MoveTowardsAngle({type} current, {type} target, {type} maxDelta)
        {{
            {type} deltaAngle = DeltaAngle(current, target);
            if (-maxDelta < deltaAngle && deltaAngle < maxDelta)
                return target;
            target = current + deltaAngle;
            return MoveTowards(current, target, maxDelta);
        }}";
            }
            else
            {
                var componentExpressions = string.Join(", ", components.Select(c => $"MoveTowardsAngle(current.{c}, target.{c}, maxDelta)"));
                return $@"        /// <summary>Moves an angle vector towards a target angle vector componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} MoveTowardsAngle({returnType} current, {returnType} target, {type} maxDelta) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }
}