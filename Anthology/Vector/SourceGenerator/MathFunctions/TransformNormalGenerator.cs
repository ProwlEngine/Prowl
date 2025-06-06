using System.Text;

namespace SourceGenerator.MathFunctions;

[MathFunction("TransformNormal")]
public class TransformNormalGenerator : MathFunctionGenerator
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

        // Generate normal transformations for 2D and 3D
        Generate2DNormalTransform(sb, type, typeName);
        Generate3DNormalTransform(sb, type, typeName);

        return sb.ToString();
    }

    private void Generate2DNormalTransform(StringBuilder sb, string type, string typeName)
    {
        // 2x2 matrix transforming 2D normal
        var functionKey = $"TransformNormal_{typeName}2x2_{typeName}2";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 2D normal vector using the inverse transpose of a 2x2 matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"normal\">The 2D normal vector to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 2x2 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed and normalized normal vector.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}2 TransformNormal({typeName}2 normal, {typeName}2x2 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            // For normals, we need to use the inverse transpose of the matrix");
        sb.AppendLine($"            {typeName}2x2 invTranspose = Transpose(Inverse(matrix));");
        sb.AppendLine($"            {typeName}2 transformed = Mul(invTranspose, normal);");
        sb.AppendLine($"            return Normalize(transformed);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 3x3 matrix transforming 2D normal (extracting 2x2 portion)
        functionKey = $"TransformNormal_{typeName}3x3_{typeName}2";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 2D normal vector using the upper-left 2x2 portion of a 3x3 matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"normal\">The 2D normal vector to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed and normalized normal vector.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}2 TransformNormal({typeName}2 normal, {typeName}3x3 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            // Extract the upper-left 2x2 portion for rotation/scale");
        sb.AppendLine($"            {typeName}2x2 upperLeft = new {typeName}2x2(");
        sb.AppendLine($"                matrix.c0.X, matrix.c0.Y,");
        sb.AppendLine($"                matrix.c1.X, matrix.c1.Y");
        sb.AppendLine($"            );");
        sb.AppendLine($"            return TransformNormal(normal, upperLeft);");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private void Generate3DNormalTransform(StringBuilder sb, string type, string typeName)
    {
        // 3x3 matrix transforming 3D normal
        var functionKey = $"TransformNormal_{typeName}3x3_{typeName}3";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 3D normal vector using the inverse transpose of a 3x3 matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"normal\">The 3D normal vector to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 3x3 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed and normalized normal vector.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}3 TransformNormal({typeName}3 normal, {typeName}3x3 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            // For normals, we need to use the inverse transpose of the matrix");
        sb.AppendLine($"            {typeName}3x3 invTranspose = Transpose(Inverse(matrix));");
        sb.AppendLine($"            {typeName}3 transformed = Mul(invTranspose, normal);");
        sb.AppendLine($"            return Normalize(transformed);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 4x4 matrix transforming 3D normal (extracting 3x3 portion)
        functionKey = $"TransformNormal_{typeName}4x4_{typeName}3";
        if (!_generatedFunctions.Add(functionKey)) return;

        sb.AppendLine($"        /// <summary>Transforms a 3D normal vector using the upper-left 3x3 portion of a 4x4 matrix.</summary>");
        sb.AppendLine($"        /// <param name=\"normal\">The 3D normal vector to transform.</param>");
        sb.AppendLine($"        /// <param name=\"matrix\">The 4x4 transformation matrix.</param>");
        sb.AppendLine($"        /// <returns>The transformed and normalized normal vector.</returns>");
        sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine($"        public static {typeName}3 TransformNormal({typeName}3 normal, {typeName}4x4 matrix)");
        sb.AppendLine("        {");
        sb.AppendLine($"            // Extract the upper-left 3x3 portion for rotation/scale");
        sb.AppendLine($"            {typeName}3x3 upperLeft = new {typeName}3x3(");
        sb.AppendLine($"                matrix.c0.X, matrix.c0.Y, matrix.c0.Z,");
        sb.AppendLine($"                matrix.c1.X, matrix.c1.Y, matrix.c1.Z,");
        sb.AppendLine($"                matrix.c2.X, matrix.c2.Y, matrix.c2.Z");
        sb.AppendLine($"            );");
        sb.AppendLine($"            return TransformNormal(normal, upperLeft);");
        sb.AppendLine("        }");
        sb.AppendLine();
    }
}
