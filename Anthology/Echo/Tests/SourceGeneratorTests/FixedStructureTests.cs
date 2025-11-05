using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

public class FixedStructureTests
{
    private T RoundTrip<T>(T value)
    {
        var serialized = Serializer.Serialize(value);
        return Serializer.Deserialize<T>(serialized);
    }

    [Fact]
    public void Vector3_SerializesAsFixedStructure()
    {
        var original = new Vector3 { X = 1.5f, Y = 2.5f, Z = 3.5f };

        var serialized = Serializer.Serialize(original);

        // Verify it's serialized as a List, not a Compound
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.Equal(original.X, result.X);
        Assert.Equal(original.Y, result.Y);
        Assert.Equal(original.Z, result.Z);
    }

    [Fact]
    public void Quaternion_SerializesAsFixedStructure()
    {
        var original = new Quaternion { X = 0.0f, Y = 0.0f, Z = 0.0f, W = 1.0f };

        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.Equal(original.X, result.X);
        Assert.Equal(original.Y, result.Y);
        Assert.Equal(original.Z, result.Z);
        Assert.Equal(original.W, result.W);
    }

    [Fact]
    public void Color_SerializesAsFixedStructure()
    {
        var original = new Color { R = 255, G = 128, B = 64, A = 255 };

        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.Equal(original.R, result.R);
        Assert.Equal(original.G, result.G);
        Assert.Equal(original.B, result.B);
        Assert.Equal(original.A, result.A);
    }

