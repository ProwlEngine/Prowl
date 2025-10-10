using System.Text;
using System.Runtime.CompilerServices;

namespace SourceGenerator.MathFunctions;

[MathFunction("TransformDirection")]
public class TransformDirectionGenerator : MathFunctionGenerator
{
    public override string[] SupportedTypes => new[] { "float", "double" };
    public override int[] SupportedDimensions => new[] { 2, 3 };
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

        // Generate 2D direction transformations
        Generate2DDirectionTransform(sb, type, typeName);

        // Generate 3D direction transformations
        Generate3DDirectionTransform(sb, type, typeName);

        return sb.ToString();
    }

    private void Generate2DDirectionTransform(StringBuilder sb, string type, string typeName)
    {
        // 2x2 matrix transforming 2D direction
        var functionKey_2x2_2 = $"TransformDirection_{typeName}2x2_{typeName}2";
        if (_generatedFunctions.Add(functionKey_2x2_2)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 2D direction vector using a 2x2 matrix (ignores translation).</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 2D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 2x2 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}2 TransformDirection({typeName}2 direction, {typeName}2x2 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return Mul(matrix, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 3x3 matrix transforming 2D direction (extracting 2x2 portion)
        var functionKey_3x3_2 = $"TransformDirection_{typeName}3x3_{typeName}2";
        if (_generatedFunctions.Add(functionKey_3x3_2)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 2D direction vector using the upper-left 2x2 portion of a 3x3 matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 2D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}2 TransformDirection({typeName}2 direction, {typeName}3x3 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // Extract the upper-left 2x2 portion for rotation/scale (no translation)");
            sb.AppendLine($"            {typeName}2x2 upperLeft = new {typeName}2x2(");
            sb.AppendLine($"                matrix.c0.X, matrix.c0.Y,"); // Note: c0.X and c0.Y are first column elements
            sb.AppendLine($"                matrix.c1.X, matrix.c1.Y"); // Note: c1.X and c1.Y are second column elements
            sb.AppendLine($"            );");
            sb.AppendLine($"            return Mul(upperLeft, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    private void Generate3DDirectionTransform(StringBuilder sb, string type, string typeName)
    {
        // 3x3 matrix transforming 3D direction
        var functionKey_3x3_3 = $"TransformDirection_{typeName}3x3_{typeName}3";
        if (_generatedFunctions.Add(functionKey_3x3_3)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 3D direction vector using a 3x3 matrix (ignores translation).</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 3D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}3 TransformDirection({typeName}3 direction, {typeName}3x3 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return Mul(matrix, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 4x4 matrix transforming 3D direction (extracting 3x3 portion)
        var functionKey_4x4_3 = $"TransformDirection_{typeName}4x4_{typeName}3";
        if (_generatedFunctions.Add(functionKey_4x4_3)) // Ensure unique generation
        {
            sb.AppendLine($"        /// <summary>Transforms a 3D direction vector using the upper-left 3x3 portion of a 4x4 matrix.</summary>");
            sb.AppendLine($"        /// <param name=\"direction\">The 3D direction vector to transform.</param>");
            sb.AppendLine($"        /// <param name=\"matrix\">The 4x4 transformation matrix.</param>");
            sb.AppendLine($"        /// <returns>The transformed direction vector.</returns>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {typeName}3 TransformDirection({typeName}3 direction, {typeName}4x4 matrix)");
            sb.AppendLine("        {");
            sb.AppendLine($"            // Extract the upper-left 3x3 portion for rotation/scale (no translation)");
            sb.AppendLine($"            {typeName}3x3 upperLeft = new {typeName}3x3(");
            // Assuming Double3x3 constructor takes columns (c0, c1, c2)
            // And each DoubleN is (X, Y, Z, W) where X is first element, Y second, etc.
            // So matrix.c0 is the first column, matrix.c0.X is first element of first column (M11)
            sb.AppendLine($"                matrix.c0.X, matrix.c0.Y, matrix.c0.Z,");
            sb.AppendLine($"                matrix.c1.X, matrix.c1.Y, matrix.c1.Z,");
            sb.AppendLine($"                matrix.c2.X, matrix.c2.Y, matrix.c2.Z");
            sb.AppendLine($"            );");
            sb.AppendLine($"            return Mul(upperLeft, direction);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }
}
