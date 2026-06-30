namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Tagged value carried by an <see cref="FbxNode"/> as one of its properties. Mirrors the binary
/// FBX property-type byte:
/// <code>
///   Y = i16, C = bool, I = i32, F = f32, D = f64, L = i64,
///   R = byte[], S = string,
///   f/d/l/i/c = arrays (possibly deflate-encoded - decoded at read time),
///   b = bool array.
/// </code>
/// </summary>
internal sealed class FbxProperty
{
    public FbxPropertyType Type { get; init; }

    // Backing storage: only one field is meaningful per Type, but storing all of them keeps the
    // type definition flat. C# doesn't have C-style unions but the cost is one extra ~16-byte
    // payload per property which is negligible vs. the rest of the FBX scene.
    public long IntegerValue;          // Y, C, I, L
    public double DoubleValue;          // F, D
    public string? StringValue;         // S
    public byte[]? BlobValue;           // R, b (raw byte arrays)
    public int[]? IntArrayValue;        // i
    public long[]? LongArrayValue;      // l
    public float[]? FloatArrayValue;    // f
    public double[]? DoubleArrayValue;  // d

    public bool AsBool() => Type switch
    {
        FbxPropertyType.Bool => IntegerValue != 0,
        FbxPropertyType.Int16 or FbxPropertyType.Int32 or FbxPropertyType.Int64 => IntegerValue != 0,
        _ => throw new InvalidCastException($"FBX property of type {Type} is not bool-convertible."),
    };

    public int AsInt() => Type switch
    {
        FbxPropertyType.Int16 or FbxPropertyType.Int32 or FbxPropertyType.Int64 or FbxPropertyType.Bool
            => checked((int)IntegerValue),
        FbxPropertyType.Float or FbxPropertyType.Double => (int)DoubleValue,
        _ => throw new InvalidCastException($"FBX property of type {Type} is not int-convertible."),
    };

    public long AsLong() => Type switch
    {
        FbxPropertyType.Int16 or FbxPropertyType.Int32 or FbxPropertyType.Int64 or FbxPropertyType.Bool
            => IntegerValue,
        FbxPropertyType.Float or FbxPropertyType.Double => (long)DoubleValue,
        _ => throw new InvalidCastException($"FBX property of type {Type} is not long-convertible."),
    };

    public double AsDouble() => Type switch
    {
        FbxPropertyType.Float or FbxPropertyType.Double => DoubleValue,
        FbxPropertyType.Int16 or FbxPropertyType.Int32 or FbxPropertyType.Int64 or FbxPropertyType.Bool
            => IntegerValue,
        _ => throw new InvalidCastException($"FBX property of type {Type} is not double-convertible."),
    };

    public float AsFloat() => (float)AsDouble();

    public string AsString() => Type switch
    {
        FbxPropertyType.String => StringValue ?? string.Empty,
        FbxPropertyType.Raw => System.Text.Encoding.UTF8.GetString(BlobValue ?? Array.Empty<byte>()),
        _ => throw new InvalidCastException($"FBX property of type {Type} is not string-convertible."),
    };

    /// <summary>
    /// Returns the property as a flat float array. Handles every numeric scalar/array type.
    /// </summary>
    public float[] AsFloatArray()
    {
        if (FloatArrayValue is { } f) return f;
        if (DoubleArrayValue is { } d)
        {
            var r = new float[d.Length];
            for (int k = 0; k < d.Length; k++) r[k] = (float)d[k];
            return r;
        }
        if (IntArrayValue is { } iarr)
        {
            var r = new float[iarr.Length];
            for (int k = 0; k < iarr.Length; k++) r[k] = iarr[k];
            return r;
        }
        if (LongArrayValue is { } l)
        {
            var r = new float[l.Length];
            for (int k = 0; k < l.Length; k++) r[k] = l[k];
            return r;
        }
        if (Type == FbxPropertyType.Float || Type == FbxPropertyType.Double)
            return new[] { (float)DoubleValue };
        throw new InvalidCastException($"FBX property of type {Type} is not float-array-convertible.");
    }

    /// <summary>
    /// Returns the property as a flat double array. Handles every numeric scalar/array type.
    /// </summary>
    public double[] AsDoubleArray()
    {
        if (DoubleArrayValue is { } d) return d;
        if (FloatArrayValue is { } f)
        {
            var r = new double[f.Length];
            for (int k = 0; k < f.Length; k++) r[k] = f[k];
            return r;
        }
        if (IntArrayValue is { } iarr)
        {
            var r = new double[iarr.Length];
            for (int k = 0; k < iarr.Length; k++) r[k] = iarr[k];
            return r;
        }
        if (LongArrayValue is { } l)
        {
            var r = new double[l.Length];
            for (int k = 0; k < l.Length; k++) r[k] = l[k];
            return r;
        }
        if (Type == FbxPropertyType.Float || Type == FbxPropertyType.Double)
            return new[] { DoubleValue };
        throw new InvalidCastException($"FBX property of type {Type} is not double-array-convertible.");
    }

