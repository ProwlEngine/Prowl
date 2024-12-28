# Prowl.Echo Serializer

A lightweight, flexible serialization system (Built for the Prowl Game Engine). The serializer supports complex object graphs, circular references, and custom serialization behaviors.

Echo does what the name suggests, and create an "Echo" an intermediate representation of the target object.
This allows for fast inspection and modification before converting to Binary or Text.

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
  - Multi-dimensional and jagged arrays
  - Support for custom serializable objects
  - 190+ tests to ensure the library remains stable and reliable!
  - Less than 1k lines of executable code!

- **Flexible Serialization Control**
  - Custom serialization through `ISerializable` interface
  - Attribute-based control (`[FormerlySerializedAs]`, `[IgnoreOnNull]`)
  - Support for legacy data through attribute mapping

- **Misc**
  - Battle Tested in the Prowl Game Engine
  - Supports both String & Binary formats
  - Mimics Unity's Serializer
  - GUID-based Resource Dependency Tracking built right in, No overhead when unused.


## Usage

### Basic Serialization

```csharp
// Serialize an object
var myObject = new MyClass { Value = 42 };
var serialized = Serializer.Serialize(myObject);

// Deserialize back
var deserialized = Serializer.Deserialize<MyClass>(serialized);
```

### Serializating to Text

```csharp
var serialized = Serializer.Serialize(myObject);

// Save to Text
string text = serialized.WriteToString();

// Read to From
var fromText = EchoObject.ReadFromString(text);

var deserialized = Serializer.Deserialize<MyClass>(fromText);
```

### Custom Serialization - Fastest mode Echo has to offer

```csharp
public class CustomObject : ISerializable
{
    public int Value = 42;
    public string Text = "Custom";
	public MyClass Obj = new();

    public EchoObject Serialize(SerializationContext ctx)
    {
        var compound = EchoObject.NewCompound();
        compound.Add("value", new(Value));
        compound.Add("text", new(Text));
        compound.Add("obj", Serializer.Serialize(Obj, ctx));
        return compound;
    }

    public void Deserialize(EchoObject tag, SerializationContext ctx)
    {
        Value = tag.Get("value").IntValue;
        Text = tag.Get("text").StringValue;
		Obj = Serializer.Deserialize(tag.Get("obj"), ctx);
    }
}
```

### Working with Collections

```csharp
// Lists
var list = new List<string> { "one", "two", "three" };
var serializedList = Serializer.Serialize(list);

// Dictionaries
var dict = new Dictionary<MyClass, int> {
    { new("one"), 1 },
    { new("two"), 2 }
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
}

// Fixed Structure attribute tells the serializer this struct is reliable in shape/structure and will never change
// This allows it to skip serializing type names, and only serialize the field values in the order they appear
[FixedEchoStructure]
public struct MyVector3
{
    public float X;
    public float Y;
    public float Z;
}
```

## Limitations
  - Properties are not serialized (only fields)

## Performance

Echo prioritizes simplicity over raw performance.
If your looking for an ultra-fast serializer you may want to consider alternatives.
However, If you're using Echo and need better performance:
  1. Implement `ISerializable` for your critical types - this can significantly outperform the default reflection-based approach
  2. Minimize deep object graphs when possible
  3. Consider using binary format instead of text, as the text format is significantly slower

If size is a concern:
  1. Try to use the FixedEchoStructure attribute wherever possible.
  2. Use binary and set its encoding to Size mode.
  3. 
This should help

Heres is a quick Benchmark I did with BenchmarkDotNet, These were done on a simple Vector3 class.

|      Method |           Serializer |        Mean |     Error |    StdDev | DataSize |
|------------ |--------------------- |------------:|----------:|----------:|---------:|
|   **Serialize** |      **Newtonsoft.Json** | **1,318.56 ns** |  **8.097 ns** |  **7.574 ns** |     **35 B** |
| Deserialize |      Newtonsoft.Json | 2,278.44 ns |  9.244 ns |  8.195 ns |        - |
|   **Serialize** |           **Echo** | **1,489.87 ns** | **17.410 ns** | **15.434 ns** |     **17 B** |
| Deserialize |           Echo | 1,948.34 ns | 65.129 ns | 72.390 ns |        - |
|   **Serialize** |           **Manual** | **68.53 ns** | **0.313 ns** | **0.277 ns** |     **12 B** |
| Deserialize |           Manual | 60.46 ns | 0.429 ns | 0.359 ns |        - |

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