    [Fact]
    public void NetworkPacket_Class_SerializesAsFixedStructure()
    {
        var original = new NetworkPacket
        {
            PacketId = 42,
            Timestamp = DateTime.Now.Ticks,
            Data = new byte[] { 1, 2, 3, 4, 5 }
        };

        var serialized = Serializer.Serialize(original);
        // For classes with FixedEchoStructure, the implementation uses a list
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.PacketId, result.PacketId);
        Assert.Equal(original.Timestamp, result.Timestamp);
        Assert.Equal(original.Data, result.Data);
    }

    [Fact]
    public void Transform_NestedFixedStructures_SerializesCorrectly()
    {
        var original = new Transform
        {
            Position = new Vector3 { X = 10, Y = 20, Z = 30 },
            Rotation = new Quaternion { X = 0, Y = 0, Z = 0, W = 1 },
            Scale = new Vector3 { X = 1, Y = 1, Z = 1 }
        };

        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.Equal(original.Position.X, result.Position.X);
        Assert.Equal(original.Position.Y, result.Position.Y);
        Assert.Equal(original.Position.Z, result.Position.Z);
        Assert.Equal(original.Rotation.W, result.Rotation.W);
        Assert.Equal(original.Scale.X, result.Scale.X);
    }

    [Fact]
    public void EntityWithFixedStructures_MixedSerialization_WorksCorrectly()
    {
        var original = new EntityWithFixedStructures
        {
            EntityId = 100,
            Position = new Vector3 { X = 1, Y = 2, Z = 3 },
            Rotation = new Quaternion { X = 0, Y = 0, Z = 0, W = 1 },
            Path = new List<Vector3>
            {
                new Vector3 { X = 0, Y = 0, Z = 0 },
                new Vector3 { X = 10, Y = 0, Z = 0 },
                new Vector3 { X = 10, Y = 10, Z = 0 }
            }
        };

        // Entity itself is not a FixedStructure, so it should use Compound
        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.Compound, serialized.TagType);

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.EntityId, result.EntityId);
        Assert.Equal(original.Position.X, result.Position.X);
        Assert.Equal(original.Rotation.W, result.Rotation.W);
        Assert.Equal(3, result.Path.Count);
        Assert.Equal(10f, result.Path[1].X);
    }

    [Fact]
    public void Point2D_SerializesAsFixedStructure()
    {
        var original = new Point2D { X = 100, Y = 200 };

        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.Equal(original.X, result.X);
        Assert.Equal(original.Y, result.Y);
    }

    [Fact]
    public void Rectangle_NestedFixedStructures_SerializesCorrectly()
    {
        var original = new Rectangle
        {
            TopLeft = new Point2D { X = 0, Y = 0 },
            BottomRight = new Point2D { X = 100, Y = 100 }
        };

        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.List, serialized.TagType);

        var result = RoundTrip(original);

        Assert.Equal(original.TopLeft.X, result.TopLeft.X);
        Assert.Equal(original.TopLeft.Y, result.TopLeft.Y);
        Assert.Equal(original.BottomRight.X, result.BottomRight.X);
        Assert.Equal(original.BottomRight.Y, result.BottomRight.Y);
    }

    [Fact]
    public void FixedNetworkMessage_SerializesCompactly()
    {
        var original = new FixedNetworkMessage
        {
            MessageType = 1,
            SenderId = 42,
            ReceiverId = 99,
            Payload = "Hello, World!"
        };

        var serialized = Serializer.Serialize(original);
        Assert.Equal(EchoType.List, serialized.TagType);

        // Verify it's more compact than compound (no field names stored)
        var list = (List<EchoObject>)serialized.Value!;
        Assert.Equal(4, list.Count); // Should have exactly 4 elements

        var result = RoundTrip(original);

        Assert.NotNull(result);
        Assert.Equal(original.MessageType, result.MessageType);
        Assert.Equal(original.SenderId, result.SenderId);
        Assert.Equal(original.ReceiverId, result.ReceiverId);
        Assert.Equal(original.Payload, result.Payload);
    }

    [Fact]
    public void ListOfFixedStructures_SerializesCorrectly()
    {
        var original = new List<Vector3>
        {
            new Vector3 { X = 1, Y = 2, Z = 3 },
            new Vector3 { X = 4, Y = 5, Z = 6 },
            new Vector3 { X = 7, Y = 8, Z = 9 }
        };

        var result = RoundTrip(original);

        Assert.Equal(3, result.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].X, result[i].X);
            Assert.Equal(original[i].Y, result[i].Y);
            Assert.Equal(original[i].Z, result[i].Z);
        }
    }

    [Fact]
    public void ArrayOfFixedStructures_SerializesCorrectly()
    {
        var original = new Vector3[]
        {
            new Vector3 { X = 1, Y = 1, Z = 1 },
            new Vector3 { X = 2, Y = 2, Z = 2 }
        };

        var result = RoundTrip(original);

        Assert.Equal(2, result.Length);
        Assert.Equal(original[0].X, result[0].X);
        Assert.Equal(original[1].Z, result[1].Z);
    }

    [Fact]
    public void FixedStructure_FieldOrderMatters()
    {
        var original = new Color { R = 10, G = 20, B = 30, A = 40 };

        var serialized = Serializer.Serialize(original);
        var list = (List<EchoObject>)serialized.Value!;

        // Fields should be in declaration order
        Assert.Equal(10, list[0].ByteValue); // R
        Assert.Equal(20, list[1].ByteValue); // G
        Assert.Equal(30, list[2].ByteValue); // B
        Assert.Equal(40, list[3].ByteValue); // A
    }

    [Fact]
    public void FixedStructure_MemoryEfficiency()
    {
        // Create both fixed and non-fixed versions to compare
        var fixedStruct = new Vector3 { X = 1, Y = 2, Z = 3 };
        var fixedSerialized = Serializer.Serialize(fixedStruct);

        // Fixed structure should use List (ordinal-based)
        Assert.Equal(EchoType.List, fixedSerialized.TagType);

        // List should contain exactly 3 elements (X, Y, Z) without field names
        var list = (List<EchoObject>)fixedSerialized.Value!;
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void FixedStructure_DefaultValues_SerializeCorrectly()
    {
        var original = new Vector3(); // All fields default to 0

        var serialized = Serializer.Serialize(original);
        var result = RoundTrip(original);

        Assert.Equal(0f, result.X);
        Assert.Equal(0f, result.Y);
        Assert.Equal(0f, result.Z);
    }

    [Fact]
    public void FixedStructure_MultipleRoundTrips_PreservesData()
    {
        var original = new Quaternion { X = 0.5f, Y = 0.5f, Z = 0.5f, W = 0.5f };

        var result1 = RoundTrip(original);
        var result2 = RoundTrip(result1);
        var result3 = RoundTrip(result2);

        Assert.Equal(original.X, result3.X);
        Assert.Equal(original.Y, result3.Y);
        Assert.Equal(original.Z, result3.Z);
        Assert.Equal(original.W, result3.W);
    }
}
