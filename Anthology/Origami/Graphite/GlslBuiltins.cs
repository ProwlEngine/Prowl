// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.OrigamiUI.Graphite;

/// <summary>One overload of a GLSL built in function.</summary>
public sealed record GlslBuiltinFunction(string Name, string ReturnType, string Parameters, string Doc, GlslStages Stages = GlslStages.All);

/// <summary>A GLSL built in variable such as gl_Position.</summary>
public sealed record GlslBuiltinVariable(string Name, string Type, string Doc, GlslStages Stages, bool Output = false);

[System.Flags]
public enum GlslStages { Vertex = 1, Fragment = 2, Compute = 4, All = Vertex | Fragment | Compute }

/// <summary>The built in GLSL 4.60 language: keywords, types, functions and variables.</summary>
public static class GlslBuiltins
{
    public static readonly HashSet<string> Keywords;
    public static readonly HashSet<string> ControlKeywords;
    public static readonly HashSet<string> Types;
    public static readonly IReadOnlyList<GlslBuiltinFunction> Functions;
    public static readonly IReadOnlyList<GlslBuiltinVariable> Variables;
    public static readonly IReadOnlyDictionary<string, string> TypeDocs;

    static GlslBuiltins()
    {
        Keywords = new HashSet<string>
        {
            "attribute", "const", "uniform", "varying", "buffer", "shared", "coherent", "volatile", "restrict",
            "readonly", "writeonly", "layout", "centroid", "flat", "smooth", "noperspective", "patch", "sample",
            "in", "out", "inout", "invariant", "precise", "subroutine", "precision", "highp", "mediump", "lowp",
            "struct", "true", "false", "void",
            "common", "partition", "active", "asm", "class", "union", "enum", "typedef", "template", "this",
            "resource", "goto", "inline", "noinline", "public", "static", "extern", "external", "interface",
            "long", "short", "half", "fixed", "unsigned", "superp", "input", "output", "filter", "sizeof",
            "cast", "namespace", "using",
        };

        ControlKeywords = new HashSet<string>
        {
            "if", "else", "switch", "case", "default", "for", "while", "do", "break", "continue", "return", "discard",
        };

        Types = BuildTypes();
        TypeDocs = BuildTypeDocs();
        Functions = BuildFunctions();
        Variables = BuildVariables();
    }

    static HashSet<string> BuildTypes()
    {
        var types = new HashSet<string> { "bool", "int", "uint", "float", "double", "atomic_uint", "sampler", "samplerShadow" };

        for (int n = 2; n <= 4; n++)
        {
            foreach (var prefix in new[] { "", "d", "i", "u", "b" })
                types.Add(prefix + "vec" + n);

            types.Add("mat" + n);
            types.Add("dmat" + n);
            for (int m = 2; m <= 4; m++)
            {
                types.Add("mat" + n + "x" + m);
                types.Add("dmat" + n + "x" + m);
            }
        }

        string[] samplerDims = { "1D", "2D", "3D", "Cube", "2DRect", "1DArray", "2DArray", "CubeArray", "Buffer", "2DMS", "2DMSArray" };
        foreach (var prefix in new[] { "", "i", "u" })
        {
            foreach (var dim in samplerDims)
            {
                types.Add(prefix + "sampler" + dim);
                types.Add(prefix + "image" + dim);
                types.Add(prefix + "texture" + dim);
            }
            types.Add(prefix + "subpassInput");
            types.Add(prefix + "subpassInputMS");
        }

        foreach (var dim in new[] { "1D", "2D", "2DRect", "1DArray", "2DArray", "Cube", "CubeArray" })
            types.Add("sampler" + dim + "Shadow");

        return types;
    }

    static Dictionary<string, string> BuildTypeDocs() => new()
    {
        ["float"] = "A 32 bit IEEE floating point scalar.",
        ["double"] = "A 64 bit IEEE floating point scalar.",
        ["int"] = "A 32 bit signed two's complement integer.",
        ["uint"] = "A 32 bit unsigned integer.",
        ["bool"] = "A boolean value, true or false.",
        ["vec2"] = "A two component single precision floating point vector.",
        ["vec3"] = "A three component single precision floating point vector.",
        ["vec4"] = "A four component single precision floating point vector.",
        ["dvec2"] = "A two component double precision floating point vector.",
        ["dvec3"] = "A three component double precision floating point vector.",
        ["dvec4"] = "A four component double precision floating point vector.",
        ["ivec2"] = "A two component signed integer vector.",
        ["ivec3"] = "A three component signed integer vector.",
        ["ivec4"] = "A four component signed integer vector.",
        ["uvec2"] = "A two component unsigned integer vector.",
        ["uvec3"] = "A three component unsigned integer vector.",
        ["uvec4"] = "A four component unsigned integer vector.",
        ["bvec2"] = "A two component boolean vector.",
        ["bvec3"] = "A three component boolean vector.",
        ["bvec4"] = "A four component boolean vector.",
        ["mat2"] = "A 2 by 2 single precision floating point matrix, stored column major.",
        ["mat3"] = "A 3 by 3 single precision floating point matrix, stored column major.",
        ["mat4"] = "A 4 by 4 single precision floating point matrix, stored column major.",
        ["sampler2D"] = "A combined image and sampler for a 2D floating point texture.",
        ["sampler2DArray"] = "A combined image and sampler for an array of 2D floating point textures.",
        ["sampler3D"] = "A combined image and sampler for a 3D floating point texture.",
        ["samplerCube"] = "A combined image and sampler for a floating point cube map.",
        ["samplerCubeArray"] = "A combined image and sampler for an array of floating point cube maps.",
        ["sampler2DShadow"] = "A combined image and sampler for a 2D depth texture with depth comparison.",
        ["image2D"] = "A handle to a 2D floating point image for load, store and atomic operations.",
        ["texture2D"] = "A Vulkan separate 2D floating point texture, combined with a sampler as sampler2D(tex, samp).",
        ["sampler"] = "A Vulkan separate sampler object, combined with a texture to form a combined image sampler.",
        ["samplerShadow"] = "A Vulkan separate sampler object that performs depth comparison.",
        ["void"] = "No value. Used as a function return type or an empty parameter list.",
    };

