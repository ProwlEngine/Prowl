// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Echo.Test
{
    public class ArrayElementIsolation_Tests
    {
        // Serializes fine but throws on the way back in, standing in for one corrupt element.
        private sealed class ThrowsOnDeserialize : ISerializable
        {
            // Writes a body so the value is a definition, not an empty reference stub, and its
            // Deserialize actually runs on the way back in.
            public void Serialize(ref EchoObject compound, SerializationContext ctx)
                => compound["Marker"] = new EchoObject(1);
            public void Deserialize(EchoObject value, SerializationContext ctx)
                => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void OneThrowingElement_LeavesItNullAndKeepsTheRest()
        {
            object[] source = { "a", new ThrowsOnDeserialize(), "c" };
            var clone = Serializer.Deserialize<object[]>(Serializer.Serialize(source));

            Assert.NotNull(clone);
            Assert.Equal(3, clone!.Length);
            Assert.Equal("a", clone[0]);
            Assert.Null(clone[1]);
            Assert.Equal("c", clone[2]);
        }
    }
}
