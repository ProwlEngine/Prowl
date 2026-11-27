// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

namespace Prowl.Echo.Test
{
    // The rule is global, so it only claims Stored objects and every other test is unaffected.
    [Collection("ReferenceRule")]
    public class ReferenceRule_Tests : IDisposable
    {
        private class Stored
        {
            public string Name = "";
            public Stored? Self;
            public Stored? Other;
        }

        private sealed class DerivedStored : Stored { }

        private struct Holder
        {
            public Stored? Value;
        }

        private sealed class Owner
        {
            public Stored? Field;
            public object? Boxed;
            public Holder Held;
            public List<Stored> List = new();
            public Stored[] Array = System.Array.Empty<Stored>();
            public Dictionary<string, Stored> ByName = new();
            public Dictionary<Stored, int> ByKey = new();
        }

        private sealed class Store : IReferenceRule
        {
            public readonly Dictionary<string, Stored> Objects = new();
            public string Key => "$stored";

            public bool TryGetReference(object value, SerializationContext context, out string reference)
            {
                reference = value is Stored stored ? stored.Name : "";
                return value is Stored;
            }

            public object? Resolve(string reference, Type declaredType, SerializationContext context)
                => Objects.TryGetValue(reference, out Stored? stored) ? stored : null;
        }

        private readonly Store _store = new();

        public ReferenceRule_Tests() => Serializer.ReferenceRule = _store;

        public void Dispose() => Serializer.ReferenceRule = null;

        private Stored Add(string name, Stored? stored = null)
        {
            stored ??= new Stored();
            stored.Name = name;
            _store.Objects[name] = stored;
            return stored;
        }

        private static T RoundTrip<T>(T value) => Serializer.Deserialize<T>(Serializer.Serialize(value))!;

        [Fact]
        public void TheRootIsWrittenInFull_AndWhatItReferencesAsAStub()
        {
            Stored rock = Add("Rock");
            rock.Other = Add("Moss");

            EchoObject echo = Serializer.Serialize(typeof(Stored), rock);

            Assert.Equal("Rock", echo["Name"].StringValue);
            Assert.Equal("Moss", echo["Other"]["$stored"].StringValue);
            Assert.False(echo["Other"].Contains("Name"));
        }

        [Fact]
        public void ASelfReference_IsAStub_AndReadsBackAsTheStoredObject()
        {
            Stored rock = Add("Rock");
            rock.Self = rock;

            Stored copy = Serializer.Deserialize<Stored>(Serializer.Serialize(typeof(Stored), rock))!;

            Assert.NotSame(rock, copy);
            Assert.Same(rock, copy.Self);
        }

        [Fact]
        public void StubsResolveInEveryKindOfContainer()
        {
            Stored a = Add("A");
            Stored b = Add("B", new DerivedStored());
            var owner = new Owner
            {
                Field = a,
                Boxed = b,
                Held = new Holder { Value = a },
                List = { a, b },
                Array = new[] { b },
                ByName = { ["a"] = a },
                ByKey = { [b] = 7 },
            };

            Owner copy = RoundTrip(owner);

            Assert.Same(a, copy.Field);
            Assert.Same(b, copy.Boxed);
            Assert.Same(a, copy.Held.Value);
            Assert.Same(a, copy.List[0]);
            Assert.Same(b, copy.List[1]);
            Assert.Same(b, copy.Array[0]);
            Assert.Same(a, copy.ByName["a"]);
            Assert.Equal(7, copy.ByKey[b]);
        }

        [Fact]
        public void AStructAtTheRoot_StillStubsWhatItHolds()
        {
            Stored a = Add("A");

            EchoObject echo = Serializer.Serialize(typeof(Holder), new Holder { Value = a });

            Assert.Equal("A", echo["Value"]["$stored"].StringValue);
        }

        [Fact]
        public void AStubKeepsItsTypeWhenTheFieldIsLessSpecific()
        {
            Add("B", new DerivedStored());

            EchoObject echo = Serializer.Serialize(new Owner { Field = _store.Objects["B"] });

            Assert.True(echo["Field"].Contains("$type"));
        }

        [Fact]
        public void DeserializingIntoALiveObject_ResolvesStubs()
        {
            Stored moss = Add("Moss");
            EchoObject echo = Serializer.Serialize(new Owner { Field = moss });

            var target = new Owner();
            Serializer.DeserializeInto(echo, target);

            Assert.Same(moss, target.Field);
        }

        [Fact]
        public void IgnoringTheRule_WritesEverythingInline()
        {
            Stored moss = Add("Moss");

            EchoObject echo = Serializer.Serialize(new Owner { Field = moss }, new SerializationContext { IgnoreReferenceRule = true });

            Assert.Equal("Moss", echo["Field"]["Name"].StringValue);
        }

        [Fact]
        public void RootByReference_WritesTheRootAsAStubToo()
        {
            Stored moss = Add("Moss");

            EchoObject echo = Serializer.Serialize(typeof(Stored), moss, new SerializationContext { RootByReference = true });

            Assert.Equal("Moss", echo["$stored"].StringValue);
            Assert.Same(moss, Serializer.Deserialize<Stored>(echo));
        }

        [Fact]
        public void TheRootIsOnlyTheOutermostValue_AndClearsAfterTheWrite()
        {
            Stored moss = Add("Moss");
            var context = new SerializationContext();

            Serializer.Serialize(new Owner { Field = moss }, context);
            EchoObject second = Serializer.Serialize(typeof(Stored), moss, context);

            Assert.Null(context.Root);
            Assert.Equal("Moss", second["Name"].StringValue);
        }

        [Fact]
        public void TheRuleAndExternalReferences_EachHandleTheirOwnObjects()
        {
            Stored moss = Add("Moss");
            var outside = new List<int> { 1 };
            var holder = new Dictionary<string, object> { ["moss"] = moss, ["outside"] = outside };
            var resolver = new ListResolver(outside);

            EchoObject echo = Serializer.Serialize(holder, new SerializationContext { ExternalReferences = resolver });
            var copy = Serializer.Deserialize<Dictionary<string, object>>(echo, new SerializationContext { ExternalReferences = resolver })!;

            Assert.Same(moss, copy["moss"]);
            Assert.Same(outside, copy["outside"]);
        }

        private sealed class ListResolver(List<int> outside) : IExternalReferenceResolver
        {
            public object? GetReferenceKey(object value) => ReferenceEquals(value, outside) ? 1 : null;
            public object? ResolveReference(object key, Type targetType) => outside;
        }
    }
}
