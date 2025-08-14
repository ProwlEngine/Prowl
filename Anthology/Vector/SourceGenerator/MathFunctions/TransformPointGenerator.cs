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

    #region Tests

    private static readonly HashSet<string> _generatedTestMethods = new HashSet<string>();

    public override List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        var tests = new List<string>();
        var typeName = GetTypeName(type);
        var assertEqualMethod = GetXUnitAssertEqual(type);

        // Only generate tests once per type (not per dimension)
        var typeKey = $"TransformPoint_Tests_{type}"; // Unique key for type
        if (!_generatedTestMethods.Add(typeKey))
        {
            return tests; // Already generated tests for this type
        }

        // Generate tests for 3x3 matrix transforming 2D points
        Generate3x3Transform2DTest(tests, type, typeName, assertEqualMethod);

        // Generate tests for 4x4 matrix transforming 3D points
        Generate4x4Transform3DTest(tests, type, typeName, assertEqualMethod);

        // Generate tests for 4x4 matrix transforming 4D points
        Generate4x4Transform4DTest(tests, type, typeName, assertEqualMethod);

        return tests;
    }

    private void Generate3x3Transform2DTest(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";
        var epsilon = type == "float" ? "0.0001f" : "0.0000000001";

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}2_3x3Matrix_IdentityTest()
{{
// Test with identity matrix - point should remain unchanged
{typeName}2 point = new {typeName}2(3.0{suffix}, 4.0{suffix});
{typeName}3x3 identity = new {typeName}3x3(
new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})
);

{typeName}2 result = Maths.TransformPoint(point, identity);

{assertEqualMethod.Replace("{expected}", "point.X").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "point.Y").Replace("{actual}", "result.Y")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}2_3x3Matrix_TranslationTest()
{{
// Test with translation matrix
{typeName}2 point = new {typeName}2(1.0{suffix}, 2.0{suffix});
{typeName}3x3 translation = new {typeName}3x3(
new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}),  // Column 0
new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}),  // Column 1
new {typeName}3(5.0{suffix}, 7.0{suffix}, 1.0{suffix})   // Column 2 (Tx, Ty, W). This is the translation column.
);

{typeName}2 result = Maths.TransformPoint(point, translation);

{type} expectedX = 6.0{suffix}; // 1 + 5
{type} expectedY = 9.0{suffix}; // 2 + 7
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}2_3x3Matrix_ScaleTest()
{{
// Test with scale matrix
{typeName}2 point = new {typeName}2(2.0{suffix}, 3.0{suffix});
{typeName}3x3 scale = new {typeName}3x3(
new {typeName}3(2.0{suffix}, 0.0{suffix}, 0.0{suffix}), // scale X by 2
new {typeName}3(0.0{suffix}, 3.0{suffix}, 0.0{suffix}), // scale Y by 3
new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})   // Last column for homogeneous coordinate
);

{typeName}2 result = Maths.TransformPoint(point, scale);

{type} expectedX = 4.0{suffix}; // 2 * 2
{type} expectedY = 9.0{suffix}; // 3 * 3
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}2_3x3Matrix_PerspectiveDivideTest()
{{
// Test perspective divide with non-unity W component
{typeName}2 point = new {typeName}2(4.0{suffix}, 6.0{suffix});
{typeName}3x3 perspective = new {typeName}3x3(
new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
new {typeName}3(0.0{suffix}, 0.0{suffix}, 2.0{suffix}) // This 2.0 in the (3,3) position will be the W component (Z for 3x3)
);

{typeName}2 result = Maths.TransformPoint(point, perspective);

{type} expectedX = 2.0{suffix}; // 4 / 2
{type} expectedY = 3.0{suffix}; // 6 / 2
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
}}");
    }

    private void Generate4x4Transform3DTest(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}3_4x4Matrix_IdentityTest()
{{
// Test with identity matrix - point should remain unchanged
{typeName}3 point = new {typeName}3(2.0{suffix}, 3.0{suffix}, 4.0{suffix});
{typeName}4x4 identity = new {typeName}4x4(
new {typeName}4(1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})
);

{typeName}3 result = Maths.TransformPoint(point, identity);

{assertEqualMethod.Replace("{expected}", "point.X").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "point.Y").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "point.Z").Replace("{actual}", "result.Z")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}3_4x4Matrix_TranslationTest()
{{
// Test with translation matrix
{typeName}3 point = new {typeName}3(1.0{suffix}, 2.0{suffix}, 3.0{suffix});
{typeName}4x4 translation = new {typeName}4x4(
new {typeName}4(1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}),  // Column 0
new {typeName}4(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 0.0{suffix}),  // Column 1
new {typeName}4(0.0{suffix}, 0.0{suffix}, 1.0{suffix}, 0.0{suffix}),  // Column 2
new {typeName}4(5.0{suffix}, 6.0{suffix}, 7.0{suffix}, 1.0{suffix})   // Column 3 (Tx, Ty, Tz, W). This is the translation column.
);

{typeName}3 result = Maths.TransformPoint(point, translation);

{type} expectedX = 6.0{suffix}; // 1 + 5
{type} expectedY = 8.0{suffix}; // 2 + 6
{type} expectedZ = 10.0{suffix}; // 3 + 7
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "expectedZ").Replace("{actual}", "result.Z")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}3_4x4Matrix_ScaleTest()
{{
// Test with scale matrix
{typeName}3 point = new {typeName}3(2.0{suffix}, 3.0{suffix}, 4.0{suffix});
{typeName}4x4 scale = new {typeName}4x4(
new {typeName}4(2.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}), // scale X by 2
new {typeName}4(0.0{suffix}, 3.0{suffix}, 0.0{suffix}, 0.0{suffix}), // scale Y by 3
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.5{suffix}, 0.0{suffix}), // scale Z by 0.5
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})   // Last column for homogeneous coordinate
);

