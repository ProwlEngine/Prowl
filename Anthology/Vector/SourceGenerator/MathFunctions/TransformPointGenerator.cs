using System.Text;
using System.Runtime.CompilerServices; // Required for MethodImplOptions

namespace SourceGenerator.MathFunctions;

[MathFunction("TransformPoint")]
public class TransformPointGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override int[] SupportedDimensions => new[] { 2, 3, 4 };
    public override bool SupportsScalars => false;

    private readonly HashSet<string> _generatedFunctions = new HashSet<string>();

    public override bool SupportsType(string type, int dimension)
    {
        return SupportedTypes.Contains(type) && SupportedDimensions.Contains(dimension);
    }

    public override string GenerateFunction(string type, int dimension, string[] components)
    {
        var sb = new StringBuilder();
        var typeName = GetTypeName(type);

        // Generate 3x3 matrix transforming 2D points (treating as homogeneous coordinates)
        Generate3x3Transform2D(sb, type, typeName);

        // Generate 4x4 matrix transforming 3D points (treating as homogeneous coordinates)
        Generate4x4Transform3D(sb, type, typeName);

        // Generate 4x4 matrix transforming 4D points (direct multiplication)
        Generate4x4Transform4D(sb, type, typeName);

        return sb.ToString();
    }

    private void Generate3x3Transform2D(StringBuilder sb, string type, string typeName)
    {
        var functionKey = $"TransformPoint_{typeName}3x3_{typeName}2";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 2D point using a 3x3 matrix (treating point as homogeneous with w=1).</summary>");
        sb.AppendLine($"        /// <param name=\"point\">The 2D point to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed 2D point with perspective divide applied.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}2 TransformPoint({typeName}2 point, {typeName}3x3 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            // Treat point as homogeneous coordinates (x, y, 1)");
        sb.AppendLine($"            {typeName}3 homogeneous = new {typeName}3(point.X, point.Y, 1.0{(type == "float" ? "f" : "")});");
        sb.AppendLine($"            {typeName}3 transformed = Mul(matrix, homogeneous);");
        sb.AppendLine($"            ");
        sb.AppendLine($"            // Perform perspective divide");
        sb.AppendLine($"            if (Abs(transformed.Z) > {type}.Epsilon)"); // For 2D points in 3x3 matrix, Z is the W component for perspective divide.
        sb.AppendLine($"                return new {typeName}2(transformed.X / transformed.Z, transformed.Y / transformed.Z);");
        sb.AppendLine($"            else");
        sb.AppendLine($"                return new {typeName}2(transformed.X, transformed.Y);");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void Generate4x4Transform3D(StringBuilder sb, string type, string typeName)
    {
        var functionKey = $"TransformPoint_{typeName}4x4_{typeName}3";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 3D point using a 4x4 matrix (treating point as homogeneous with w=1).</summary>");
        sb.AppendLine($"        /// <param name=\"point\">The 3D point to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 4x4 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed 3D point with perspective divide applied.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}3 TransformPoint({typeName}3 point, {typeName}4x4 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            // Treat point as homogeneous coordinates (x, y, z, 1)");
        sb.AppendLine($"            {typeName}4 homogeneous = new {typeName}4(point.X, point.Y, point.Z, 1.0{(type == "float" ? "f" : "")});");
        sb.AppendLine($"            {typeName}4 transformed = Mul(matrix, homogeneous);");
        sb.AppendLine($"            ");
        sb.AppendLine($"            // Perform perspective divide");
        sb.AppendLine($"            if (Abs(transformed.W) > {type}.Epsilon)");
        sb.AppendLine($"                return new {typeName}3(transformed.X / transformed.W, transformed.Y / transformed.W, transformed.Z / transformed.W);");
        sb.AppendLine($"            else");
        sb.AppendLine($"                return new {typeName}3(transformed.X, transformed.Y, transformed.Z);");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void Generate4x4Transform4D(StringBuilder sb, string type, string typeName)
    {
        var functionKey = $"TransformPoint_{typeName}4x4_{typeName}4";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 4D point using a 4x4 matrix (direct multiplication).</summary>");
        sb.AppendLine($"        /// <param name=\"point\">The 4D point to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 4x4 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed 4D point.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}4 TransformPoint({typeName}4 point, {typeName}4x4 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return Mul(matrix, point);");
        sb.AppendLine("        }");
        sb.AppendLine();
    }
}
