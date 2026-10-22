using System.IO;
using Prowl.Echo;

namespace Echo.Tests;

public class ArrayShapeValidationTests
{
    private static EchoObject Shape(int[] dimensions, int count)
    {
        EchoObject value = EchoObject.NewCompound();
        value["dimensions"] = Serializer.Serialize(dimensions);
        EchoObject elements = EchoObject.NewList();
        for (int i = 0; i < count; i++) elements.ListAdd(new EchoObject(i));
        value["elements"] = elements;
        return value;
    }

    [Theory]
    [InlineData(new[] { 1000, 1000 }, 0)]
    [InlineData(new[] { int.MaxValue, int.MaxValue }, 0)]
    [InlineData(new[] { -1, 2 }, 0)]
    [InlineData(new[] { 0, -1 }, 0)]
    [InlineData(new[] { 1 }, 1)]
    [InlineData(new[] { 1, 1, 1 }, 1)]
    [InlineData(new[] { 2, 2 }, 3)]
    [InlineData(new[] { 1, 1 }, 2)]
    [InlineData(new[] { 0, 2 }, 1)]
    public void InvalidShapeIsRejectedBeforeArrayAllocation(int[] dimensions, int count)
    {
        EchoObject value = Shape(dimensions, count);
        Assert.Throws<InvalidDataException>(() => Serializer.Deserialize<int[,]>(value));
    }

    [Fact]
    public void DimensionProductCannotOverflow()
    {
        EchoObject value = Shape(new[] { int.MaxValue, int.MaxValue, int.MaxValue }, 0);
        Assert.Throws<InvalidDataException>(() => Serializer.Deserialize<int[,,]>(value));
    }

    [Fact]
    public void ElementsMustBeAList()
    {
        EchoObject value = Shape(new[] { 1, 1 }, 0);
        value["elements"] = new EchoObject("invalid");
        Assert.Throws<InvalidDataException>(() => Serializer.Deserialize<int[,]>(value));
    }

    [Theory]
    [InlineData(1_000_000, 0)]
    [InlineData(0, 1_000_000)]
    public void EmptyArraysWithLargeAxesRoundTripWithoutWalkingEmptyDimensions(int first, int second)
    {
        int[,] source = new int[first, second];
        int[,] copy = Serializer.Deserialize<int[,]>(Serializer.Serialize(source))!;
        Assert.Empty(copy);
        Assert.Equal(first, copy.GetLength(0));
        Assert.Equal(second, copy.GetLength(1));
    }

    [Fact]
    public void ValidArrayPreservesShapeAndElements()
    {
        int[,] result = Serializer.Deserialize<int[,]>(Shape(new[] { 2, 3 }, 6))!;
        Assert.Equal(2, result.GetLength(0));
        Assert.Equal(3, result.GetLength(1));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, result.Cast<int>());
    }
}
