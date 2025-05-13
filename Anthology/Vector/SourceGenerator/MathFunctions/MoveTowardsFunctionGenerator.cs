namespace SourceGenerator.MathFunctions
{
    [MathFunction("MoveTowards")]
    public class MoveTowardsFunctionGenerator : MathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double" };
        public override bool SupportsScalars => true;

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;

            if (dimension == 1)
            {
                return $@"        /// <summary>Moves a value current towards target.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} MoveTowards({type} current, {type} target, {type} maxDelta)
        {{
            if (Abs(target - current) <= maxDelta)
                return target;
            return current + Sign(target - current) * maxDelta;
        }}";
            }
            else
            {
                return $@"        /// <summary>Moves a vector current towards target.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} MoveTowards({returnType} current, {returnType} target, {type} maxDistanceDelta)
        {{
            {returnType} toVector = target - current;
            {type} distance = Length(toVector);
            if (distance <= maxDistanceDelta || distance < {type}.Epsilon)
                return target;
            return current + toVector / distance * maxDistanceDelta;
        }}";
            }
        }
    }
}