{typeName}3 result = Maths.TransformPoint(point, scale);

{type} expectedX = 4.0{suffix}; // 2 * 2
{type} expectedY = 9.0{suffix}; // 3 * 3
{type} expectedZ = 2.0{suffix}; // 4 * 0.5
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "expectedZ").Replace("{actual}", "result.Z")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}3_4x4Matrix_PerspectiveDivideTest()
{{
// Test perspective divide with non-unity W component
{typeName}3 point = new {typeName}3(6.0{suffix}, 8.0{suffix}, 12.0{suffix});
{typeName}4x4 perspective = new {typeName}4x4(
new {typeName}4(1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 2.0{suffix}) // This 2.0 in the (4,4) position will be the W component
);

{typeName}3 result = Maths.TransformPoint(point, perspective);

{type} expectedX = 3.0{suffix}; // 6 / 2
{type} expectedY = 4.0{suffix}; // 8 / 2
{type} expectedZ = 6.0{suffix}; // 12 / 2
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "expectedZ").Replace("{actual}", "result.Z")}
}}");
    }

    private void Generate4x4Transform4DTest(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}4_4x4Matrix_IdentityTest()
{{
// Test with identity matrix - point should remain unchanged
{typeName}4 point = new {typeName}4(1.0{suffix}, 2.0{suffix}, 3.0{suffix}, 4.0{suffix});
{typeName}4x4 identity = new {typeName}4x4(
new {typeName}4(1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})
);

{typeName}4 result = Maths.TransformPoint(point, identity);

{assertEqualMethod.Replace("{expected}", "point.X").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "point.Y").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "point.Z").Replace("{actual}", "result.Z")}
{assertEqualMethod.Replace("{expected}", "point.W").Replace("{actual}", "result.W")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}4_4x4Matrix_ScaleTest()
{{
// Test with scale transformation
{typeName}4 point = new {typeName}4(2.0{suffix}, 3.0{suffix}, 4.0{suffix}, 5.0{suffix});
{typeName}4x4 scale = new {typeName}4x4(
new {typeName}4(2.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}), // scale X by 2
new {typeName}4(0.0{suffix}, 3.0{suffix}, 0.0{suffix}, 0.0{suffix}), // scale Y by 3
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.5{suffix}, 0.0{suffix}), // scale Z by 0.5
new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.2{suffix})   // scale W by 0.2
);

{typeName}4 result = Maths.TransformPoint(point, scale);

{type} expectedX = 4.0{suffix}; // 2 * 2
{type} expectedY = 9.0{suffix}; // 3 * 3
{type} expectedZ = 2.0{suffix}; // 4 * 0.5
{type} expectedW = 1.0{suffix}; // 5 * 0.2
{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "expectedZ").Replace("{actual}", "result.Z")}
{assertEqualMethod.Replace("{expected}", "expectedW").Replace("{actual}", "result.W")}
}}");

        tests.Add($@"        [Fact]
public void TransformPoint_{typeName}4_4x4Matrix_GeneralTransformTest()
{{
// Test with a general transformation matrix
{typeName}4 point = new {typeName}4(1.0{suffix}, 1.0{suffix}, 1.0{suffix}, 1.0{suffix});
{typeName}4x4 transform = new {typeName}4x4(
new {typeName}4(1.0{suffix}, 2.0{suffix}, 3.0{suffix}, 4.0{suffix}),   // Column 0
new {typeName}4(5.0{suffix}, 6.0{suffix}, 7.0{suffix}, 8.0{suffix}),   // Column 1
new {typeName}4(9.0{suffix}, 10.0{suffix}, 11.0{suffix}, 12.0{suffix}), // Column 2
new {typeName}4(13.0{suffix}, 14.0{suffix}, 15.0{suffix}, 16.0{suffix}) // Column 3
);

{typeName}4 result = Maths.TransformPoint(point, transform);

// Corrected Expected values based on matrix (column-major) * vector (column) multiplication
{type} expectedX = 28.0{suffix}; // 1 + 5 + 9 + 13
{type} expectedY = 32.0{suffix}; // 2 + 6 + 10 + 14
{type} expectedZ = 36.0{suffix}; // 3 + 7 + 11 + 15
{type} expectedW = 40.0{suffix}; // 4 + 8 + 12 + 16

{assertEqualMethod.Replace("{expected}", "expectedX").Replace("{actual}", "result.X")}
{assertEqualMethod.Replace("{expected}", "expectedY").Replace("{actual}", "result.Y")}
{assertEqualMethod.Replace("{expected}", "expectedZ").Replace("{actual}", "result.Z")}
{assertEqualMethod.Replace("{expected}", "expectedW").Replace("{actual}", "result.W")}
}}");
    }

    #endregion
}
