using System.Diagnostics;
using Xunit.Abstractions;

namespace Prowl.Echo.Test
{

    public class BinaryTagConverterTests
    {
        private readonly ITestOutputHelper _output;

        public BinaryTagConverterTests(ITestOutputHelper testOutputHelper)
        {
            _output = testOutputHelper;
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestPrimitiveTypes(BinaryEncodingMode mode)
        {
            // Arrange
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var compound = EchoObject.NewCompound();
            compound.Add("byte", new EchoObject(EchoType.Byte, (byte)255));
            compound.Add("sbyte", new EchoObject(EchoType.sByte, (sbyte)-128));
            compound.Add("short", new EchoObject(EchoType.Short, (short)-32768));
            compound.Add("ushort", new EchoObject(EchoType.UShort, (ushort)65535));
            compound.Add("int", new EchoObject(EchoType.Int, -1000000));
            compound.Add("uint", new EchoObject(EchoType.UInt, 1000000u));
            compound.Add("long", new EchoObject(EchoType.Long, -1000000L));
            compound.Add("ulong", new EchoObject(EchoType.ULong, 1000000uL));
            compound.Add("float", new EchoObject(EchoType.Float, 3.14f));
            compound.Add("double", new EchoObject(EchoType.Double, 3.14159));
            compound.Add("decimal", new EchoObject(EchoType.Decimal, 3.14159m));
            compound.Add("bool", new EchoObject(EchoType.Bool, true));
            compound.Add("string", new EchoObject(EchoType.String, "test"));
            compound.Add("null", new EchoObject(EchoType.Null, null));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            // Assert
            Assert.Equal(compound.Count, deserialized.Count);
            Assert.Equal((byte)255, deserialized.Get("byte").ByteValue);
            Assert.Equal((sbyte)-128, deserialized.Get("sbyte").sByteValue);
            Assert.Equal((short)-32768, deserialized.Get("short").ShortValue);
            Assert.Equal((ushort)65535, deserialized.Get("ushort").UShortValue);
            Assert.Equal(-1000000, deserialized.Get("int").IntValue);
            Assert.Equal(1000000u, deserialized.Get("uint").UIntValue);
            Assert.Equal(-1000000L, deserialized.Get("long").LongValue);
            Assert.Equal(1000000uL, deserialized.Get("ulong").ULongValue);
            Assert.Equal(3.14f, deserialized.Get("float").FloatValue);
            Assert.Equal(3.14159, deserialized.Get("double").DoubleValue);
            Assert.Equal(3.14159m, deserialized.Get("decimal").DecimalValue);
            Assert.True(deserialized.Get("bool").BoolValue);
            Assert.Equal("test", deserialized.Get("string").StringValue);
            Assert.Equal(EchoType.Null, deserialized.Get("null").TagType);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestByteArray(BinaryEncodingMode mode)
        {
            // Arrange
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var compound = EchoObject.NewCompound();
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            compound.Add("bytes", new EchoObject(EchoType.ByteArray, bytes));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(bytes, deserialized.Get("bytes").ByteArrayValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestNestedCompounds(BinaryEncodingMode mode)
        {
            // Arrange
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var inner = EchoObject.NewCompound();
            inner.Add("value", new EchoObject(EchoType.Int, 42));

            var outer = EchoObject.NewCompound();
            outer.Add("nested", inner);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            outer.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(42, deserialized.Get("nested").Get("value").IntValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestList(BinaryEncodingMode mode)
        {
            // Arrange
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(EchoType.Int, 1));
            list.ListAdd(new EchoObject(EchoType.Int, 2));
            list.ListAdd(new EchoObject(EchoType.Int, 3));

            var compound = EchoObject.NewCompound();
            compound.Add("list", list);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            var deserializedList = deserialized.Get("list");
            Assert.Equal(3, deserializedList.Count);
            Assert.Equal(1, deserializedList.List[0].IntValue);
            Assert.Equal(2, deserializedList.List[1].IntValue);
            Assert.Equal(3, deserializedList.List[2].IntValue);
        }

        [Fact]
        public void TestFileIO()
        {
            // Test both modes with file IO
            foreach (var mode in new[] { BinaryEncodingMode.Performance, BinaryEncodingMode.Size })
            {
                var options = new BinarySerializationOptions { EncodingMode = mode };
                var compound = EchoObject.NewCompound();
                compound.Add("test", new EchoObject(EchoType.String, "file test"));

                var tempFile = Path.GetTempFileName();

                try
                {
                    compound.WriteToBinary(new FileInfo(tempFile), options);

                    var deserialized = EchoObject.ReadFromBinary(new FileInfo(tempFile), options);

                    Assert.Equal("file test", deserialized.Get("test").StringValue);
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestIncompatibleVersions(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };

            // Write with current version
            var compound = EchoObject.NewCompound();
            compound.Add("test", new EchoObject(EchoType.String, "test"));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            // Verify we can read it back
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);
            Assert.Equal("test", deserialized.Get("test").StringValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestLongStrings(BinaryEncodingMode mode)
        {
            // Arrange
            Stopwatch sw = Stopwatch.StartNew();
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var compound = EchoObject.NewCompound();
            var longString = new string('a', 1000); // 1000 characters
            compound.Add("long_string", new EchoObject(EchoType.String, longString));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(longString, deserialized.Get("long_string").StringValue);

            sw.Stop();
            _output.WriteLine($"Time taken: {sw.Elapsed.TotalMilliseconds} ms");
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestComplexStructure(BinaryEncodingMode mode)
        {
            // Arrange
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var innerList = EchoObject.NewList();
            innerList.ListAdd(new EchoObject(EchoType.String, "one"));
            innerList.ListAdd(new EchoObject(EchoType.String, "two"));

            var innerCompound = EchoObject.NewCompound();
            innerCompound.Add("number", new EchoObject(EchoType.Int, 42));
            innerCompound.Add("text", new EchoObject(EchoType.String, "test"));

            var root = EchoObject.NewCompound();
            root.Add("list", innerList);
            root.Add("compound", innerCompound);

            // Act
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            root.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            // Assert
            var deserializedList = deserialized.Get("list");
            Assert.Equal("one", deserializedList.List[0].StringValue);
            Assert.Equal("two", deserializedList.List[1].StringValue);

            var deserializedCompound = deserialized.Get("compound");
            Assert.Equal(42, deserializedCompound.Get("number").IntValue);
            Assert.Equal("test", deserializedCompound.Get("text").StringValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestLargeNumbers(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var compound = EchoObject.NewCompound();
            compound.Add("large_int", new EchoObject(EchoType.Int, int.MaxValue));
            compound.Add("large_uint", new EchoObject(EchoType.UInt, uint.MaxValue));
            compound.Add("large_long", new EchoObject(EchoType.Long, long.MaxValue));
            compound.Add("large_ulong", new EchoObject(EchoType.ULong, ulong.MaxValue));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(int.MaxValue, deserialized.Get("large_int").IntValue);
            Assert.Equal(uint.MaxValue, deserialized.Get("large_uint").UIntValue);
            Assert.Equal(long.MaxValue, deserialized.Get("large_long").LongValue);
            Assert.Equal(ulong.MaxValue, deserialized.Get("large_ulong").ULongValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestLargeCollections(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var compound = EchoObject.NewCompound();
            var list = EchoObject.NewList();
            for (int i = 0; i < 1000000; i++) // 1M items
                list.ListAdd(new EchoObject(EchoType.Int, i));
            compound.Add("large_list", list);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            compound.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(1000000, deserialized.Get("large_list").Count);
            for (int i = 0; i < 1000000; i++)
                Assert.Equal(i, deserialized.Get("large_list").List[i].IntValue);
        }

        #region Non-Compound Tests

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestSinglePrimitiveTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var intTag = new EchoObject(EchoType.Int, 42);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            intTag.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(EchoType.Int, deserialized.TagType);
            Assert.Equal(42, deserialized.IntValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestSingleListTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(EchoType.Int, 1));
            list.ListAdd(new EchoObject(EchoType.String, "test"));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            list.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(2, deserialized.Count);
            Assert.Equal(1, deserialized.List[0].IntValue);
            Assert.Equal("test", deserialized.List[1].StringValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestByteArrayTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            var byteArrayTag = new EchoObject(EchoType.ByteArray, bytes);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            byteArrayTag.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(EchoType.ByteArray, deserialized.TagType);
            Assert.Equal(bytes, deserialized.ByteArrayValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestFloatingPointTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var doubleTag = new EchoObject(EchoType.Double, 3.14159);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            doubleTag.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(EchoType.Double, deserialized.TagType);
            Assert.Equal(3.14159, deserialized.DoubleValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestNullTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var nullTag = new EchoObject(EchoType.Null, null);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            nullTag.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(EchoType.Null, deserialized.TagType);
            Assert.Null(deserialized.Value);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestLongStringTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var longString = new string('a', 10000);
            var stringTag = new EchoObject(EchoType.String, longString);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            stringTag.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(EchoType.String, deserialized.TagType);
            Assert.Equal(longString, deserialized.StringValue);
        }

        [Theory]
        [InlineData(BinaryEncodingMode.Performance)]
        [InlineData(BinaryEncodingMode.Size)]
        public void TestNestedListTag(BinaryEncodingMode mode)
        {
            var options = new BinarySerializationOptions { EncodingMode = mode };
            var innerList = EchoObject.NewList();
            innerList.ListAdd(new EchoObject(EchoType.Int, 1));
            innerList.ListAdd(new EchoObject(EchoType.Int, 2));

            var outerList = EchoObject.NewList();
            outerList.ListAdd(innerList);
            outerList.ListAdd(new EchoObject(EchoType.String, "test"));

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            outerList.WriteToBinary(writer, options);

            _output.WriteLine($"Binary size: {stream.Length} bytes");

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var deserialized = EchoObject.ReadFromBinary(reader, options);

            Assert.Equal(2, deserialized.Count);
            var deserializedInnerList = deserialized.List[0];
            Assert.Equal(2, deserializedInnerList.Count);
            Assert.Equal(1, deserializedInnerList.List[0].IntValue);
            Assert.Equal(2, deserializedInnerList.List[1].IntValue);
            Assert.Equal("test", deserialized.List[1].StringValue);
        }

        #endregion
    }
}
