namespace Prowl.Echo.Test
{
    public class LEB128Tests
    {
        [Theory]
        [InlineData(0UL)]
        [InlineData(1UL)]
        [InlineData(127UL)]
        [InlineData(128UL)]
        [InlineData(16383UL)]
        [InlineData(16384UL)]
        [InlineData(ulong.MaxValue)]
        public void TestUnsignedRoundTrip(ulong value)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Write the value
            LEB128.WriteUnsigned(writer, value);

            // Read it back
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var result = LEB128.ReadUnsigned(reader);

            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(63L)]
        [InlineData(-64L)]
        [InlineData(64L)]
        [InlineData(-65L)]
        [InlineData(8191L)]
        [InlineData(-8192L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void TestSignedRoundTrip(long value)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Write the value
            LEB128.WriteSigned(writer, value);

            // Read it back
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var result = LEB128.ReadSigned(reader);

            Assert.Equal(value, result);
        }

        [Fact]
        public void TestUnsignedKnownValues()
        {
            // Test cases from DWARF LEB128 specification
            void TestValue(ulong value, byte[] expected)
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);

                LEB128.WriteUnsigned(writer, value);
                Assert.Equal(expected, stream.ToArray());

                stream.Position = 0;
                using var reader = new BinaryReader(stream);
                Assert.Equal(value, LEB128.ReadUnsigned(reader));
            }

            TestValue(2, new byte[] { 0x02 });
            TestValue(127, new byte[] { 0x7F });
            TestValue(128, new byte[] { 0x80, 0x01 });
            TestValue(129, new byte[] { 0x81, 0x01 });
            TestValue(130, new byte[] { 0x82, 0x01 });
            TestValue(12857, new byte[] { 0xB9, 0x64 });
        }

        [Fact]
        public void TestSignedKnownValues()
        {
            // Test cases from DWARF LEB128 specification
            void TestValue(long value, byte[] expected)
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);

                LEB128.WriteSigned(writer, value);
                Assert.Equal(expected, stream.ToArray());

                stream.Position = 0;
                using var reader = new BinaryReader(stream);
                Assert.Equal(value, LEB128.ReadSigned(reader));
            }

            TestValue(2, new byte[] { 0x02 });
            TestValue(-2, new byte[] { 0x7E });
            TestValue(127, new byte[] { 0xFF, 0x00 });
            TestValue(-127, new byte[] { 0x81, 0x7F });
            TestValue(128, new byte[] { 0x80, 0x01 });
            TestValue(-128, new byte[] { 0x80, 0x7F });
            TestValue(129, new byte[] { 0x81, 0x01 });
            TestValue(-129, new byte[] { 0xFF, 0x7E });
        }

        [Fact]
        public void TestOverflowDetection()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Write 10 bytes with continuation bits set
            for (int i = 0; i < 10; i++)
                writer.Write((byte)0xFF);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<OverflowException>(() => LEB128.ReadUnsigned(reader));

            stream.Position = 0;
            Assert.Throws<OverflowException>(() => LEB128.ReadSigned(reader));
        }

        [Fact]
        public void TestIncompleteRead()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Write incomplete LEB128 value
            writer.Write((byte)0x80);

            stream.Position = 0;
            using var reader = new BinaryReader(stream);

            Assert.Throws<EndOfStreamException>(() => LEB128.ReadUnsigned(reader));

            stream.Position = 0;
            Assert.Throws<EndOfStreamException>(() => LEB128.ReadSigned(reader));
        }
    }
}