    static List<GlslBuiltinFunction> BuildFunctions()
    {
        var list = new List<GlslBuiltinFunction>();

        void F(string name, string ret, string pars, string doc, GlslStages stages = GlslStages.All)
            => list.Add(new GlslBuiltinFunction(name, ret, pars, doc, stages));

        void FD(string name, string pars, string doc)
        {
            F(name, "genType", pars, doc);
            F(name, "genDType", pars.Replace("genType", "genDType").Replace("float ", "double "), doc);
        }

        void FB(string name, string ret, string pars, string doc)
        {
            F(name, ret, pars, doc);
            F(name, ret, pars + ", float bias", doc, GlslStages.Fragment);
        }

        void FC(string name, string ret, string pars, string doc)
        {
            F(name, ret, pars, doc);
            F(name, ret, pars + ", int comp", doc);
        }

        F("radians", "genType", "genType degrees", "Converts degrees to radians, returning (pi / 180) * degrees.");
        F("degrees", "genType", "genType radians", "Converts radians to degrees, returning (180 / pi) * radians.");
        F("sin", "genType", "genType angle", "The standard trigonometric sine of angle in radians.");
        F("cos", "genType", "genType angle", "The standard trigonometric cosine of angle in radians.");
        F("tan", "genType", "genType angle", "The standard trigonometric tangent of angle in radians.");
        F("asin", "genType", "genType x", "Arc sine. Returns an angle whose sine is x, in the range minus pi/2 to pi/2. Undefined if the absolute value of x is greater than 1.");
        F("acos", "genType", "genType x", "Arc cosine. Returns an angle whose cosine is x, in the range 0 to pi. Undefined if the absolute value of x is greater than 1.");
        F("atan", "genType", "genType y, genType x", "Arc tangent of y over x. The signs of x and y determine the quadrant, giving a result in the range minus pi to pi. Undefined if both are 0.");
        F("atan", "genType", "genType y_over_x", "Arc tangent. Returns an angle whose tangent is y_over_x, in the range minus pi/2 to pi/2.");
        F("sinh", "genType", "genType x", "Hyperbolic sine, (exp(x) minus exp(minus x)) / 2.");
        F("cosh", "genType", "genType x", "Hyperbolic cosine, (exp(x) plus exp(minus x)) / 2.");
        F("tanh", "genType", "genType x", "Hyperbolic tangent, sinh(x) / cosh(x).");
        F("asinh", "genType", "genType x", "Arc hyperbolic sine, the inverse of sinh.");
        F("acosh", "genType", "genType x", "Arc hyperbolic cosine, the non negative inverse of cosh. Undefined if x is less than 1.");
        F("atanh", "genType", "genType x", "Arc hyperbolic tangent, the inverse of tanh. Undefined if the absolute value of x is 1 or greater.");

        F("pow", "genType", "genType x, genType y", "Returns x raised to the power y. Undefined if x is less than 0, or if x is 0 and y is 0 or less.");
        F("exp", "genType", "genType x", "Returns the natural exponentiation of x, e raised to the power x.");
        F("log", "genType", "genType x", "Returns the natural logarithm of x. Undefined if x is 0 or less.");
        F("exp2", "genType", "genType x", "Returns 2 raised to the power x.");
        F("log2", "genType", "genType x", "Returns the base 2 logarithm of x. Undefined if x is 0 or less.");
        FD("sqrt", "genType x", "Returns the square root of x. Undefined if x is less than 0.");
        FD("inversesqrt", "genType x", "Returns 1 / sqrt(x). Undefined if x is 0 or less.");

        FD("abs", "genType x", "Returns x if x is 0 or greater, otherwise returns minus x.");
        F("abs", "genIType", "genIType x", "Returns x if x is 0 or greater, otherwise returns minus x.");
        FD("sign", "genType x", "Returns 1.0 if x is greater than 0, 0.0 if x is 0, and minus 1.0 if x is less than 0.");
        F("sign", "genIType", "genIType x", "Returns 1 if x is greater than 0, 0 if x is 0, and minus 1 if x is less than 0.");
        FD("floor", "genType x", "Returns the nearest whole number that is less than or equal to x.");
        FD("trunc", "genType x", "Returns the nearest whole number to x whose absolute value is not larger than the absolute value of x.");
        FD("round", "genType x", "Returns the nearest whole number to x. Whether 0.5 rounds up or down is implementation defined.");
        FD("roundEven", "genType x", "Returns the nearest whole number to x, rounding a fractional part of 0.5 to the nearest even number.");
        FD("ceil", "genType x", "Returns the nearest whole number that is greater than or equal to x.");
        FD("fract", "genType x", "Returns the fractional part of x, x minus floor(x).");
        FD("mod", "genType x, float y", "Modulus. Returns x minus y * floor(x / y).");
        FD("mod", "genType x, genType y", "Modulus. Returns x minus y * floor(x / y).");
        FD("modf", "genType x, out genType i", "Returns the fractional part of x and writes the whole number part to i. Both results have the same sign as x.");

        const string minDoc = "Returns y if y is less than x, otherwise returns x.";
        const string maxDoc = "Returns y if x is less than y, otherwise returns x.";
        foreach (var (gen, scalar) in new[] { ("genType", "float"), ("genDType", "double"), ("genIType", "int"), ("genUType", "uint") })
        {
            F("min", gen, gen + " x, " + gen + " y", minDoc);
            F("min", gen, gen + " x, " + scalar + " y", minDoc);
            F("max", gen, gen + " x, " + gen + " y", maxDoc);
            F("max", gen, gen + " x, " + scalar + " y", maxDoc);
            F("clamp", gen, gen + " x, " + gen + " minVal, " + gen + " maxVal", "Returns min(max(x, minVal), maxVal). Undefined if minVal is greater than maxVal.");
            F("clamp", gen, gen + " x, " + scalar + " minVal, " + scalar + " maxVal", "Returns min(max(x, minVal), maxVal). Undefined if minVal is greater than maxVal.");
        }

        const string mixDoc = "Returns the linear blend of x and y, x * (1 minus a) plus y * a.";
        const string mixSelectDoc = "Selects which vector each returned component comes from. Where a is false the component of x is returned, where a is true the component of y is returned.";
        FD("mix", "genType x, genType y, genType a", mixDoc);
        FD("mix", "genType x, genType y, float a", mixDoc);
        foreach (var gen in new[] { "genType", "genDType", "genIType", "genUType", "genBType" })
            F("mix", gen, gen + " x, " + gen + " y, genBType a", mixSelectDoc);

        FD("step", "genType edge, genType x", "Returns 0.0 if x is less than edge, otherwise returns 1.0.");
        FD("step", "float edge, genType x", "Returns 0.0 if x is less than edge, otherwise returns 1.0.");
        const string smoothDoc = "Returns 0.0 if x is at or below edge0 and 1.0 if x is at or above edge1, with smooth Hermite interpolation in between. Undefined if edge0 is greater than or equal to edge1.";
        FD("smoothstep", "genType edge0, genType edge1, genType x", smoothDoc);
        FD("smoothstep", "float edge0, float edge1, genType x", smoothDoc);

        F("isnan", "genBType", "genType x", "Returns true if x holds a NaN, otherwise returns false.");
        F("isnan", "genBType", "genDType x", "Returns true if x holds a NaN, otherwise returns false.");
        F("isinf", "genBType", "genType x", "Returns true if x holds a positive or negative infinity, otherwise returns false.");
        F("isinf", "genBType", "genDType x", "Returns true if x holds a positive or negative infinity, otherwise returns false.");
        F("floatBitsToInt", "genIType", "genType value", "Returns a signed integer holding the raw bit encoding of the floating point value.");
        F("floatBitsToUint", "genUType", "genType value", "Returns an unsigned integer holding the raw bit encoding of the floating point value.");
        F("intBitsToFloat", "genType", "genIType value", "Returns the floating point value whose bit encoding is the given signed integer.");
        F("uintBitsToFloat", "genType", "genUType value", "Returns the floating point value whose bit encoding is the given unsigned integer.");
        FD("fma", "genType a, genType b, genType c", "Computes a * b plus c, possibly as a single fused operation with one rounding.");
        F("frexp", "genType", "genType x, out genIType exp", "Splits x into a significand in the range 0.5 to 1.0, which is returned, and an integral exponent of two written to exp.");
        F("frexp", "genDType", "genDType x, out genIType exp", "Splits x into a significand in the range 0.5 to 1.0, which is returned, and an integral exponent of two written to exp.");
        F("ldexp", "genType", "genType x, genIType exp", "Builds a floating point number from x and the exponent, returning x * 2 raised to exp.");
        F("ldexp", "genDType", "genDType x, genIType exp", "Builds a floating point number from x and the exponent, returning x * 2 raised to exp.");

        F("packUnorm2x16", "uint", "vec2 v", "Converts each component of v, clamped to 0 to 1, to a 16 bit unsigned normalized integer and packs them into a uint. The first component goes in the least significant bits.");
        F("packSnorm2x16", "uint", "vec2 v", "Converts each component of v, clamped to minus 1 to 1, to a 16 bit signed normalized integer and packs them into a uint. The first component goes in the least significant bits.");
        F("packUnorm4x8", "uint", "vec4 v", "Converts each component of v, clamped to 0 to 1, to an 8 bit unsigned normalized integer and packs them into a uint. The first component goes in the least significant bits.");
        F("packSnorm4x8", "uint", "vec4 v", "Converts each component of v, clamped to minus 1 to 1, to an 8 bit signed normalized integer and packs them into a uint. The first component goes in the least significant bits.");
        F("unpackUnorm2x16", "vec2", "uint p", "Unpacks two 16 bit unsigned normalized integers from p into floating point values in the range 0 to 1.");
        F("unpackSnorm2x16", "vec2", "uint p", "Unpacks two 16 bit signed normalized integers from p into floating point values in the range minus 1 to 1.");
        F("unpackUnorm4x8", "vec4", "uint p", "Unpacks four 8 bit unsigned normalized integers from p into floating point values in the range 0 to 1.");
        F("unpackSnorm4x8", "vec4", "uint p", "Unpacks four 8 bit signed normalized integers from p into floating point values in the range minus 1 to 1.");
        F("packHalf2x16", "uint", "vec2 v", "Converts both components of v to 16 bit half floats and packs them into a uint. The first component goes in the least significant bits.");
        F("unpackHalf2x16", "vec2", "uint v", "Unpacks two 16 bit half floats from v and returns them as 32 bit floats.");
        F("packDouble2x32", "double", "uvec2 v", "Returns a double whose bits are v. The first component holds the least significant 32 bits.");
        F("unpackDouble2x32", "uvec2", "double v", "Returns the bits of the double v as two unsigned integers. The first component holds the least significant 32 bits.");

        F("length", "float", "genType x", "Returns the length of vector x.");
        F("length", "double", "genDType x", "Returns the length of vector x.");
        F("distance", "float", "genType p0, genType p1", "Returns the distance between p0 and p1, length(p0 minus p1).");
        F("distance", "double", "genDType p0, genDType p1", "Returns the distance between p0 and p1, length(p0 minus p1).");
        F("dot", "float", "genType x, genType y", "Returns the dot product of x and y.");
        F("dot", "double", "genDType x, genDType y", "Returns the dot product of x and y.");
        F("cross", "vec3", "vec3 x, vec3 y", "Returns the cross product of x and y.");
        F("cross", "dvec3", "dvec3 x, dvec3 y", "Returns the cross product of x and y.");
        FD("normalize", "genType x", "Returns a vector in the same direction as x but with a length of 1.");
        FD("faceforward", "genType N, genType I, genType Nref", "Returns N if dot(Nref, I) is less than 0, otherwise returns minus N.");
        FD("reflect", "genType I, genType N", "Returns the reflection direction for incident vector I and surface normal N, I minus 2 * dot(N, I) * N. N should be normalized.");
        FD("refract", "genType I, genType N, float eta", "Returns the refraction vector for incident vector I, surface normal N and ratio of indices of refraction eta. Returns zero on total internal reflection. I and N should be normalized.");

        F("matrixCompMult", "mat", "mat x, mat y", "Multiplies matrix x by matrix y component by component.");
        F("matrixCompMult", "dmat", "dmat x, dmat y", "Multiplies matrix x by matrix y component by component.");
        F("outerProduct", "mat", "vec c, vec r", "Treats c as a column vector and r as a row vector and returns their linear algebraic matrix product.");
        F("outerProduct", "dmat", "dvec c, dvec r", "Treats c as a column vector and r as a row vector and returns their linear algebraic matrix product.");
        F("transpose", "mat", "mat m", "Returns the transpose of m.");
        F("transpose", "dmat", "dmat m", "Returns the transpose of m.");
        F("determinant", "float", "mat m", "Returns the determinant of the square matrix m.");
        F("determinant", "double", "dmat m", "Returns the determinant of the square matrix m.");
        F("inverse", "mat", "mat m", "Returns the inverse of the square matrix m. Undefined if m is singular or poorly conditioned.");
        F("inverse", "dmat", "dmat m", "Returns the inverse of the square matrix m. Undefined if m is singular or poorly conditioned.");

        foreach (var (name, op) in new[] { ("lessThan", "x less than y"), ("lessThanEqual", "x less than or equal to y"), ("greaterThan", "x greater than y"), ("greaterThanEqual", "x greater than or equal to y"), ("equal", "x equal to y"), ("notEqual", "x not equal to y") })
        {
            var doc = "Returns the component wise compare of " + op + ".";
            foreach (var vec in new[] { "vec", "dvec", "ivec", "uvec" })
                F(name, "bvec", vec + " x, " + vec + " y", doc);
            if (name is "equal" or "notEqual")
                F(name, "bvec", "bvec x, bvec y", doc);
        }
        F("any", "bool", "bvec x", "Returns true if any component of x is true.");
        F("all", "bool", "bvec x", "Returns true only if all components of x are true.");
        F("not", "bvec", "bvec x", "Returns the component wise logical complement of x.");

        F("uaddCarry", "genUType", "genUType x, genUType y, out genUType carry", "Adds x and y modulo 2 to the 32. Writes 0 to carry if the sum was below 2 to the 32, otherwise 1.");
        F("usubBorrow", "genUType", "genUType x, genUType y, out genUType borrow", "Subtracts y from x, returning the difference if non negative or 2 to the 32 plus the difference otherwise. Writes 0 to borrow if x is at least y, otherwise 1.");
        F("umulExtended", "void", "genUType x, genUType y, out genUType msb, out genUType lsb", "Multiplies 32 bit unsigned integers x and y into a 64 bit result. The most significant 32 bits go to msb and the least significant 32 bits go to lsb.");
        F("imulExtended", "void", "genIType x, genIType y, out genIType msb, out genIType lsb", "Multiplies 32 bit signed integers x and y into a 64 bit result. The most significant 32 bits go to msb and the least significant 32 bits go to lsb.");
        const string extractDoc = "Extracts bits offset through offset plus bits minus 1 from value into the least significant bits of the result. Signed results are sign extended. Returns 0 if bits is 0.";
        F("bitfieldExtract", "genIType", "genIType value, int offset, int bits", extractDoc);
        F("bitfieldExtract", "genUType", "genUType value, int offset, int bits", extractDoc);
        const string insertDoc = "Returns base with bits offset through offset plus bits minus 1 replaced by the least significant bits of insert. Returns base if bits is 0.";
        F("bitfieldInsert", "genIType", "genIType base, genIType insert, int offset, int bits", insertDoc);
        F("bitfieldInsert", "genUType", "genUType base, genUType insert, int offset, int bits", insertDoc);
        F("bitfieldReverse", "genIType", "genIType value", "Reverses the bits of value. Bit n of the result comes from bit 31 minus n of value.");
        F("bitfieldReverse", "genUType", "genUType value", "Reverses the bits of value. Bit n of the result comes from bit 31 minus n of value.");
        F("bitCount", "genIType", "genIType value", "Returns the number of one bits in the binary representation of value.");
        F("bitCount", "genIType", "genUType value", "Returns the number of one bits in the binary representation of value.");
        F("findLSB", "genIType", "genIType value", "Returns the bit number of the least significant one bit in value, or minus 1 if value is 0.");
        F("findLSB", "genIType", "genUType value", "Returns the bit number of the least significant one bit in value, or minus 1 if value is 0.");
        F("findMSB", "genIType", "genIType value", "Returns the bit number of the most significant bit that differs from the sign bit, or minus 1 if value is 0 or minus 1.");
        F("findMSB", "genIType", "genUType value", "Returns the bit number of the most significant one bit in value, or minus 1 if value is 0.");

        AddTextureFunctions(F, FB, FC);

        foreach (var (op, desc) in new[] { ("Increment", "Atomically increments the counter and returns its value before the increment."), ("Decrement", "Atomically decrements the counter and returns its value after the decrement.") })
            F("atomicCounter" + op, "uint", "atomic_uint c", desc);
        F("atomicCounter", "uint", "atomic_uint c", "Returns the current value of the counter.");
        foreach (var (op, desc) in new[] { ("Add", "adds data to"), ("Subtract", "subtracts data from"), ("Min", "sets to the minimum of itself and data"), ("Max", "sets to the maximum of itself and data"), ("And", "bitwise ANDs data into"), ("Or", "bitwise ORs data into"), ("Xor", "bitwise XORs data into"), ("Exchange", "replaces with data") })
            F("atomicCounter" + op, "uint", "atomic_uint c, uint data", "Atomically " + desc + " the counter and returns its value before the operation.");
        F("atomicCounterCompSwap", "uint", "atomic_uint c, uint compare, uint data", "Atomically compares compare with the counter, replacing the counter with data if they are equal. Returns the value before the operation.");

        foreach (var (op, desc) in new[] { ("Add", "Adds data to mem"), ("Min", "Stores the minimum of mem and data in mem"), ("Max", "Stores the maximum of mem and data in mem"), ("And", "Stores the bitwise AND of mem and data in mem"), ("Or", "Stores the bitwise OR of mem and data in mem"), ("Xor", "Stores the bitwise XOR of mem and data in mem"), ("Exchange", "Replaces mem with data") })
        {
            var doc = desc + " atomically and returns the original value of mem. mem must be a buffer or shared variable.";
            F("atomic" + op, "uint", "inout uint mem, uint data", doc);
            F("atomic" + op, "int", "inout int mem, int data", doc);
        }
        const string compSwapDoc = "Atomically compares compare with mem, replacing mem with data if they are equal. Returns the original value of mem.";
        F("atomicCompSwap", "uint", "inout uint mem, uint compare, uint data", compSwapDoc);
        F("atomicCompSwap", "int", "inout int mem, int compare, int data", compSwapDoc);

        AddImageFunctions(F);

        foreach (var (name, desc) in new[]
        {
            ("dFdx", "Returns the partial derivative of p with respect to the window x coordinate, using either local or fine differencing."),
            ("dFdy", "Returns the partial derivative of p with respect to the window y coordinate, using either local or fine differencing."),
            ("dFdxFine", "Returns the partial derivative of p with respect to the window x coordinate, using local differencing based on the current fragment and its immediate neighbor."),
            ("dFdyFine", "Returns the partial derivative of p with respect to the window y coordinate, using local differencing based on the current fragment and its immediate neighbor."),
            ("dFdxCoarse", "Returns the partial derivative of p with respect to the window x coordinate, using coarse differencing that may be shared by neighboring fragments."),
            ("dFdyCoarse", "Returns the partial derivative of p with respect to the window y coordinate, using coarse differencing that may be shared by neighboring fragments."),
            ("fwidth", "Returns abs(dFdx(p)) plus abs(dFdy(p))."),
            ("fwidthFine", "Returns abs(dFdxFine(p)) plus abs(dFdyFine(p))."),
            ("fwidthCoarse", "Returns abs(dFdxCoarse(p)) plus abs(dFdyCoarse(p))."),
        })
            F(name, "genType", "genType p", desc, GlslStages.Fragment);
        F("interpolateAtCentroid", "genType", "genType interpolant", "Returns the value of the input interpolant sampled at a location inside both the pixel and the primitive being processed.", GlslStages.Fragment);
        F("interpolateAtSample", "genType", "genType interpolant, int sample", "Returns the value of the input interpolant at the location of the given sample number. Undefined if the sample does not exist.", GlslStages.Fragment);
        F("interpolateAtOffset", "genType", "genType interpolant, vec2 offset", "Returns the value of the input interpolant sampled at an offset in pixels from the pixel center.", GlslStages.Fragment);

        F("barrier", "void", "", "Waits until every invocation in the workgroup has reached this call, and makes shared memory writes before it visible after it.", GlslStages.Compute);
        F("memoryBarrier", "void", "", "Orders all memory transactions issued by this invocation before the barrier ahead of those issued after it.");
        F("memoryBarrierAtomicCounter", "void", "", "Orders atomic counter accesses issued by this invocation before the barrier ahead of those issued after it.");
        F("memoryBarrierBuffer", "void", "", "Orders buffer variable accesses issued by this invocation before the barrier ahead of those issued after it.");
        F("memoryBarrierShared", "void", "", "Orders shared variable accesses issued by this invocation before the barrier ahead of those issued after it.", GlslStages.Compute);
        F("memoryBarrierImage", "void", "", "Orders image accesses issued by this invocation before the barrier ahead of those issued after it.");
        F("groupMemoryBarrier", "void", "", "Orders all memory transactions issued by this invocation as seen by other invocations in the same workgroup.", GlslStages.Compute);

        F("subpassLoad", "gvec4", "gsubpassInput subpass", "Reads the input attachment value for the current fragment from the subpass input.", GlslStages.Fragment);
        F("subpassLoad", "gvec4", "gsubpassInputMS subpass, int sample", "Reads the given sample of the input attachment value for the current fragment from the multisampled subpass input.", GlslStages.Fragment);

        return list;
    }

