using System.Reflection;

namespace SourceGenerator
{
    // Attribute to mark math function generators
    [AttributeUsage(AttributeTargets.Class)]
    public class MathFunctionAttribute : Attribute
    {
        public string FunctionName { get; }
        public MathFunctionAttribute(string functionName)
        {
            FunctionName = functionName;
        }
    }

    // Base class for math function generators
    public abstract class MathFunctionGenerator
    {
        public virtual string[] SupportedTypes => new[] { "float", "double" };
        public virtual int[] SupportedDimensions => new[] { 2, 3, 4 };
        public virtual bool SupportsScalars => false;

        public abstract string GenerateFunction(string type, int dimension, string[] components);
        public virtual bool SupportsType(string type, int dimension)
        {
            // For scalars (dimension 1), check SupportsScalars flag
            if (dimension == 1)
                return SupportsScalars && SupportedTypes.Contains(type);

            // For vectors, check both type and dimension
            return SupportedTypes.Contains(type) && SupportedDimensions.Contains(dimension);
        }

        protected string GetTypeName(string type)
        {
            return type switch {
                "float" => "Float",
                "double" => "Double",
                "int" => "Int",
                "byte" => "Byte",
                "ushort" => "UShort",
                "uint" => "UInt",
                "ulong" => "ULong",
                "bool" => "Bool",
                _ => type
            };
        }

        protected string GetMathClass(string type)
        {
            return type == "float" ? "MathF" : "Math";
        }

        protected static string[] GetComponents(int dimension)
        {
            if (dimension == 2) return new[] { "X", "Y" };
            if (dimension == 3) return new[] { "X", "Y", "Z" };
            if (dimension == 4) return new[] { "X", "Y", "Z", "W" };
            throw new ArgumentException($"Unsupported dimension: {dimension}");
        }
    }

    // Simple math generator for functions that just wrap System.Math
    public abstract class SimpleMathFunctionGenerator : MathFunctionGenerator
    {
        public override bool SupportsScalars => true;

        protected abstract string MathMethodName { get; }
        protected virtual bool RequiresTwoParameters => false;
        protected virtual string GetDocumentation(string type, string functionName, bool isScalar) =>
            isScalar ? $"Returns the {functionName.ToLower()} of {type} x."
                     : $"Returns the componentwise {functionName.ToLower()} of the vector.";

        public override string GenerateFunction(string type, int dimension, string[] components)
        {
            var typeName = GetTypeName(type);
            var mathClass = GetMathClass(type);
            var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
            var functionName = GetType().GetCustomAttribute<MathFunctionAttribute>()?.FunctionName ?? "UnknownFunction";

            if (dimension == 1)
            {
                var parameters = RequiresTwoParameters ? $"{type} x, {type} y" : $"{type} x";
                var arguments = RequiresTwoParameters ? "x, y" : "x";

                return $@"        /// <summary>{GetDocumentation(type, functionName, true)}</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {type} {functionName}({parameters}) {{ return {mathClass}.{MathMethodName}({arguments}); }}";
            }
            else
            {
                string componentExpressions;
                if (RequiresTwoParameters)
                {
                    componentExpressions = string.Join(", ", components.Select(c => $"{functionName}(x.{c}, y.{c})"));
                    return $@"        /// <summary>{GetDocumentation(type, functionName, false)}</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} {functionName}({returnType} x, {returnType} y) {{ return new {returnType}({componentExpressions}); }}";
                }
                else
                {
                    componentExpressions = string.Join(", ", components.Select(c => $"{functionName}(x.{c})"));
                    return $@"        /// <summary>{GetDocumentation(type, functionName, false)}</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static {returnType} {functionName}({returnType} x) {{ return new {returnType}({componentExpressions}); }}";
                }
            }
        }
    }
}