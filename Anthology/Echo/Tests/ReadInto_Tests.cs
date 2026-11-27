// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Echo.Test
{
    public class ReadInto_Tests
    {
        private sealed class Part
        {
            public int Value;
            public Part? Other;
        }

        private sealed class Whole
        {
            public Part? First;
            public Part? Second;
            public List<Part> All = new();
        }

        private static (Whole source, Part first, Part second) Build()
        {
            var first = new Part { Value = 1 };
            var second = new Part { Value = 2, Other = first };
            return (new Whole { First = first, Second = second, All = { first, second } }, first, second);
        }

        private static EchoObject Write(object source, out SerializationContext written)
        {
            written = new SerializationContext();
            return Serializer.Serialize(source.GetType(), source, written);
        }

        [Fact]
        public void APairedDefinition_FillsTheExistingObject()
        {
            var (source, first, _) = Build();
            EchoObject echo = Write(source, out SerializationContext written);

            var existing = new Part { Value = 99 };
            var read = new SerializationContext();
            read.ReadInto(written.objectToId[first], existing);
            var copy = Serializer.Deserialize<Whole>(echo, read)!;

            Assert.Same(existing, copy.First);
            Assert.Equal(1, existing.Value);
        }

        [Fact]
        public void EveryReferenceToAPairedId_LandsOnTheExistingObject()
        {
            var (source, first, _) = Build();
            EchoObject echo = Write(source, out SerializationContext written);

            var existing = new Part();
            var read = new SerializationContext();
            read.ReadInto(written.objectToId[first], existing);
            var copy = Serializer.Deserialize<Whole>(echo, read)!;

            Assert.Same(existing, copy.Second!.Other);
            Assert.Same(existing, copy.All[0]);
        }

        [Fact]
        public void AnUnpairedDefinition_StillMakesANewObject()
        {
            var (source, first, second) = Build();
            EchoObject echo = Write(source, out SerializationContext written);

            var read = new SerializationContext();
            read.ReadInto(written.objectToId[first], new Part());
            var copy = Serializer.Deserialize<Whole>(echo, read)!;

            Assert.NotSame(second, copy.Second);
            Assert.Equal(2, copy.Second!.Value);
        }

        [Fact]
        public void DeserializeInto_FillsNestedPairedObjects_InsteadOfReplacingThem()
        {
            var (source, first, second) = Build();
            EchoObject echo = Write(source, out SerializationContext written);

            var target = new Whole { First = new Part(), Second = new Part() };
            Part keptFirst = target.First, keptSecond = target.Second;
            var read = new SerializationContext();
            read.ReadInto(written.objectToId[first], keptFirst);
            read.ReadInto(written.objectToId[second], keptSecond);
            Serializer.DeserializeInto(echo, target, read);

            Assert.Same(keptFirst, target.First);
            Assert.Same(keptSecond, target.Second);
            Assert.Same(keptFirst, keptSecond.Other);
            Assert.Equal(2, keptSecond.Value);
        }
    }
}