    delegate void Adder(string name, string ret, string pars, string doc, GlslStages stages = GlslStages.All);
    delegate void VariantAdder(string name, string ret, string pars, string doc);

    static void AddTextureFunctions(Adder F, VariantAdder FB, VariantAdder FC)
    {
        const string size = "Returns the dimensions of level lod of the texture bound to sampler. Array textures report the layer count in the last component.";
        const string sizeNoLod = "Returns the dimensions of the texture bound to sampler. Array textures report the layer count in the last component.";
        F("textureSize", "int", "gsampler1D sampler, int lod", size);
        F("textureSize", "ivec2", "gsampler2D sampler, int lod", size);
        F("textureSize", "ivec3", "gsampler3D sampler, int lod", size);
        F("textureSize", "ivec2", "gsamplerCube sampler, int lod", size);
        F("textureSize", "int", "sampler1DShadow sampler, int lod", size);
        F("textureSize", "ivec2", "sampler2DShadow sampler, int lod", size);
        F("textureSize", "ivec2", "samplerCubeShadow sampler, int lod", size);
        F("textureSize", "ivec3", "gsamplerCubeArray sampler, int lod", size);
        F("textureSize", "ivec3", "samplerCubeArrayShadow sampler, int lod", size);
        F("textureSize", "ivec2", "gsampler2DRect sampler", sizeNoLod);
        F("textureSize", "ivec2", "sampler2DRectShadow sampler", sizeNoLod);
        F("textureSize", "ivec2", "gsampler1DArray sampler, int lod", size);
        F("textureSize", "ivec2", "sampler1DArrayShadow sampler, int lod", size);
        F("textureSize", "ivec3", "gsampler2DArray sampler, int lod", size);
        F("textureSize", "ivec3", "sampler2DArrayShadow sampler, int lod", size);
        F("textureSize", "int", "gsamplerBuffer sampler", sizeNoLod);
        F("textureSize", "ivec2", "gsampler2DMS sampler", sizeNoLod);
        F("textureSize", "ivec3", "gsampler2DMSArray sampler", sizeNoLod);

        const string queryLod = "Returns the mipmap array that would be accessed in x and the computed level of detail relative to the base level in y.";
        foreach (var (s, p) in new[] { ("gsampler1D", "float"), ("gsampler2D", "vec2"), ("gsampler3D", "vec3"), ("gsamplerCube", "vec3"), ("gsampler1DArray", "float"), ("gsampler2DArray", "vec2"), ("gsamplerCubeArray", "vec3"), ("sampler1DShadow", "float"), ("sampler2DShadow", "vec2"), ("samplerCubeShadow", "vec3"), ("sampler1DArrayShadow", "float"), ("sampler2DArrayShadow", "vec2"), ("samplerCubeArrayShadow", "vec3") })
            F("textureQueryLod", "vec2", s + " sampler, " + p + " P", queryLod, GlslStages.Fragment);

        foreach (var s in new[] { "gsampler1D", "gsampler2D", "gsampler3D", "gsamplerCube", "gsampler1DArray", "gsampler2DArray", "gsamplerCubeArray", "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow", "sampler1DArrayShadow", "sampler2DArrayShadow", "samplerCubeArrayShadow" })
            F("textureQueryLevels", "int", s + " sampler", "Returns the number of mipmap levels accessible in the texture bound to sampler, or 0 if no texture is bound.");

        F("textureSamples", "int", "gsampler2DMS sampler", "Returns the number of samples of the multisample texture bound to sampler.");
        F("textureSamples", "int", "gsampler2DMSArray sampler", "Returns the number of samples of the multisample texture bound to sampler.");

        const string tex = "Samples the texture bound to sampler at coordinate P. For shadow forms the last coordinate component is the depth reference and the comparison result is returned. bias is added to the implicit level of detail.";
        FB("texture", "gvec4", "gsampler1D sampler, float P", tex);
        FB("texture", "gvec4", "gsampler2D sampler, vec2 P", tex);
        FB("texture", "gvec4", "gsampler3D sampler, vec3 P", tex);
        FB("texture", "gvec4", "gsamplerCube sampler, vec3 P", tex);
        FB("texture", "float", "sampler1DShadow sampler, vec3 P", tex);
        FB("texture", "float", "sampler2DShadow sampler, vec3 P", tex);
        FB("texture", "float", "samplerCubeShadow sampler, vec4 P", tex);
        FB("texture", "gvec4", "gsampler1DArray sampler, vec2 P", tex);
        FB("texture", "gvec4", "gsampler2DArray sampler, vec3 P", tex);
        FB("texture", "gvec4", "gsamplerCubeArray sampler, vec4 P", tex);
        FB("texture", "float", "sampler1DArrayShadow sampler, vec3 P", tex);
        F("texture", "float", "sampler2DArrayShadow sampler, vec4 P", tex);
        F("texture", "gvec4", "gsampler2DRect sampler, vec2 P", tex);
        F("texture", "float", "sampler2DRectShadow sampler, vec3 P", tex);
        F("texture", "float", "samplerCubeArrayShadow sampler, vec4 P, float compare", tex);

        const string proj = "Samples the texture with projection. The coordinates of P, excluding the last, are divided by the last component of P before lookup.";
        FB("textureProj", "gvec4", "gsampler1D sampler, vec2 P", proj);
        FB("textureProj", "gvec4", "gsampler1D sampler, vec4 P", proj);
        FB("textureProj", "gvec4", "gsampler2D sampler, vec3 P", proj);
        FB("textureProj", "gvec4", "gsampler2D sampler, vec4 P", proj);
        FB("textureProj", "gvec4", "gsampler3D sampler, vec4 P", proj);
        FB("textureProj", "float", "sampler1DShadow sampler, vec4 P", proj);
        FB("textureProj", "float", "sampler2DShadow sampler, vec4 P", proj);
        F("textureProj", "gvec4", "gsampler2DRect sampler, vec3 P", proj);
        F("textureProj", "gvec4", "gsampler2DRect sampler, vec4 P", proj);
        F("textureProj", "float", "sampler2DRectShadow sampler, vec4 P", proj);

        const string lod = "Samples the texture at coordinate P with an explicit level of detail lod.";
        F("textureLod", "gvec4", "gsampler1D sampler, float P, float lod", lod);
        F("textureLod", "gvec4", "gsampler2D sampler, vec2 P, float lod", lod);
        F("textureLod", "gvec4", "gsampler3D sampler, vec3 P, float lod", lod);
        F("textureLod", "gvec4", "gsamplerCube sampler, vec3 P, float lod", lod);
        F("textureLod", "float", "sampler2DShadow sampler, vec3 P, float lod", lod);
        F("textureLod", "float", "sampler1DShadow sampler, vec3 P, float lod", lod);
        F("textureLod", "gvec4", "gsampler1DArray sampler, vec2 P, float lod", lod);
        F("textureLod", "float", "sampler1DArrayShadow sampler, vec3 P, float lod", lod);
        F("textureLod", "gvec4", "gsampler2DArray sampler, vec3 P, float lod", lod);
        F("textureLod", "gvec4", "gsamplerCubeArray sampler, vec4 P, float lod", lod);

        const string offset = "Samples the texture at coordinate P with a constant texel offset added to the texel coordinates before lookup.";
        FB("textureOffset", "gvec4", "gsampler1D sampler, float P, int offset", offset);
        FB("textureOffset", "gvec4", "gsampler2D sampler, vec2 P, ivec2 offset", offset);
        FB("textureOffset", "gvec4", "gsampler3D sampler, vec3 P, ivec3 offset", offset);
        FB("textureOffset", "float", "sampler2DShadow sampler, vec3 P, ivec2 offset", offset);
        FB("textureOffset", "float", "sampler1DShadow sampler, vec3 P, int offset", offset);
        FB("textureOffset", "gvec4", "gsampler1DArray sampler, vec2 P, int offset", offset);
        FB("textureOffset", "gvec4", "gsampler2DArray sampler, vec3 P, ivec2 offset", offset);
        FB("textureOffset", "float", "sampler1DArrayShadow sampler, vec3 P, int offset", offset);
        F("textureOffset", "float", "sampler2DArrayShadow sampler, vec4 P, ivec2 offset", offset);
        F("textureOffset", "gvec4", "gsampler2DRect sampler, vec2 P, ivec2 offset", offset);
        F("textureOffset", "float", "sampler2DRectShadow sampler, vec3 P, ivec2 offset", offset);

        const string fetch = "Reads a single texel at integer coordinate P from level lod or sample number sample, without filtering.";
        F("texelFetch", "gvec4", "gsampler1D sampler, int P, int lod", fetch);
        F("texelFetch", "gvec4", "gsampler2D sampler, ivec2 P, int lod", fetch);
        F("texelFetch", "gvec4", "gsampler3D sampler, ivec3 P, int lod", fetch);
        F("texelFetch", "gvec4", "gsampler2DRect sampler, ivec2 P", fetch);
        F("texelFetch", "gvec4", "gsampler1DArray sampler, ivec2 P, int lod", fetch);
        F("texelFetch", "gvec4", "gsampler2DArray sampler, ivec3 P, int lod", fetch);
        F("texelFetch", "gvec4", "gsamplerBuffer sampler, int P", fetch);
        F("texelFetch", "gvec4", "gsampler2DMS sampler, ivec2 P, int sample", fetch);
        F("texelFetch", "gvec4", "gsampler2DMSArray sampler, ivec3 P, int sample", fetch);

        const string fetchOffset = "Reads a single texel like texelFetch with a constant offset added to the texel coordinate.";
        F("texelFetchOffset", "gvec4", "gsampler1D sampler, int P, int lod, int offset", fetchOffset);
        F("texelFetchOffset", "gvec4", "gsampler2D sampler, ivec2 P, int lod, ivec2 offset", fetchOffset);
        F("texelFetchOffset", "gvec4", "gsampler3D sampler, ivec3 P, int lod, ivec3 offset", fetchOffset);
        F("texelFetchOffset", "gvec4", "gsampler2DRect sampler, ivec2 P, ivec2 offset", fetchOffset);
        F("texelFetchOffset", "gvec4", "gsampler1DArray sampler, ivec2 P, int lod, int offset", fetchOffset);
        F("texelFetchOffset", "gvec4", "gsampler2DArray sampler, ivec3 P, int lod, ivec2 offset", fetchOffset);

        const string projOffset = "Samples the texture with projection like textureProj, with a constant texel offset added before lookup.";
        FB("textureProjOffset", "gvec4", "gsampler1D sampler, vec2 P, int offset", projOffset);
        FB("textureProjOffset", "gvec4", "gsampler1D sampler, vec4 P, int offset", projOffset);
        FB("textureProjOffset", "gvec4", "gsampler2D sampler, vec3 P, ivec2 offset", projOffset);
        FB("textureProjOffset", "gvec4", "gsampler2D sampler, vec4 P, ivec2 offset", projOffset);
        FB("textureProjOffset", "gvec4", "gsampler3D sampler, vec4 P, ivec3 offset", projOffset);
        FB("textureProjOffset", "float", "sampler1DShadow sampler, vec4 P, int offset", projOffset);
        FB("textureProjOffset", "float", "sampler2DShadow sampler, vec4 P, ivec2 offset", projOffset);
        F("textureProjOffset", "gvec4", "gsampler2DRect sampler, vec3 P, ivec2 offset", projOffset);
        F("textureProjOffset", "gvec4", "gsampler2DRect sampler, vec4 P, ivec2 offset", projOffset);
        F("textureProjOffset", "float", "sampler2DRectShadow sampler, vec4 P, ivec2 offset", projOffset);

        const string lodOffset = "Samples the texture with an explicit level of detail like textureLod, with a constant texel offset added before lookup.";
        F("textureLodOffset", "gvec4", "gsampler1D sampler, float P, float lod, int offset", lodOffset);
        F("textureLodOffset", "gvec4", "gsampler2D sampler, vec2 P, float lod, ivec2 offset", lodOffset);
        F("textureLodOffset", "gvec4", "gsampler3D sampler, vec3 P, float lod, ivec3 offset", lodOffset);
        F("textureLodOffset", "float", "sampler1DShadow sampler, vec3 P, float lod, int offset", lodOffset);
        F("textureLodOffset", "float", "sampler2DShadow sampler, vec3 P, float lod, ivec2 offset", lodOffset);
        F("textureLodOffset", "gvec4", "gsampler1DArray sampler, vec2 P, float lod, int offset", lodOffset);
        F("textureLodOffset", "gvec4", "gsampler2DArray sampler, vec3 P, float lod, ivec2 offset", lodOffset);
        F("textureLodOffset", "float", "sampler1DArrayShadow sampler, vec3 P, float lod, int offset", lodOffset);

        const string projLod = "Samples the texture with projection like textureProj and an explicit level of detail like textureLod.";
        const string projLodOffset = "Samples the texture with projection and an explicit level of detail, with a constant texel offset added before lookup.";
        foreach (var (ret, s, p, o) in new[] { ("gvec4", "gsampler1D", "vec2", "int"), ("gvec4", "gsampler1D", "vec4", "int"), ("gvec4", "gsampler2D", "vec3", "ivec2"), ("gvec4", "gsampler2D", "vec4", "ivec2"), ("gvec4", "gsampler3D", "vec4", "ivec3"), ("float", "sampler1DShadow", "vec4", "int"), ("float", "sampler2DShadow", "vec4", "ivec2") })
        {
            F("textureProjLod", ret, s + " sampler, " + p + " P, float lod", projLod);
            F("textureProjLodOffset", ret, s + " sampler, " + p + " P, float lod, " + o + " offset", projLodOffset);
        }

        const string grad = "Samples the texture with explicit gradients dPdx and dPdy used to compute the level of detail.";
        const string gradOffset = "Samples the texture with explicit gradients like textureGrad, with a constant texel offset added before lookup.";
        foreach (var (ret, s, p, d, o) in new[]
        {
            ("gvec4", "gsampler1D", "float", "float", "int"),
            ("gvec4", "gsampler2D", "vec2", "vec2", "ivec2"),
            ("gvec4", "gsampler3D", "vec3", "vec3", "ivec3"),
            ("gvec4", "gsamplerCube", "vec3", "vec3", null),
            ("gvec4", "gsampler2DRect", "vec2", "vec2", "ivec2"),
            ("float", "sampler2DRectShadow", "vec3", "vec2", "ivec2"),
            ("float", "sampler1DShadow", "vec3", "float", "int"),
            ("gvec4", "gsampler1DArray", "vec2", "float", "int"),
            ("gvec4", "gsampler2DArray", "vec3", "vec2", "ivec2"),
            ("float", "sampler1DArrayShadow", "vec3", "float", "int"),
            ("float", "sampler2DShadow", "vec3", "vec2", "ivec2"),
            ("float", "samplerCubeShadow", "vec4", "vec3", null),
            ("float", "sampler2DArrayShadow", "vec4", "vec2", "ivec2"),
            ("gvec4", "gsamplerCubeArray", "vec4", "vec3", null),
        })
        {
            var pars = s + " sampler, " + p + " P, " + d + " dPdx, " + d + " dPdy";
            F("textureGrad", ret, pars, grad);
            if (o != null)
                F("textureGradOffset", ret, pars + ", " + o + " offset", gradOffset);
        }

        const string projGrad = "Samples the texture with projection like textureProj and explicit gradients like textureGrad.";
        const string projGradOffset = "Samples the texture with projection and explicit gradients, with a constant texel offset added before lookup.";
        foreach (var (ret, s, p, d, o) in new[]
        {
            ("gvec4", "gsampler1D", "vec2", "float", "int"),
            ("gvec4", "gsampler1D", "vec4", "float", "int"),
            ("gvec4", "gsampler2D", "vec3", "vec2", "ivec2"),
            ("gvec4", "gsampler2D", "vec4", "vec2", "ivec2"),
            ("gvec4", "gsampler3D", "vec4", "vec3", "ivec3"),
            ("gvec4", "gsampler2DRect", "vec3", "vec2", "ivec2"),
            ("gvec4", "gsampler2DRect", "vec4", "vec2", "ivec2"),
            ("float", "sampler2DRectShadow", "vec4", "vec2", "ivec2"),
            ("float", "sampler1DShadow", "vec4", "float", "int"),
            ("float", "sampler2DShadow", "vec4", "vec2", "ivec2"),
        })
        {
            var pars = s + " sampler, " + p + " P, " + d + " dPdx, " + d + " dPdy";
            F("textureProjGrad", ret, pars, projGrad);
            F("textureProjGradOffset", ret, pars + ", " + o + " offset", projGradOffset);
        }

        const string gather = "Gathers the four texels that would be used for bilinear filtering and returns component comp of each, where comp defaults to 0 (x). Shadow forms return the comparison results against refZ.";
        FC("textureGather", "gvec4", "gsampler2D sampler, vec2 P", gather);
        FC("textureGather", "gvec4", "gsampler2DArray sampler, vec3 P", gather);
        FC("textureGather", "gvec4", "gsamplerCube sampler, vec3 P", gather);
        FC("textureGather", "gvec4", "gsamplerCubeArray sampler, vec4 P", gather);
        FC("textureGather", "gvec4", "gsampler2DRect sampler, vec2 P", gather);
        F("textureGather", "vec4", "sampler2DShadow sampler, vec2 P, float refZ", gather);
        F("textureGather", "vec4", "sampler2DArrayShadow sampler, vec3 P, float refZ", gather);
        F("textureGather", "vec4", "samplerCubeShadow sampler, vec3 P, float refZ", gather);
        F("textureGather", "vec4", "samplerCubeArrayShadow sampler, vec4 P, float refZ", gather);
        F("textureGather", "vec4", "sampler2DRectShadow sampler, vec2 P, float refZ", gather);

        const string gatherOffset = "Gathers four texels like textureGather, with a texel offset added to the coordinates before lookup.";
        const string gatherOffsets = "Gathers four texels like textureGather, using a separate texel offset from offsets for each of the four texels.";
        foreach (var (name, o, doc) in new[] { ("textureGatherOffset", "ivec2 offset", gatherOffset), ("textureGatherOffsets", "ivec2 offsets[4]", gatherOffsets) })
        {
            FC(name, "gvec4", "gsampler2D sampler, vec2 P, " + o, doc);
            FC(name, "gvec4", "gsampler2DArray sampler, vec3 P, " + o, doc);
            FC(name, "gvec4", "gsampler2DRect sampler, vec2 P, " + o, doc);
            F(name, "vec4", "sampler2DShadow sampler, vec2 P, float refZ, " + o, doc);
            F(name, "vec4", "sampler2DArrayShadow sampler, vec3 P, float refZ, " + o, doc);
            F(name, "vec4", "sampler2DRectShadow sampler, vec2 P, float refZ, " + o, doc);
        }
    }

