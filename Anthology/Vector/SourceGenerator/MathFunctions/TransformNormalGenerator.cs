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

    #region Tests

    private static readonly HashSet<string> _generatedTestMethods = new HashSet<string>();

    public override List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        var tests = new List<string>();
        var typeName = GetTypeName(type);
        var assertEqualMethod = GetXUnitAssertEqual(type);

        // Only generate tests once per type (not per dimension)
        var typeKey = $"TransformNormal_{type}";
        if (!_generatedTestMethods.Add(typeKey))
        {
            return tests; // Already generated for this type
        }

        // Generate tests for 2D normal transformations
        Generate2DNormalTransformTests(tests, type, typeName, assertEqualMethod);

        // Generate tests for 3D normal transformations
        Generate3DNormalTransformTests(tests, type, typeName, assertEqualMethod);

        return tests;
    }

    private void Generate2DNormalTransformTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";
        var sqrt2 = type == "float" ? "0.7071068f" : "0.7071067811865475"; // 1/sqrt(2)

        // Test 2x2 matrix normal transformation
        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}2_2x2Identity_Test()
        {{
            // Test with identity matrix - normal should remain unchanged
            {typeName}2 normal = new {typeName}2(0.0{suffix}, 1.0{suffix}); // Up vector
            {typeName}2x2 identity = new {typeName}2x2(
                1.0{suffix}, 0.0{suffix},
                0.0{suffix}, 1.0{suffix}
            );
            
            {typeName}2 result = Maths.TransformNormal(normal, identity);
            
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Y")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}2_2x2Scale_Test()
        {{
            // Test with non-uniform scale - normal should be transformed by inverse transpose
            {typeName}2 normal = new {typeName}2(1.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}2x2 scale = new {typeName}2x2(
                2.0{suffix}, 0.0{suffix},  // Scale X by 2
                0.0{suffix}, 4.0{suffix}   // Scale Y by 4
            );
            
            {typeName}2 result = Maths.TransformNormal(normal, scale);
            
            // For non-uniform scale, normal should be scaled by reciprocal and normalized
            // Inverse transpose of scale matrix has (0.5, 0, 0, 0.25)
            // Transformed normal would be (0.5, 0), normalized to (1, 0)
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}2_2x2Rotation_Test()
        {{
            // Test with 45-degree rotation
            {typeName}2 normal = new {typeName}2(1.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}2x2 rotation = new {typeName}2x2(
                {sqrt2}, -{sqrt2},  // 45-degree counter-clockwise rotation
                {sqrt2}, {sqrt2}
            );
            
            {typeName}2 result = Maths.TransformNormal(normal, rotation);
            
            // 45-degree rotation of (1,0) should give approximately (sqrt(2)/2, sqrt(2)/2)
            {assertEqualMethod.Replace("{expected}", sqrt2).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", sqrt2).Replace("{actual}", "result.Y")}
        }}");

        // Test 3x3 matrix with 2D normal (extracting 2x2 portion)
        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}2_3x3Matrix_Test()
        {{
            // Test 2D normal with 3x3 transformation matrix
            {typeName}2 normal = new {typeName}2(0.0{suffix}, 1.0{suffix}); // Up vector
            {typeName}3x3 transform = new {typeName}3x3(
                new {typeName}3(2.0{suffix}, 0.0{suffix}, 5.0{suffix}), // Scale X by 2, translate by 5
                new {typeName}3(0.0{suffix}, 3.0{suffix}, 7.0{suffix}), // Scale Y by 3, translate by 7
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})  // Homogeneous row
            );
            
            {typeName}2 result = Maths.TransformNormal(normal, transform);
            
            // Should extract upper-left 2x2 and transform normal accordingly
            // Inverse transpose of [[2,0],[0,3]] is [[0.5,0],[0,1/3]], so (0,1) -> (0,1/3) -> normalized (0,1)
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Y")}
        }}");
    }

    private void Generate3DNormalTransformTests(List<string> tests, string type, string typeName, string assertEqualMethod)
    {
        var suffix = type == "float" ? "f" : "";
        var sqrt3 = type == "float" ? "0.5773503f" : "0.5773502691896257"; // 1/sqrt(3)

        // Test 3x3 matrix normal transformation
        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}3_3x3Identity_Test()
        {{
            // Test with identity matrix - normal should remain unchanged
            {typeName}3 normal = new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix}); // Forward vector
            {typeName}3x3 identity = new {typeName}3x3(
                new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {typeName}3(0.0{suffix}, 1.0{suffix}, 0.0{suffix}),
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 1.0{suffix})
            );
            
            {typeName}3 result = Maths.TransformNormal(normal, identity);
            
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}3_3x3Scale_Test()
        {{
            // Test with non-uniform scale
            {typeName}3 normal = new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}3x3 scale = new {typeName}3x3(
                new {typeName}3(2.0{suffix}, 0.0{suffix}, 0.0{suffix}), // Scale X by 2
                new {typeName}3(0.0{suffix}, 3.0{suffix}, 0.0{suffix}), // Scale Y by 3
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 4.0{suffix})  // Scale Z by 4
            );
            
            {typeName}3 result = Maths.TransformNormal(normal, scale);
            
            // For non-uniform scale, normal should be scaled by reciprocal and normalized
            // Inverse transpose of scale matrix scales by (0.5, 1/3, 0.25)
            // (1,0,0) -> (0.5,0,0) -> normalized (1,0,0)
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}3_3x3UniformScale_Test()
        {{
            // Test with uniform scale - normal should remain the same direction
            {typeName}3 normal = new {typeName}3({sqrt3}, {sqrt3}, {sqrt3}); // Normalized diagonal vector
            {typeName}3x3 uniformScale = new {typeName}3x3(
                new {typeName}3(2.0{suffix}, 0.0{suffix}, 0.0{suffix}), // Uniform scale by 2
                new {typeName}3(0.0{suffix}, 2.0{suffix}, 0.0{suffix}),
                new {typeName}3(0.0{suffix}, 0.0{suffix}, 2.0{suffix})
            );
            
            {typeName}3 result = Maths.TransformNormal(normal, uniformScale);
            
            // With uniform scale, normal direction should be preserved
            {assertEqualMethod.Replace("{expected}", sqrt3).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", sqrt3).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", sqrt3).Replace("{actual}", "result.Z")}
        }}");

        // Test 4x4 matrix with 3D normal (extracting 3x3 portion)
        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}3_4x4Matrix_Test()
        {{
            // Test 3D normal with 4x4 transformation matrix
            {typeName}3 normal = new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}4x4 transform = new {typeName}4x4(
                new {typeName}4(2.0{suffix}, 0.0{suffix}, 0.0{suffix}, 10.0{suffix}), // Scale X by 2, translate
                new {typeName}4(0.0{suffix}, 3.0{suffix}, 0.0{suffix}, 20.0{suffix}), // Scale Y by 3, translate
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 4.0{suffix}, 30.0{suffix}), // Scale Z by 4, translate
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})   // Homogeneous row
            );
            
            {typeName}3 result = Maths.TransformNormal(normal, transform);
            
            // Should extract upper-left 3x3 and transform normal accordingly
            // Translation should be ignored for normals
            // Inverse transpose of scale matrix should transform (1,0,0) -> (0.5,0,0) -> normalized (1,0,0)
            {assertEqualMethod.Replace("{expected}", "1.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Z")}
        }}");

        tests.Add($@"        [Fact]
        public void TransformNormal_{typeName}3_4x4WithRotation_Test()
        {{
            // Test with a rotation around Y-axis (90 degrees)
            {typeName}3 normal = new {typeName}3(1.0{suffix}, 0.0{suffix}, 0.0{suffix}); // Right vector
            {typeName}4x4 rotationY = new {typeName}4x4(
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 1.0{suffix}, 0.0{suffix}),  // 90° rotation around Y
                new {typeName}4(0.0{suffix}, 1.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {typeName}4(-1.0{suffix}, 0.0{suffix}, 0.0{suffix}, 0.0{suffix}),
                new {typeName}4(0.0{suffix}, 0.0{suffix}, 0.0{suffix}, 1.0{suffix})
            );
            
            {typeName}3 result = Maths.TransformNormal(normal, rotationY);
            
            // 90° rotation around Y should transform (1,0,0) to (0,0,-1)
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.X")}
            {assertEqualMethod.Replace("{expected}", "0.0" + suffix).Replace("{actual}", "result.Y")}
            {assertEqualMethod.Replace("{expected}", "-1.0" + suffix).Replace("{actual}", "result.Z")}
        }}");
    }

    #endregion
}
