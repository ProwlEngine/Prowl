// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Runtime.Utils;

using Xunit;

namespace Prowl.Runtime.Test;

public class Boolean32MatrixTests
{
    [Fact]
    public void Constructor_Default_CreatesEmptyMatrix()
    {
        var matrix = new Boolean32Matrix();

        // Check all values are false
        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Assert.False(matrix[i, j]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_WithDefaultValue_SetsAllValues(bool defaultValue)
    {
        var matrix = new Boolean32Matrix(defaultValue);

        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Assert.Equal(defaultValue, matrix[i, j]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 15)]
    [InlineData(31, 31)]
    public void Indexer_ValidIndices_WorksCorrectly(int row, int col)
    {
        var matrix = new Boolean32Matrix();

        Assert.False(matrix[row, col]);

        matrix[row, col] = true;
        Assert.True(matrix[row, col]);

        matrix[row, col] = false;
        Assert.False(matrix[row, col]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(32, 0)]
    [InlineData(0, 32)]
    public void Indexer_InvalidIndices_HandlesGracefully(int row, int col)
    {
        var matrix = new Boolean32Matrix();

        // Should not throw and return false for invalid indices
        Assert.False(matrix[row, col]);

        // Should not throw when setting invalid indices
        matrix[row, col] = true;
    }

    [Fact]
    public void SetSymmetric_ValidIndices_SetsBothPositions()
    {
        var matrix = new Boolean32Matrix();

        matrix.SetSymmetric(1, 2, true);

        Assert.True(matrix[1, 2]);
        Assert.True(matrix[2, 1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAll_SetsAllValuesCorrectly(bool value)
    {
        var matrix = new Boolean32Matrix(!value); // Initialize with opposite value
        matrix.SetAll(value);

        for (int i = 0; i < 32; i++)
            for (int j = 0; j < 32; j++)
                Assert.Equal(value, matrix[i, j]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    public void SetRow_SetsEntireRowCorrectly(int row)
    {
        var matrix = new Boolean32Matrix();
        matrix.SetRow(row, true);

        for (int col = 0; col < 32; col++)
            Assert.True(matrix[row, col]);

        matrix.SetRow(row, false);

        for (int col = 0; col < 32; col++)
            Assert.False(matrix[row, col]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    public void SetColumn_SetsEntireColumnCorrectly(int col)
    {
        var matrix = new Boolean32Matrix();
        matrix.SetColumn(col, true);

        for (int row = 0; row < 32; row++)
            Assert.True(matrix[row, col]);

        matrix.SetColumn(col, false);

        for (int row = 0; row < 32; row++)
            Assert.False(matrix[row, col]);
    }

    [Fact]
    public void GetRow_ReturnsCorrectValues()
    {
        var matrix = new Boolean32Matrix();
        matrix[5, 0] = true;
        matrix[5, 31] = true;

        bool[] row = matrix.GetRow(5);

        Assert.True(row[0]);
        Assert.True(row[31]);
        Assert.False(row[15]);
    }

    [Fact]
    public void GetColumn_ReturnsCorrectValues()
    {
        var matrix = new Boolean32Matrix();
        matrix[0, 5] = true;
        matrix[31, 5] = true;

        bool[] col = matrix.GetColumn(5);

        Assert.True(col[0]);
        Assert.True(col[31]);
        Assert.False(col[15]);
    }

    [Fact]
    public void IsSymmetric_ReturnsTrueForSymmetricMatrix()
    {
        var matrix = new Boolean32Matrix();

        // Make some symmetric changes
        matrix.SetSymmetric(0, 1, true);
        matrix.SetSymmetric(5, 10, true);

        Assert.True(matrix.IsSymmetric());
    }

    [Fact]
    public void IsSymmetric_ReturnsFalseForAsymmetricMatrix()
    {
        var matrix = new Boolean32Matrix();

        // Make asymmetric change
        matrix[0, 1] = true;
        matrix[1, 0] = false;

        Assert.False(matrix.IsSymmetric());
    }

    [Fact]
    public void MakeSymmetric_CreatesSymmetricMatrix()
    {
        var matrix = new Boolean32Matrix();

        // Make asymmetric changes
        matrix[0, 1] = true;
        matrix[1, 0] = false;
        matrix[5, 10] = true;
        matrix[10, 5] = false;

        matrix.MakeSymmetric();

        Assert.True(matrix.IsSymmetric());
        Assert.True(matrix[0, 1]);
        Assert.True(matrix[1, 0]);
        Assert.True(matrix[5, 10]);
        Assert.True(matrix[10, 5]);
    }

    [Fact]
    public void Serialization_PreservesValues()
    {
        var original = new Boolean32Matrix();
        original.SetSymmetric(0, 1, true);
        original.SetSymmetric(5, 10, true);

        var tags = Serializer.Serialize(original);
        var deserialized = Serializer.Deserialize<Boolean32Matrix>(tags);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Equals_ReturnsTrueForIdenticalMatrices()
    {
        var matrix1 = new Boolean32Matrix();
        var matrix2 = new Boolean32Matrix();

        matrix1.SetSymmetric(0, 1, true);
        matrix2.SetSymmetric(0, 1, true);

        Assert.Equal(matrix1, matrix2);
        Assert.True(matrix1 == matrix2);
    }

    [Fact]
    public void Equals_ReturnsFalseForDifferentMatrices()
    {
        var matrix1 = new Boolean32Matrix();
        var matrix2 = new Boolean32Matrix();

        matrix1.SetSymmetric(0, 1, true);
        matrix2.SetSymmetric(0, 2, true);

        Assert.NotEqual(matrix1, matrix2);
        Assert.True(matrix1 != matrix2);
    }

    [Fact]
    public void GetHashCode_ReturnsSameValueForEqualMatrices()
    {
        var matrix1 = new Boolean32Matrix();
        var matrix2 = new Boolean32Matrix();

        matrix1.SetSymmetric(0, 1, true);
        matrix2.SetSymmetric(0, 1, true);

        Assert.Equal(matrix1.GetHashCode(), matrix2.GetHashCode());
    }

    [Fact]
    public void BoundaryTest_AllCornersAndEdges()
    {
        var matrix = new Boolean32Matrix();

        // Test corners
        matrix[0, 0] = true;
        matrix[0, 31] = true;
        matrix[31, 0] = true;
        matrix[31, 31] = true;

        Assert.True(matrix[0, 0]);
        Assert.True(matrix[0, 31]);
        Assert.True(matrix[31, 0]);
        Assert.True(matrix[31, 31]);
    }

    [Fact]
    public void StressTest_RandomOperations()
    {
        var matrix = new Boolean32Matrix();
        var random = new System.Random(42); // Fixed seed for reproducibility

        // Perform 10000 random operations
        for (int i = 0; i < 10000; i++)
        {
            int row = random.Next(0, 32);
            int col = random.Next(0, 32);
            bool value = random.Next(2) == 1;

            matrix.SetSymmetric(row, col, value);
            Assert.Equal(value, matrix[row, col]);
            Assert.Equal(value, matrix[col, row]);
        }
    }
}