    static void AddImageFunctions(Adder F)
    {
        const string size = "Returns the dimensions of the image. Array images report the layer count in the last component.";
        foreach (var (ret, img) in new[] { ("int", "gimage1D"), ("ivec2", "gimage2D"), ("ivec3", "gimage3D"), ("ivec2", "gimageCube"), ("ivec3", "gimageCubeArray"), ("ivec2", "gimage2DRect"), ("ivec2", "gimage1DArray"), ("ivec3", "gimage2DArray"), ("int", "gimageBuffer"), ("ivec2", "gimage2DMS"), ("ivec3", "gimage2DMSArray") })
            F("imageSize", ret, img + " image", size);
        F("imageSamples", "int", "gimage2DMS image", "Returns the number of samples of the multisample image.");
        F("imageSamples", "int", "gimage2DMSArray image", "Returns the number of samples of the multisample image.");

        string[] imageParams =
        {
            "gimage1D image, int P", "gimage2D image, ivec2 P", "gimage3D image, ivec3 P", "gimage2DRect image, ivec2 P",
            "gimageCube image, ivec3 P", "gimageBuffer image, int P", "gimage1DArray image, ivec2 P", "gimage2DArray image, ivec3 P",
            "gimageCubeArray image, ivec3 P", "gimage2DMS image, ivec2 P, int sample", "gimage2DMSArray image, ivec3 P, int sample",
        };

        (string Op, string Desc)[] atomics =
        {
            ("Add", "Adds data to the texel"), ("Min", "Stores the minimum of the texel and data"), ("Max", "Stores the maximum of the texel and data"),
            ("And", "Stores the bitwise AND of the texel and data"), ("Or", "Stores the bitwise OR of the texel and data"),
            ("Xor", "Stores the bitwise XOR of the texel and data"), ("Exchange", "Replaces the texel with data"),
        };

        const string compSwap = "Atomically compares compare with the texel at P, replacing it with data if they are equal. Returns the original texel value.";
        foreach (var p in imageParams)
        {
            F("imageLoad", "gvec4", p, "Loads the texel at coordinate P from the image.");
            F("imageStore", "void", p + ", gvec4 data", "Stores data into the texel at coordinate P of the image.");
            foreach (var (op, desc) in atomics)
            {
                var doc = desc + " at P atomically and returns the original texel value.";
                F("imageAtomic" + op, "uint", p + ", uint data", doc);
                F("imageAtomic" + op, "int", p + ", int data", doc);
            }
            F("imageAtomicExchange", "float", p + ", float data", "Replaces the texel with data at P atomically and returns the original texel value.");
            F("imageAtomicCompSwap", "uint", p + ", uint compare, uint data", compSwap);
            F("imageAtomicCompSwap", "int", p + ", int compare, int data", compSwap);
        }
    }

