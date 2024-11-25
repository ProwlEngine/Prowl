# Prowl.Echo Serializer

A lightweight, flexible serialization system (Built for the Prowl Game Engine). The serializer supports complex object graphs, circular references, and custom serialization behaviors.

Echo does what the name suggests, and create an "Echo" an intermediate representation of the target object.
This allows for fast inspection and modification before converting to Binary or Text.

## Features

- **Type Support**
  - Primitives (int, float, double, string, bool, etc.)
  - Complex objects and nested types
  - Collections (List, Array, HashSet)
  - Dictionaries
  - Enums
  - DateTime and Guid
  - Nullable types
  - Circular references
  - Multi-dimensional and jagged arrays
  - Support for custom serializable objects

- **Flexible Serialization Control**
  - Custom serialization through `ISerializable` interface
  - Attribute-based control (`[FormerlySerializedAs]`, `[IgnoreOnNull]`)
  - Support for legacy data through attribute mapping

- **Misc**
  - Battle Tested in the Prowl Game Engine
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

### Serializating to Text

```csharp
var serialized = Serializer.Serialize(myObject);

// Save to Text
string text = StringTagConverter.Write(serialized);

// Read to From
var fromText = StringTagConverter.Read(text);

var deserialized = Serializer.Deserialize<MyClass>(fromText);
```

### Custom Serialization

```csharp
public class CustomObject : ISerializable
{
    public int Value = 42;
    public string Text = "Custom";

    public SerializedProperty Serialize(SerializationContext ctx)
    {
        var compound = SerializedProperty.NewCompound();
        compound.Add("customValue", new SerializedProperty(PropertyType.Int, Value));
        compound.Add("customText", new SerializedProperty(PropertyType.String, Text));
        return compound;
    }

    public void Deserialize(SerializedProperty tag, SerializationContext ctx)
    {
        Value = tag.Get("customValue").IntValue;
        Text = tag.Get("customText").StringValue;
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
}
```

## Limitations
  - Does not serialize Properties
  - No benchmarks exist but performance is expected to be lacking

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.