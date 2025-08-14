using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SourceGenerator;

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
    public virtual string[] SupportedTypes => new string[] { "float", "double" };
    public virtual int[] SupportedDimensions => new int[] { 2, 3, 4 };
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

    public virtual List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        return new List<string>();
    }


    protected string GetTypeName(string type)
    {
        return type switch
        {
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
        if (dimension == 2) return new string[] { "X", "Y" };
        if (dimension == 3) return new string[] { "X", "Y", "Z" };
        if (dimension == 4) return new string[] { "X", "Y", "Z", "W" };
        throw new ArgumentException($"Unsupported dimension: {dimension}");
    }

    protected string GetTestValue(string type, int index)
    {
        // Provide some basic test values based on type and index
        return type switch
        {
            "float" => $"{(index % 2 == 0 ? (index + 1) * 1.5f : -(index + 1) * 0.5f)}f",
            "double" => $"{(index % 2 == 0 ? (index + 1) * 1.5 : -(index + 1) * 0.5)}",
            "int" => $"{(index % 2 == 0 ? (index + 1) * 2 : -(index + 1))}",
            "byte" => $"(byte){(index + 1)}",
            "ushort" => $"(ushort){(index + 1) * 10}",
            "uint" => $"(uint){(index + 1) * 100}",
            "ulong" => $"(ulong){(index + 1) * 1000}",
            "bool" => $"{(index % 2 == 0 ? "true" : "false")}",
            _ => "0"
        };
    }

    protected string GetXUnitAssertEqual(string type)
    {
        return type switch
        {
            "float" => "Assert.Equal({expected}, {actual}, 0.0001f);",
            "double" => "Assert.Equal({expected}, {actual}, 0.0000000001);",
            _ => "Assert.Equal({expected}, {actual});"
        };
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

            return $@"    /// <summary>{GetDocumentation(type, functionName, true)}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {type} {functionName}({parameters}) {{ return {mathClass}.{MathMethodName}({arguments}); }}";
        }
        else
        {
            string componentExpressions;
            if (RequiresTwoParameters)
            {
                componentExpressions = string.Join(", ", components.Select(c => $"{functionName}(x.{c}, y.{c})"));
                return $@"    /// <summary>{GetDocumentation(type, functionName, false)}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {returnType} {functionName}({returnType} x, {returnType} y) {{ return new {returnType}({componentExpressions}); }}";
            }
            else
            {
                componentExpressions = string.Join(", ", components.Select(c => $"{functionName}(x.{c})"));
                return $@"    /// <summary>{GetDocumentation(type, functionName, false)}</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static {returnType} {functionName}({returnType} x) {{ return new {returnType}({componentExpressions}); }}";
            }
        }
    }

    public override List<string> GenerateTestMethods(string type, int dimension, string[] components)
    {
        var tests = new List<string>();
        var functionName = GetType().GetCustomAttribute<MathFunctionAttribute>()?.FunctionName ?? "UnknownFunction";
        var typeName = GetTypeName(type);
        var returnType = dimension > 1 ? $"{typeName}{dimension}" : type;
        var assertEqualMethod = GetXUnitAssertEqual(type);

        // Determine if we need to explicitly cast the argument to the System.Math method
        // This is primarily for float/double to avoid ambiguity with decimal overloads.
        string castPrefix = "";
        if (type == "float" || type == "double" || type == "int") // Added int to ensure correct overload for Math.Ceiling(decimal) scenarios
        {
            castPrefix = $"({type})";
        }

        if (dimension == 1) // Scalar test
        {
            if (RequiresTwoParameters)
            {
                // Simple test case for scalar, two parameters
                var xVal = GetTestValue(type, 0);
                var yVal = GetTestValue(type, 1);
                var expectedScalar = $"System.{GetMathClass(type)}.{MathMethodName}({castPrefix}{xVal}, {castPrefix}{yVal})";

                tests.Add($@"        [Fact]
        public void {functionName}_{typeName}ScalarTest()
        {{
            {type} x = {xVal};
            {type} y = {yVal};
            {type} result = Maths.{functionName}(x, y);
            {type} expected = {expectedScalar};
            {assertEqualMethod.Replace("{expected}", "expected").Replace("{actual}", "result")}
        }}");
            }
            else
            {
                // Simple test case for scalar, one parameter
                var xVal = GetTestValue(type, 0);
                var expectedScalar = $"System.{GetMathClass(type)}.{MathMethodName}({castPrefix}{xVal})";

                tests.Add($@"        [Fact]
        public void {functionName}_{typeName}ScalarTest()
        {{
            {type} x = {xVal};
            {type} result = Maths.{functionName}(x);
            {type} expected = {expectedScalar};
            {assertEqualMethod.Replace("{expected}", "expected").Replace("{actual}", "result")}
        }}");
            }
        }
        else // Vector test
        {
            string xParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, i)));
            string yParams = string.Join(", ", components.Select((c, i) => GetTestValue(type, i + dimension))); // Use different values for y

            if (RequiresTwoParameters)
            {
                // Vector, two parameters
                string expectedComponents = string.Join(", ", components.Select((c, i) =>
                    $"System.{GetMathClass(type)}.{MathMethodName}({castPrefix}{GetTestValue(type, i)}, {castPrefix}{GetTestValue(type, i + dimension)})"
                ));
                string expectedVector = $"new {returnType}({expectedComponents})";

                tests.Add($@"        [Fact]
        public void {functionName}_{typeName}{dimension}Test()
        {{
            {returnType} x = new {returnType}({xParams});
            {returnType} y = new {returnType}({yParams});
            {returnType} result = Maths.{functionName}(x, y);
            {returnType} expected = {expectedVector};
            {string.Join("\n            ", components.Select((c, i) =>
                assertEqualMethod.Replace("{expected}", $"expected.{c}").Replace("{actual}", $"result.{c}")
            ))}
        }}");
            }
            else
            {
                // Vector, one parameter
                string expectedComponents = string.Join(", ", components.Select((c, i) =>
                    $"System.{GetMathClass(type)}.{MathMethodName}({castPrefix}{GetTestValue(type, i)})"
                ));
                string expectedVector = $"new {returnType}({expectedComponents})";

                tests.Add($@"        [Fact]
        public void {functionName}_{typeName}{dimension}Test()
        {{
            {returnType} x = new {returnType}({xParams});
            {returnType} result = Maths.{functionName}(x);
            {returnType} expected = {expectedVector};
            {string.Join("\n            ", components.Select((c, i) =>
                assertEqualMethod.Replace("{expected}", $"expected.{c}").Replace("{actual}", $"result.{c}")
            ))}
        }}");
            }
        }

        return tests;
    }
}
