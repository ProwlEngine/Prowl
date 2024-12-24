namespace Prowl.Echo.Test
{

    public class StringConverterTests
    {

        [Fact]
        public void TestPrimitiveTypes()
        {
            // Arrange
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

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

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

        [Fact]
        public void TestByteArray()
        {
            // Arrange
            var compound = EchoObject.NewCompound();
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            compound.Add("bytes", new EchoObject(EchoType.ByteArray, bytes));

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            Assert.Equal(bytes, deserialized.Get("bytes").ByteArrayValue);
        }

        [Fact]
        public void TestNestedCompounds()
        {
            // Arrange
            var inner = EchoObject.NewCompound();
            inner.Add("value", new EchoObject(EchoType.Int, 42));

            var outer = EchoObject.NewCompound();
            outer.Add("nested", inner);

            string text = outer.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            Assert.Equal(42, deserialized.Get("nested").Get("value").IntValue);
        }

        [Fact]
        public void TestList()
        {
            // Arrange
            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(EchoType.Int, 1));
            list.ListAdd(new EchoObject(EchoType.Int, 2));
            list.ListAdd(new EchoObject(EchoType.Int, 3));

            var compound = EchoObject.NewCompound();
            compound.Add("list", list);

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            var deserializedList = deserialized.Get("list");
            Assert.Equal(3, deserializedList.Count);
            Assert.Equal(1, deserializedList.List[0].IntValue);
            Assert.Equal(2, deserializedList.List[1].IntValue);
            Assert.Equal(3, deserializedList.List[2].IntValue);
        }

        [Fact]
        public void TestFileIO()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("test", new EchoObject(EchoType.String, "file test"));

            var tempFile = Path.GetTempFileName();

            try
            {
                compound.WriteToString(new FileInfo(tempFile));
                var deserialized = EchoObject.ReadFromString(new FileInfo(tempFile));

                Assert.Equal("file test", deserialized.Get("test").StringValue);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void TestIncompatibleVersions()
        {

            // Write with current version
            var compound = EchoObject.NewCompound();
            compound.Add("test", new EchoObject(EchoType.String, "test"));

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            Assert.Equal("test", deserialized.Get("test").StringValue);
        }

        [Fact]
        public void TestLongStrings()
        {
            // Arrange
            var compound = EchoObject.NewCompound();
            var longString = new string('a', 1000); // 1000 characters
            compound.Add("long_string", new EchoObject(EchoType.String, longString));

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            Assert.Equal(longString, deserialized.Get("long_string").StringValue);
        }

        [Fact]
        public void TestComplexStructure()
        {
            // Arrange
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
            string text = root.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            // Assert
            var deserializedList = deserialized.Get("list");
            Assert.Equal("one", deserializedList.List[0].StringValue);
            Assert.Equal("two", deserializedList.List[1].StringValue);

            var deserializedCompound = deserialized.Get("compound");
            Assert.Equal(42, deserializedCompound.Get("number").IntValue);
            Assert.Equal("test", deserializedCompound.Get("text").StringValue);
        }

        [Fact]
        public void TestLargeNumbers()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("large_int", new EchoObject(EchoType.Int, int.MaxValue));
            compound.Add("large_uint", new EchoObject(EchoType.UInt, uint.MaxValue));
            compound.Add("large_long", new EchoObject(EchoType.Long, long.MaxValue));
            compound.Add("large_ulong", new EchoObject(EchoType.ULong, ulong.MaxValue));

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            Assert.Equal(int.MaxValue, deserialized.Get("large_int").IntValue);
            Assert.Equal(uint.MaxValue, deserialized.Get("large_uint").UIntValue);
            Assert.Equal(long.MaxValue, deserialized.Get("large_long").LongValue);
            Assert.Equal(ulong.MaxValue, deserialized.Get("large_ulong").ULongValue);
        }

        [Fact]
        public void TestLargeCollections()
        {
            var compound = EchoObject.NewCompound();
            var list = EchoObject.NewList();
            for (int i = 0; i < 1000000; i++) // 1M items
                list.ListAdd(new EchoObject(EchoType.Int, i));
            compound.Add("large_list", list);

            string text = compound.WriteToString();

            var deserialized = EchoObject.ReadFromString(text);

            Assert.Equal(1000000, deserialized.Get("large_list").Count);
            for (int i = 0; i < 1000000; i++)
                Assert.Equal(i, deserialized.Get("large_list").List[i].IntValue);
        }

    }
}
