# Prowl.Echo Serializer

A high-performance, flexible serialization system built for the Prowl Game Engine. Echo creates an intermediate representation (an "Echo") of your objects, allowing fast inspection and modification before converting to and from Binary or Text.

## Performance

Echo with source generation **outperforms System.Text.Json and Newtonsoft.Json** across all operations.

Benchmark run with `[GenerateSerializer]` and `[FixedEchoStructure]` on a complex object graph (20 nested objects, 100-element arrays, 50-entry dictionaries, collections):

```
  Serialize:
    MessagePack          Avg:   0.0297 ms  (1.64x faster)
    Echo                 Avg:   0.0487 ms
    System.Text.Json     Avg:   0.0708 ms  (2.39x slower)
    Newtonsoft.Json      Avg:   0.1040 ms  (3.51x slower)

  Deserialize:
    Echo                 Avg:   0.0200 ms
    MessagePack          Avg:   0.0470 ms  (2.35x slower)
    System.Text.Json     Avg:   0.1113 ms  (5.56x slower)
    Newtonsoft.Json      Avg:   0.1570 ms  (7.84x slower)

  Round-trip:
    Echo                 Avg:   0.0644 ms
    MessagePack          Avg:   0.0714 ms  (1.11x slower)
    System.Text.Json     Avg:   0.1776 ms  (2.76x slower)
    Newtonsoft.Json      Avg:   0.2533 ms  (3.93x slower)
```

## Features

- **Type Support**
  - Primitives (int, float, double, string, bool, etc.)
  - Complex objects and nested types
  - Collections (List, Arrays, HashSet, Stack, Queue, LinkedLists)
  - Dictionaries
  - Enums
  - DateTime and Guid
  - Nullable types
  - Circular references
  - Anonymous types
  - Multi-dimensional and jagged arrays
  - 470+ tests to ensure stability and reliability

- **Source Generation**
  - `[GenerateSerializer]` attribute generates optimized `ISerializable` implementations at compile time
  - Inlines serialization for primitives, strings, enums, DateTime, Guid, TimeSpan, and common collections
  - Works alongside `[FixedEchoStructure]` for maximum performance, great for networking!
  - Zero reflection overhead for generated types

- **Flexible Serialization Control**
  - Custom serialization through `ISerializable` interface
  - Attribute-based control (`[FormerlySerializedAs]`, `[IgnoreOnNull]`, `[SerializeIf]`, `[SerializeField]`)
  - `[FixedEchoStructure]` for compact positional serialization of stable types
  - Support for legacy data through attribute mapping

- **Misc**
  - Battle tested in the Prowl Game Engine
  - Supports both String & Binary formats
  - Mimics Unity's Serializer

## Usage

### Basic Serialization

```csharp
// Serialize an object
var myObject = new MyClass { Value = 42 };
var serialized = Serializer.Serialize(myObject);

// Deserialize back
var deserialized = Serializer.Deserialize<MyClass>(serialized);
```

### Source-Generated Serialization (Recommended)

```csharp
[GenerateSerializer]
public partial class Player
{
    public string Name = "Player";
    public int Health = 100;
    public float Speed = 5.0f;
    public List<string> Inventory = new();
}

// Works exactly like basic serialization — but much faster
var player = new Player { Name = "Hero", Health = 200 };
var serialized = Serializer.Serialize(player);
var deserialized = Serializer.Deserialize<Player>(serialized);
```

The source generator automatically creates optimized `Serialize`/`Deserialize` methods that inline primitive construction and bypass the reflection pipeline entirely.

### Serializing to Text

```csharp
var serialized = Serializer.Serialize(myObject);

// Save to text
string text = serialized.WriteToString();

// Read back
var fromText = EchoObject.ReadFromString(text);
var deserialized = Serializer.Deserialize<MyClass>(fromText);
```

### Custom Serialization

```csharp
public class CustomObject : ISerializable
{
    public int Value = 42;
    public string Text = "Custom";
    public MyClass Obj = new();

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        compound.Add("value", new EchoObject(Value));
        compound.Add("text", new EchoObject(Text));
        compound.Add("obj", Serializer.Serialize(typeof(MyClass), Obj, ctx));
    }

    public void Deserialize(EchoObject tag, SerializationContext ctx)
    {
        Value = tag.Get("value").IntValue;
        Text = tag.Get("text").StringValue;
        Obj = Serializer.Deserialize<MyClass>(tag.Get("obj"), ctx);
    }
}
```

### Working with Collections

```csharp
// Lists
var list = new List<string> { "one", "two", "three" };
var serializedList = Serializer.Serialize(list);

// Dictionaries
var dict = new Dictionary<string, int> {
    { "one", 1 },
    { "two", 2 }
};
var serializedDict = Serializer.Serialize(dict);

// Arrays
var array = new int[] { 1, 2, 3, 4, 5 };
var serializedArray = Serializer.Serialize(array);
```

### Handling Circular References

```csharp
var parent = new CircularObject();
parent.Child = new CircularObject();
parent.Child.Child = parent; // Circular reference
var serialized = Serializer.Serialize(parent);
```

### Using Attributes

```csharp
public class MyClass
{
    [FormerlySerializedAs("oldName")]
    public string NewName = "Test";

    [IgnoreOnNull]
    public string? OptionalField = null;

    [SerializeIf("ShouldSerializeDebug")]
    public string DebugInfo = "";

    public bool ShouldSerializeDebug => false;
}

// FixedEchoStructure tells the serializer this type has a stable shape
// and will never change. This allows compact positional serialization.
[GenerateSerializer]
[FixedEchoStructure]
public partial struct MyVector3
{
    public float X;
    public float Y;
    public float Z;
}
```

## Performance Tips

For maximum serialization speed:
1. Use `[GenerateSerializer]` on your types (requires `partial` class/struct), Or manually implement ISerializable
2. Add `[FixedEchoStructure]` to small, stable value types (like vectors, colors), Or for network packets/messages
3. Use binary format instead of text for I/O-bound workloads

For minimum serialized size:
1. Use `[FixedEchoStructure]` wherever possible (skips field names)
2. Use binary format with size encoding mode

## Limitations

- Properties are not serialized (only fields) this is by design.

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
