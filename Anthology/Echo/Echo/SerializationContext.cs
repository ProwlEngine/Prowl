// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public enum TypeMode 
{
    /// <summary> Always include type information. </summary>
    Aggressive,
    /// <summary> Include type information only when necessary (When the type is not the expected type) </summary>
    Auto,
    /// <summary> Never include type information. (This may cause deserialization to fail if the type is not the expected type) </summary>
    None
}

/// <summary>
/// Called during serialization to optionally override how an object is serialized.
/// Return a non-null EchoObject to use that instead of normal serialization.
/// Return null to proceed with normal serialization.
/// </summary>
public delegate EchoObject? SerializeOverride(object value, SerializationContext context);

/// <summary>
/// Called during deserialization to optionally override how an EchoObject is deserialized.
/// Return (true, result) to use the result instead of normal deserialization.
/// Return (false, null) to proceed with normal deserialization.
/// </summary>
public delegate (bool handled, object? result) DeserializeOverride(EchoObject data, Type targetType, SerializationContext context);

public class SerializationContext
{
    private class NullKey { }

    internal TypeMode TypeMode = TypeMode.Auto;

    public Dictionary<object, int> objectToId = new(ReferenceEqualityComparer.Instance);
    public Dictionary<int, object> idToObject = new();
    public int nextId = 1;

    /// <summary>
    /// Reference ids whose full definition has already been deserialized. Lets the deserializer tell a
    /// forward-reference placeholder (still awaiting its definition) apart from an already-populated
    /// instance, and detect a duplicate definition for the same id.
    /// </summary>
    public HashSet<int> fullyDefinedIds = new();

    private List<Action>? _deferredActions;
    private int _deserializeDepth;
    private bool _runningDeferred;

    /// <summary>
    /// Queues work to run after the whole object graph has finished deserializing (once every reference
    /// placeholder exists with its correct type). Used to recover data whose definition was serialized
    /// inline somewhere that couldn't be deserialized in the normal pass (e.g. inside a missing component).
    /// Runs automatically when the outermost Deserialize/DeserializeInto call using this context returns.
    /// </summary>
    public void Defer(Action action) => (_deferredActions ??= new()).Add(action);

    // Called by the deserializer around every call so deferred actions can fire once the OUTERMOST call
    // (whichever overload, whatever context) unwinds - not per nested field, and not re-entrantly while
    // deferred actions are themselves deserializing.
    internal void EnterDeserialize() => _deserializeDepth++;
    internal void ExitDeserialize()
    {
        if (--_deserializeDepth == 0)
            RunDeferredActions();
    }

    /// <summary>Runs (and drains) all deferred actions, including any queued while running earlier ones.</summary>
    public void RunDeferredActions()
    {
        if (_runningDeferred) return;
        _runningDeferred = true;
        try
        {
            while (_deferredActions is { Count: > 0 })
            {
                List<Action> batch = _deferredActions;
                _deferredActions = null;
                foreach (Action action in batch)
                    action();
            }
        }
        finally { _runningDeferred = false; }
    }

    /// <summary>
    /// Optional override for serialization. When set, this is called before normal serialization.
    /// If it returns a non-null EchoObject, that is used as the serialized data (type wrapping still applies).
    /// If it returns null, normal serialization proceeds.
    /// </summary>
    public SerializeOverride? OnSerialize { get; set; }

    /// <summary>
    /// Optional override for deserialization. When set, this is called before normal deserialization.
    /// If it returns (true, result), the result is used directly.
    /// If it returns (false, null), normal deserialization proceeds.
    /// </summary>
    public DeserializeOverride? OnDeserialize { get; set; }

    public SerializationContext(TypeMode typeMode = TypeMode.Auto)
    {
        TypeMode = typeMode;
        //objectToId.Clear(); // Not sure why we cleared these?
        objectToId.Add(new NullKey(), 0);
        //idToObject.Clear();
        idToObject.Add(0, new NullKey());
        nextId = 1;
    }
}