    /// <summary>
    /// Returns the property as a flat int array. Handles every numeric scalar/array type.
    /// </summary>
    public int[] AsIntArray()
    {
        if (IntArrayValue is { } i) return i;
        if (LongArrayValue is { } l)
        {
            var r = new int[l.Length];
            for (int j = 0; j < l.Length; j++) r[j] = checked((int)l[j]);
            return r;
        }
        if (FloatArrayValue is { } f)
        {
            var r = new int[f.Length];
            for (int j = 0; j < f.Length; j++) r[j] = (int)f[j];
            return r;
        }
        if (DoubleArrayValue is { } d)
        {
            var r = new int[d.Length];
            for (int j = 0; j < d.Length; j++) r[j] = (int)d[j];
            return r;
        }
        if (Type == FbxPropertyType.Int16 || Type == FbxPropertyType.Int32 || Type == FbxPropertyType.Int64)
            return new[] { checked((int)IntegerValue) };
        throw new InvalidCastException($"FBX property of type {Type} is not int-array-convertible.");
    }

    // Factory helpers used by the ASCII reader (binary path constructs FbxProperty inline).
    public static FbxProperty FromInt(int v) => new() { Type = FbxPropertyType.Int32, IntegerValue = v };
    public static FbxProperty FromLong(long v) => new() { Type = FbxPropertyType.Int64, IntegerValue = v };
    public static FbxProperty FromDouble(double v) => new() { Type = FbxPropertyType.Double, DoubleValue = v };
    public static FbxProperty FromString(string v) => new() { Type = FbxPropertyType.String, StringValue = v };
    public static FbxProperty FromIntArray(int[] v) => new() { Type = FbxPropertyType.ArrayInt32, IntArrayValue = v };
    public static FbxProperty FromDoubleArray(double[] v) => new() { Type = FbxPropertyType.ArrayDouble, DoubleArrayValue = v };
}

internal enum FbxPropertyType : byte
{
    Bool = (byte)'C',
    Int16 = (byte)'Y',
    Int32 = (byte)'I',
    Int64 = (byte)'L',
    Float = (byte)'F',
    Double = (byte)'D',
    Raw = (byte)'R',
    String = (byte)'S',
    ArrayInt32 = (byte)'i',
    ArrayInt64 = (byte)'l',
    ArrayFloat = (byte)'f',
    ArrayDouble = (byte)'d',
    ArrayByte = (byte)'c',
    ArrayBool = (byte)'b',
}

/// <summary>
/// One node in the FBX file's recursive structure: a name, an ordered list of properties, and
/// optional children. Both the binary and ASCII readers produce this same shape.
/// </summary>
internal sealed class FbxNode
{
    public required string Name { get; init; }
    public List<FbxProperty> Properties { get; } = new();
    public List<FbxNode> Children { get; } = new();

    /// <summary>Finds the first child with the given name, or <c>null</c>.</summary>
    public FbxNode? FindChild(string name)
    {
        for (int i = 0; i < Children.Count; i++)
            if (Children[i].Name == name)
                return Children[i];
        return null;
    }

    /// <summary>Enumerates every child whose name matches (used for repeated-key sections like Object).</summary>
    public IEnumerable<FbxNode> FindChildren(string name)
    {
        for (int i = 0; i < Children.Count; i++)
            if (Children[i].Name == name)
                yield return Children[i];
    }

    /// <summary>Returns the i-th property cast to int, or <paramref name="fallback"/> if out of range.</summary>
    public int IntAt(int index, int fallback = 0) =>
        index < Properties.Count ? Properties[index].AsInt() : fallback;

    /// <summary>Returns the i-th property cast to long, or <paramref name="fallback"/> if out of range.</summary>
    public long LongAt(int index, long fallback = 0) =>
        index < Properties.Count ? Properties[index].AsLong() : fallback;

    /// <summary>Returns the i-th property as a string, or <paramref name="fallback"/> if out of range.</summary>
    public string StringAt(int index, string fallback = "") =>
        index < Properties.Count ? Properties[index].AsString() : fallback;

    /// <summary>Returns the i-th property as a double, or <paramref name="fallback"/> if out of range.</summary>
    public double DoubleAt(int index, double fallback = 0d) =>
        index < Properties.Count ? Properties[index].AsDouble() : fallback;
}
