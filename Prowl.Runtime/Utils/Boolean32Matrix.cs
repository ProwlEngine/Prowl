// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using Prowl.Echo;

namespace Prowl.Runtime.Utils;

/// <summary>
/// Represents a 32x32 boolean matrix using bit operations for efficient storage and operations.
/// Each row is stored as a uint where each bit represents a column value.
/// </summary>
public struct Boolean32Matrix : IEquatable<Boolean32Matrix>, ISerializable
{
    private uint[] rows;

    public Boolean32Matrix()
    {
        rows = new uint[32];
    }

    /// <summary>
    /// Creates a new Boolean32Matrix with all values set to the specified state
    /// </summary>
    public Boolean32Matrix(bool defaultValue)
    {
        rows = new uint[32];
        if (defaultValue)
            SetAll(true);
    }

    /// <summary>
    /// Gets or sets the value at the specified row and column
    /// </summary>
    public bool this[int row, int col]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (row >= 0 && row < 32 && col >= 0 && col < 32) && (rows[row] & (1u << col)) != 0;

        set
        {
            if (row >= 0 && row < 32 && col >= 0 && col < 32)
            {
                if (value)
                    rows[row] |= (1u << col);
                else
                    rows[row] &= ~(1u << col);
            }
        }
    }

    /// <summary>
    /// Sets a symmetric value (both [row,col] and [col,row]) in the matrix
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSymmetric(int row, int col, bool value)
    {
        this[row, col] = value;
        this[col, row] = value;
    }

    /// <summary>
    /// Sets all values in the matrix to the specified value
    /// </summary>
    public void SetAll(bool value)
    {
        uint setValue = value ? uint.MaxValue : 0;
        for (int i = 0; i < 32; i++)
            rows[i] = setValue;
    }

    /// <summary>
    /// Sets all values in a row to the specified value
    /// </summary>
    public void SetRow(int row, bool value)
    {
        if (row >= 0 && row < 32)
            rows[row] = value ? uint.MaxValue : 0;
    }

    /// <summary>
    /// Sets all values in a column to the specified value
    /// </summary>
    public void SetColumn(int col, bool value)
    {
        if (col >= 0 && col < 32)
        {
            uint mask = 1u << col;
            for (int i = 0; i < 32; i++)
            {
                if (value)
                    rows[i] |= mask;
                else
                    rows[i] &= ~mask;
            }
        }
    }

    /// <summary>
    /// Gets all values in a row as a bool array
    /// </summary>
    public bool[] GetRow(int row)
    {
        bool[] result = new bool[32];
        if (row >= 0 && row < 32)
        {
            uint rowValue = rows[row];
            for (int i = 0; i < 32; i++)
                result[i] = (rowValue & (1u << i)) != 0;
        }
        return result;
    }

    /// <summary>
    /// Gets all values in a column as a bool array
    /// </summary>
    public bool[] GetColumn(int col)
    {
        bool[] result = new bool[32];
        if (col >= 0 && col < 32)
        {
            uint mask = 1u << col;
            for (int i = 0; i < 32; i++)
                result[i] = (rows[i] & mask) != 0;
        }
        return result;
    }

    /// <summary>
    /// Returns true if the matrix is symmetric (value[row,col] == value[col,row] for all positions)
    /// </summary>
    public bool IsSymmetric()
    {
        for (int i = 0; i < 32; i++)
            for (int j = i + 1; j < 32; j++)
                if (this[i, j] != this[j, i])
                    return false;
        return true;
    }

    /// <summary>
    /// Makes the matrix symmetric by copying the upper triangle to the lower triangle
    /// </summary>
    public void MakeSymmetric()
    {
        for (int i = 0; i < 32; i++)
            for (int j = i + 1; j < 32; j++)
                this[j, i] = this[i, j];
    }

    public bool Equals(Boolean32Matrix other)
    {
        for (int i = 0; i < 32; i++)
            if (rows[i] != other.rows[i])
                return false;
        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is Boolean32Matrix matrix && Equals(matrix);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        for (int i = 0; i < 32; i++)
            hash.Add(rows[i]);
        return hash.ToHashCode();
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        var columnsList = EchoObject.NewList();
        for (int i = 0; i < 32; i++)
        {
            columnsList.ListAdd(new EchoObject(rows[i]));
        }
        value.Add("Columns", columnsList);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        rows = new uint[32];
        List<EchoObject> columnsList = value.Get("Columns").List;
        for (int i = 0; i < 32; i++)
        {
            rows[i] = columnsList[i].UIntValue;
        }
    }

    public static bool operator ==(Boolean32Matrix left, Boolean32Matrix right) => left.Equals(right);
    public static bool operator !=(Boolean32Matrix left, Boolean32Matrix right) => !left.Equals(right);
}