    static List<GlslBuiltinVariable> BuildVariables()
    {
        const GlslStages V = GlslStages.Vertex, Fr = GlslStages.Fragment, C = GlslStages.Compute;
        return new List<GlslBuiltinVariable>
        {
            new("gl_Position", "vec4", "The clip space position of the current vertex.", V, true),
            new("gl_PointSize", "float", "The size in pixels of the point to be rasterized.", V, true),
            new("gl_ClipDistance", "float[]", "Clip distances for user clip planes. A vertex is inside a plane when its distance is 0 or greater.", V, true),
            new("gl_CullDistance", "float[]", "Cull distances. A primitive is culled when all its vertices have a negative distance for the same plane.", V, true),
            new("gl_VertexIndex", "int", "The index of the current vertex, including the base vertex offset.", V),
            new("gl_InstanceIndex", "int", "The index of the current instance, including the base instance offset.", V),
            new("gl_DrawID", "int", "The index of the current draw within a multi draw indirect call.", V),
            new("gl_BaseVertex", "int", "The base vertex value passed to the current draw call.", V),
            new("gl_BaseInstance", "int", "The base instance value passed to the current draw call.", V),

            new("gl_FragCoord", "vec4", "The window relative coordinates of the fragment. xy is the pixel center, z is the depth and w is 1 / clip w.", Fr),
            new("gl_FrontFacing", "bool", "True if the fragment belongs to a front facing primitive.", Fr),
            new("gl_PointCoord", "vec2", "The coordinate of the fragment within a point primitive, from 0 to 1.", Fr),
            new("gl_SampleID", "int", "The sample number of the sample currently being processed.", Fr),
            new("gl_SamplePosition", "vec2", "The position of the current sample within the pixel, from 0 to 1.", Fr),
            new("gl_SampleMaskIn", "int[]", "The coverage mask of the samples covered by the primitive for the current fragment.", Fr),
            new("gl_PrimitiveID", "int", "The index of the current primitive within the draw.", Fr),
            new("gl_Layer", "int", "The layer of the framebuffer the fragment is being rendered to.", Fr),
            new("gl_ViewportIndex", "int", "The index of the viewport the fragment is being rendered to.", Fr),
            new("gl_HelperInvocation", "bool", "True if the invocation is a helper invocation that only exists to compute derivatives.", Fr),
            new("gl_FragDepth", "float", "The depth value written for the fragment. Defaults to gl_FragCoord.z when not written.", Fr, true),
            new("gl_SampleMask", "int[]", "The sample coverage mask written for the fragment.", Fr, true),

            new("gl_NumWorkGroups", "uvec3", "The number of workgroups in each dimension of the dispatch.", C),
            new("gl_WorkGroupSize", "uvec3", "The local workgroup size declared with layout local_size_x, local_size_y and local_size_z.", C),
            new("gl_WorkGroupID", "uvec3", "The index of the current workgroup within the dispatch.", C),
            new("gl_LocalInvocationID", "uvec3", "The index of the current invocation within its workgroup.", C),
            new("gl_GlobalInvocationID", "uvec3", "The index of the current invocation within the whole dispatch, gl_WorkGroupID * gl_WorkGroupSize plus gl_LocalInvocationID.", C),
            new("gl_LocalInvocationIndex", "uint", "The flattened one dimensional index of the current invocation within its workgroup.", C),

            new("gl_ViewIndex", "int", "The index of the view being rendered when multiview is enabled (GL_EXT_multiview).", GlslStages.All),
        };
    }
}
