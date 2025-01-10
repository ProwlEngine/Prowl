// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Vector;

/// <summary>
/// A memory-efficient structure for storing three boolean values.
/// Uses a single byte to store all three values, similar to how Vector3Int stores three integers.
/// </summary>
[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct Bool3 : IEquatable<Bool3>
{
    private byte _data; // Uses only 3 bits, one for each boolean

    // Bit masks for each component
    private const byte X_MASK = 1;     // 0000_0001
    private const byte Y_MASK = 1 << 1; // 0000_0010
    private const byte Z_MASK = 1 << 2; // 0000_0100

    // Static readonly instances for common values
    public static readonly Bool3 zero = new(false, false, false);
    public static readonly Bool3 one = new(true, true, true);

    /// <summary>
    /// The X (first) component of the Bool3.
    /// </summary>
    public bool x
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_data & X_MASK) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _data = value ? (byte)(_data | X_MASK) : (byte)(_data & ~X_MASK);
    }

    /// <summary>
    /// The Y (second) component of the Bool3.
    /// </summary>
    public bool y
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_data & Y_MASK) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _data = value ? (byte)(_data | Y_MASK) : (byte)(_data & ~Y_MASK);
    }

    /// <summary>
    /// The Z (third) component of the Bool3.
    /// </summary>
    public bool z
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_data & Z_MASK) != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _data = value ? (byte)(_data | Z_MASK) : (byte)(_data & ~Z_MASK);
    }

    /// <summary>
    /// Creates a new Bool3 with the specified component values.
    /// </summary>
    public Bool3(bool x, bool y, bool z)
    {
        _data = 0;
        this.x = x;
        this.y = y;
        this.z = z;
    }

    /// <summary>
    /// Creates a new Bool3 with all components set to the same value.
    /// </summary>
    public Bool3(bool value) : this(value, value, value) { }

    /// <summary>
    /// Indexer to get or set components by index (0 = x, 1 = y, 2 = z).
    /// </summary>
    public bool this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => index switch
        {
            0 => x,
            1 => y,
            2 => z,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Index must be 0, 1, or 2!")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            switch (index)
            {
                case 0: x = value; break;
                case 1: y = value; break;
                case 2: z = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index), "Index must be 0, 1, or 2!");
            }
        }
    }

    /// <summary>
    /// Returns true if any component is true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Any() => _data != 0;

    /// <summary>
    /// Returns true if all components are true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool All() => (_data & (X_MASK | Y_MASK | Z_MASK)) == (X_MASK | Y_MASK | Z_MASK);

    /// <summary>
    /// Returns how many components are true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CountTrue()
    {
        // Count set bits using Brian Kernighan's algorithm
        int count = 0;
        byte n = (byte)(_data & (X_MASK | Y_MASK | Z_MASK));
        while (n != 0)
        {
            n &= (byte)(n - 1);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Sets all components to the specified value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAll(bool value)
    {
        _data = value ? (byte)(X_MASK | Y_MASK | Z_MASK) : (byte)0;
    }

    /// <summary>
    /// Returns the Bool3 as an array of three booleans.
    /// </summary>
    public bool[] ToArray() => new[] { x, y, z };

    #region Equality and Comparison

    public bool Equals(Bool3 other) => _data == other._data;

    public override bool Equals(object? obj) => obj is Bool3 other && Equals(other);

    public override int GetHashCode() => _data.GetHashCode();

    public static bool operator ==(Bool3 left, Bool3 right) => left.Equals(right);

    public static bool operator !=(Bool3 left, Bool3 right) => !left.Equals(right);

    #endregion

    #region Operators

    public static Bool3 operator &(Bool3 left, Bool3 right) => new Bool3(
        left.x && right.x,
        left.y && right.y,
        left.z && right.z
    );

    public static Bool3 operator |(Bool3 left, Bool3 right) => new Bool3(
        left.x || right.x,
        left.y || right.y,
        left.z || right.z
    );

    public static Bool3 operator ^(Bool3 left, Bool3 right) => new Bool3(
        left.x != right.x,
        left.y != right.y,
        left.z != right.z
    );

    public static Bool3 operator !(Bool3 value) => new Bool3(
        !value.x,
        !value.y,
        !value.z
    );

    #endregion

    #region Conversion

    // Implicit conversion from single bool (sets all components)
    public static implicit operator Bool3(bool value) => new(value);

    // Explicit conversion to byte (raw data)
    public static explicit operator byte(Bool3 value) => value._data;

    // Explicit conversion from byte (raw data)
    public static explicit operator Bool3(byte value) =>
        new((value & X_MASK) != 0, (value & Y_MASK) != 0, (value & Z_MASK) != 0);

    #endregion

    public override string ToString() => $"({x}, {y}, {z})";
}
