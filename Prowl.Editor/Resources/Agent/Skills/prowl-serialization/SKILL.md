---
name: prowl-serialization
description: How Prowl saves data with the Echo serializer. Use when deciding which fields of a component or custom asset get saved and shown in the inspector, renaming saved fields or classes, writing custom serialization, storing references between objects, or debugging values that reset or go missing after saving, loading, entering play mode, or copying.
---

# Prowl serialization (Echo)

Scenes, prefabs, assets, play mode, copy and paste, undo and duplicate all go through the Echo serializer. If a value is not serialized, it does not survive any of those.

## What is saved

On any class or struct, including components and custom assets:

- **Saved:** public instance fields, private or protected fields marked `[SerializeField]`, and fields marked `[DataMember]`. Fields of base classes count too.
- **Not saved:** static fields, fields marked `[SerializeIgnore]` or `[NonSerialized]`, and **properties**, unless a property has a getter and a setter and is marked `[DataMember]`. Auto properties are not saved. Prefer a `[SerializeField] private` field behind a property.
- The inspector shows the same fields (except `[DataMember]` ones), minus `[HideInInspector]`.
- Objects are created with their parameterless constructor (it may be private), field initializers run, then saved values are applied. A field missing from the saved data keeps its initializer value. A field saved as null loads as null.
- A field that fails to load is logged and skipped. The rest of the object still loads. A component whose class no longer exists is kept as a missing component, so its data is not lost.

```csharp
using System;
using System.Collections.Generic;
using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

public enum WeaponKind { Pistol, Rifle }

[Serializable]
public class AmmoSettings
{
    public int Magazine = 30;
    public float ReloadSeconds = 1.5f;
}

public class Weapon : MonoBehaviour
{
    public WeaponKind Kind = WeaponKind.Rifle;               // enums are saved
    public AmmoSettings Ammo = new();                        // nested classes are saved field by field
    public List<Float3> Muzzles = new();                     // lists, arrays, dictionaries, hash sets
    public GameObject? Owner;                                // a reference to another object in the scene
    [SerializeField] private int _shotsFired;                // private, but saved
    [SerializeIgnore] public float Heat;                     // public, but not saved
    [FormerlySerializedAs("Damage")] public float BaseDamage = 10f;   // renamed without losing data

    public int ShotsFired => _shotsFired;                   // properties are not saved

    public void Fire() => _shotsFired++;
}
```

## Supported types

Primitives, `string`, `decimal`, enums (saved as numbers, so renaming a member is safe but changing its number is not), `Guid`, `DateTime`, `TimeSpan`, nullable types, tuples, arrays, `List`, `Dictionary` with any key type, `HashSet`, `Queue`, `Stack`, nested classes and structs, and the math types `Float2`, `Float3`, `Quaternion`, `Color`.

- **Polymorphism works.** When the value's real type differs from the field type, the type is saved with it, so a `List<BaseThing>` keeps its subclasses. An interface or abstract field only loads if that type information is there.
- **Shared references and cycles work.** An object referenced twice in one save is written once and both fields point at the same object after loading.
- **Delegates, events, lambdas and `Action` fields are not saved.**

## References

- **Another object in the same scene** (a GameObject, component or Transform field) is saved as a link and comes back pointing at the same object.
- **An asset** in a field is saved as its GUID. Loading resolves it to the shared asset object without loading the content. See the prowl-assets skill for plain fields versus `AssetRef<T>`.
- **A prefab can not hold scene references.** Fields in a prefab that point at objects outside the prefab come back null when it is instantiated. Look such objects up at runtime instead.
- `GameObject.Identifier` and component `Identifier` are restored when a scene loads, but copies and instantiated objects get new ones. Store the object reference, not its Guid.

## Renaming

- Renaming a field drops its saved value unless you add `[FormerlySerializedAs("OldName")]` (repeat it for several old names). Field names also match case insensitively, so a change of case alone is safe.
- Renaming a class needs `[FormerlySerializedAs("OldName")]` on the class (the old short name or the old namespace qualified name both work), or saved components of it become missing.

## Attributes (Prowl.Echo)

| Attribute | Effect |
| --- | --- |
| `[SerializeField]` | save a private or protected field |
| `[SerializeIgnore]` | do not save a field (also `[NonSerialized]`) |
| `[FormerlySerializedAs("old")]` | read the old name, on fields or types |
| `[IgnoreOnNull]` | do not write the field when it is null |
| `[SerializeIf("Member")]` | save only when a bool field, property or method returns true |

## Custom serialization

- **Hooks, keeping normal fields:** implement `ISerializationCallbackReceiver` with `OnBeforeSerialize()` and `OnAfterDeserialize()`. Use it to rebuild caches after loading or to copy live state into saved fields. On a component, override `OnBeforeSerialize` and `OnAfterDeserialize` instead.
- **Full control:** implement `ISerializable` with `Serialize(ref EchoObject compound, SerializationContext ctx)` and `Deserialize(EchoObject value, SerializationContext ctx)`. This replaces field saving completely, so write and read everything yourself.

## When state resets

- **Entering play mode** saves the edit scene and loads a copy. Anything not serialized, such as values set in a private non `[SerializeField]` field from the editor, starts at its initializer value.
- **Script hot reload does not go through serialization.** Live objects move to the new code with every field kept, private ones included. Mark a field `[Prowl.Ember.ReloadIgnore]` to reset it on reload instead.
- **Duplicate, copy and paste, undo** all serialize, so they keep exactly the saved fields.

## Reading and writing Echo data directly

```csharp
using Prowl.Echo;

public static class SaveExample
{
    public static string Save(object value) => Serializer.Serialize(value).WriteToString();

    public static T? Load<T>(string text) => Serializer.Deserialize<T>(EchoObject.ReadFromString(text));
}
```

`WriteToString` uses the Echo text format, which looks like JSON with type suffixes such as `1.5F` and `3L`. `WriteToJson` and `EchoObject.ReadFromJson` give plain JSON. `Serializer.DeserializeInto(data, existing)` loads into an object you already have. `ObjectCopy.Clone(obj)` (in `Prowl.Runtime`) deep copies through serialization. References to objects outside the copy stay pointing at the originals.
