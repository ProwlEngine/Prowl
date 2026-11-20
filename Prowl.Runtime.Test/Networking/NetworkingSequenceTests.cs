using Prowl.Courier;
using Prowl.Runtime.Networking;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static T RoundTrip<T>(T value)
    {
        using NetWriter writer = new();
        NetSerializer<T>.Write(writer, value);
        NetReader reader = new(writer.WrittenSpan);
        T read = NetSerializer<T>.Read(ref reader);
        Assert.True(reader.IsAtEnd);
        return read;
    }

    [Fact]
    public void ArraysListsAndBytesTravelWithNullAndEmptyKept()
    {
        Assert.Equal(new[] { 1, -2, 300 }, RoundTrip<int[]>([1, -2, 300]));
        Assert.Equal(new List<string> { "a", "b", "c" }, RoundTrip<List<string>>(["a", "b", "c"]));
        Assert.Equal(new byte[] { 0, 255, 7 }, RoundTrip<byte[]>([0, 255, 7]));
        Assert.Null(RoundTrip<int[]?>(null));
        Assert.Null(RoundTrip<List<int>?>(null));
        Assert.Empty(RoundTrip<byte[]>([]));
        Assert.Equal(new List<int[]> { new[] { 1 }, new[] { 2, 3 } }, RoundTrip<List<int[]>>([[1], [2, 3]]));
    }

    [Fact]
    public void ASequenceLongerThanItsMessageIsMalformed()
    {
        using NetWriter writer = new();
        writer.WriteVarUInt32(1_000_000);
        writer.WriteVarInt32(1);
        Assert.Throws<InvalidDataException>(() =>
        {
            NetReader reader = new(writer.WrittenSpan);
            NetSerializer<int[]>.Read(ref reader);
        });
    }
}
