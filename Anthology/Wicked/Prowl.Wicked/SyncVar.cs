using System.Numerics;

namespace Prowl.Wicked;

/// <summary>
/// Determines which clients receive SyncVar updates.
/// </summary>
public enum SyncTarget
{
    /// <summary>All clients observing the entity's map.</summary>
    Observers,

    /// <summary>Only the entity's owner.</summary>
    Owner
}

/// <summary>
/// Internal interface for SyncVar discovery and serialization.
/// </summary>
public interface ISyncVar
{
    /// <summary>Which clients receive updates for this SyncVar.</summary>
    SyncTarget Target { get; }

    /// <summary>True if the value has changed since the last sync.</summary>
    bool IsDirty { get; }

    /// <summary>
    /// Minimum seconds between network syncs. 0 = every tick.
    /// The server only sends a dirty SyncVar once the interval has elapsed.
    /// </summary>
    float SyncInterval { get; }

    /// <summary>Seconds since the last sync was sent.</summary>
    float TimeSinceLastSync { get; set; }

    /// <summary>Marks the SyncVar as clean after syncing.</summary>
    void ClearDirty();

    /// <summary>Writes the current value to the writer.</summary>
    void Serialize(NetworkWriter writer);

    /// <summary>Reads the value from the reader and applies it.</summary>
    void Deserialize(NetworkReader reader);

    /// <summary>Called each client tick for interpolation or other per-frame updates.</summary>
    void ClientUpdate(float dt);
}

/// <summary>
/// A synchronized variable that automatically replicates from server to clients.
/// Declare as a public field on a NetworkEntity. The runtime discovers SyncVars
/// via reflection at spawn time.
/// <para>
/// Usage:
/// <code>
/// public class PlayerEntity : NetworkEntity
/// {
///     public SyncVar&lt;float&gt; HP = new(100f);
///     public SyncVar&lt;int&gt; Gold = new(0, SyncTarget.Owner);
///     public SyncVar&lt;string&gt; Name = new("");
///     public SyncVar&lt;float&gt; X = new(0f) { SyncInterval = 0.05f }; // 20 Hz max
/// }
/// </code>
/// </para>
/// Reading: use <c>.Value</c> or the implicit conversion (<c>float hp = entity.HP;</c>).
/// Writing: set <c>.Value</c> on the server — the change is automatically replicated.
/// Subclass to add custom behavior (e.g., client-side interpolation).
/// </summary>
public class SyncVar<T> : ISyncVar
{
    protected T _value;
    private bool _dirty;
    private Action<T, T>? _onChange;

    public SyncTarget Target { get; }
    public float SyncInterval { get; set; }
    public float TimeSinceLastSync { get; set; }
    public bool IsDirty => _dirty;

    /// <summary>Creates a SyncVar with default(T) and Observers target.</summary>
    public SyncVar() : this(default!, SyncTarget.Observers) { }

    /// <summary>Creates a SyncVar with the given initial value and Observers target.</summary>
    public SyncVar(T initialValue) : this(initialValue, SyncTarget.Observers) { }

    /// <summary>Creates a SyncVar with the given initial value and sync target.</summary>
    public SyncVar(T initialValue, SyncTarget target)
    {
        _value = initialValue;
        Target = target;
        _dirty = false;

        if (!SyncVarSerializer.IsSupported(typeof(T)))
            throw new NotSupportedException(
                $"SyncVar<{typeof(T).Name}> is not supported. " +
                $"Supported types: primitives, string, Guid, Vector2, enums, and INetworkSerializable.");
    }

    /// <summary>
    /// The current value. Setting on the server marks the SyncVar as dirty
    /// and triggers the change callback if registered.
    /// </summary>
    public virtual T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            var old = _value;
            _value = value;
            _dirty = true;
            _onChange?.Invoke(old, value);
        }
    }

    /// <summary>
    /// Registers a callback invoked when the value changes (both server and client side).
    /// Parameters: (oldValue, newValue).
    /// Returns this SyncVar for fluent chaining.
    /// </summary>
    public SyncVar<T> OnChanged(Action<T, T> callback)
    {
        _onChange = callback;
        return this;
    }

    public void ClearDirty()
    {
        _dirty = false;
        TimeSinceLastSync = 0;
    }

    public virtual void Serialize(NetworkWriter writer) =>
        SyncVarSerializer.Write(writer, _value);

    public virtual void Deserialize(NetworkReader reader)
    {
        var old = _value;
        var incoming = SyncVarSerializer.Read<T>(reader);
        OnDeserialize(old, incoming);
    }

    /// <summary>
    /// Called when a new value arrives from the network. Override in subclasses
    /// to implement interpolation or other client-side behavior.
    /// Base implementation sets _value directly and fires the change callback.
    /// </summary>
    protected virtual void OnDeserialize(T oldValue, T newValue)
    {
        _value = newValue;
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
            _onChange?.Invoke(oldValue, newValue);
    }

    /// <summary>
    /// Called each client tick for subclasses that need per-frame updates (e.g., interpolation).
    /// Base implementation does nothing.
    /// </summary>
    public virtual void ClientUpdate(float dt) { }

    /// <summary>Implicit conversion to T for convenient read access.</summary>
    public static implicit operator T(SyncVar<T> syncVar) => syncVar._value;

    /// <summary>
    /// Compares two SyncVars by their underlying values.
    /// Without this, C# uses reference equality for SyncVar == SyncVar comparisons.
    /// </summary>
    public static bool operator ==(SyncVar<T>? left, SyncVar<T>? right)
    {
        if (left is null) return right is null;
        if (right is null) return false;
        return EqualityComparer<T>.Default.Equals(left._value, right._value);
    }

    public static bool operator !=(SyncVar<T>? left, SyncVar<T>? right) => !(left == right);

    public override bool Equals(object? obj) => obj switch
    {
        SyncVar<T> other => EqualityComparer<T>.Default.Equals(_value, other._value),
        T val => EqualityComparer<T>.Default.Equals(_value, val),
        _ => false
    };

    public override int GetHashCode() => _value?.GetHashCode() ?? 0;

    public override string ToString() => _value?.ToString() ?? "null";
}

/// <summary>
/// A SyncVar that interpolates toward the latest server value on the client.
/// Use for frequently-changing numeric values like position, rotation, health bars.
/// On the server, behaves identically to SyncVar&lt;float&gt;.
/// </summary>
public class SyncVarInterpolated : SyncVar<float>
{
    private float _target;
    private float _display;

    /// <summary>Interpolation speed in units per second. Higher = snappier.</summary>
    public float InterpSpeed { get; set; }

    /// <summary>
    /// The smoothed display value. Use this for rendering instead of Value.
    /// On the server, this is always equal to Value.
    /// </summary>
    public float Display => _display;

    public SyncVarInterpolated(float initialValue = 0f, float interpSpeed = 15f, SyncTarget target = SyncTarget.Observers)
        : base(initialValue, target)
    {
        InterpSpeed = interpSpeed;
        _target = initialValue;
        _display = initialValue;
    }

    protected override void OnDeserialize(float oldValue, float newValue)
    {
        base.OnDeserialize(oldValue, newValue);
        _target = newValue;
    }

    public override float Value
    {
        get => _value;
        set
        {
            base.Value = value;
            // On server, keep display in sync
            _target = value;
            _display = value;
        }
    }

    public override void ClientUpdate(float dt)
    {
        if (MathF.Abs(_display - _target) < 0.001f)
        {
            _display = _target;
            return;
        }
        _display = _display + (_target - _display) * MathF.Min(1f, InterpSpeed * dt);
    }
}

/// <summary>
/// A SyncVar that interpolates a Vector2 toward the latest server value on the client.
/// Use for position, velocity, or any 2D vector that changes frequently.
/// </summary>
public class SyncVarInterpolatedVector2 : SyncVar<Vector2>
{
    private Vector2 _target;
    private Vector2 _display;

    /// <summary>Interpolation speed. Higher = snappier.</summary>
    public float InterpSpeed { get; set; }

    /// <summary>
    /// The smoothed display value. Use this for rendering instead of Value.
    /// </summary>
    public Vector2 Display => _display;

    public SyncVarInterpolatedVector2(Vector2 initialValue = default, float interpSpeed = 15f, SyncTarget target = SyncTarget.Observers)
        : base(initialValue, target)
    {
        InterpSpeed = interpSpeed;
        _target = initialValue;
        _display = initialValue;
    }

    public SyncVarInterpolatedVector2(SyncTarget target)
        : this(Vector2.Zero, 15f, target) { }

    protected override void OnDeserialize(Vector2 oldValue, Vector2 newValue)
    {
        base.OnDeserialize(oldValue, newValue);
        _target = newValue;
    }

    public override Vector2 Value
    {
        get => _value;
        set
        {
            base.Value = value;
            _target = value;
            _display = value;
        }
    }

    public override void ClientUpdate(float dt)
    {
        float dx = _target.X - _display.X;
        float dy = _target.Y - _display.Y;
        if (dx * dx + dy * dy < 0.000001f)
        {
            _display = _target;
            return;
        }
        float t = MathF.Min(1f, InterpSpeed * dt);
        _display = new Vector2(
            _display.X + dx * t,
            _display.Y + dy * t);
    }
}

/// <summary>
/// Serializer registry for SyncVar types. Maps types to write/read delegates.
/// </summary>
internal static class SyncVarSerializer
{
    private static readonly Dictionary<Type, Action<NetworkWriter, object>> _writers = new();
    private static readonly Dictionary<Type, Func<NetworkReader, object>> _readers = new();

    static SyncVarSerializer()
    {
        // Primitives
        Register<byte>((w, v) => w.WriteByte(v), r => r.ReadByte());
        Register<sbyte>((w, v) => w.WriteSByte(v), r => r.ReadSByte());
        Register<short>((w, v) => w.WriteShort(v), r => r.ReadShort());
        Register<ushort>((w, v) => w.WriteUShort(v), r => r.ReadUShort());
        Register<int>((w, v) => w.WriteInt(v), r => r.ReadInt());
        Register<uint>((w, v) => w.WriteUInt(v), r => r.ReadUInt());
        Register<long>((w, v) => w.WriteLong(v), r => r.ReadLong());
        Register<ulong>((w, v) => w.WriteULong(v), r => r.ReadULong());
        Register<float>((w, v) => w.WriteFloat(v), r => r.ReadFloat());
        Register<double>((w, v) => w.WriteDouble(v), r => r.ReadDouble());
        Register<bool>((w, v) => w.WriteBool(v), r => r.ReadBool());
        Register<string>((w, v) => w.WriteString(v), r => r.ReadString()!);
        Register<Guid>((w, v) => w.WriteGuid(v), r => r.ReadGuid());
        Register<Vector2>((w, v) => w.WriteVector2(v), r => r.ReadVector2());
    }

    private static void Register<T>(Action<NetworkWriter, T> write, Func<NetworkReader, T> read)
    {
        _writers[typeof(T)] = (w, obj) => write(w, (T)obj);
        _readers[typeof(T)] = r => read(r)!;
    }

    public static bool IsSupported(Type type)
    {
        if (_writers.ContainsKey(type)) return true;
        if (type.IsEnum) return true;
        if (typeof(INetworkSerializable).IsAssignableFrom(type)) return true;
        return false;
    }

    public static void Write<T>(NetworkWriter writer, T value)
    {
        var type = typeof(T);

        if (_writers.TryGetValue(type, out var w))
        {
            w(writer, value!);
            return;
        }

        if (type.IsEnum)
        {
            WriteEnum(writer, type, value!);
            return;
        }

        if (value is INetworkSerializable serializable)
        {
            serializable.Serialize(writer);
            return;
        }

        throw new NotSupportedException($"SyncVar serialization not supported for type {type.Name}");
    }

    public static T Read<T>(NetworkReader reader)
    {
        var type = typeof(T);

        if (_readers.TryGetValue(type, out var r))
            return (T)r(reader);

        if (type.IsEnum)
            return (T)ReadEnum(reader, type);

        if (typeof(INetworkSerializable).IsAssignableFrom(type))
        {
            var value = (INetworkSerializable)Activator.CreateInstance(type)!;
            value.Deserialize(reader);
            return (T)value;
        }

        throw new NotSupportedException($"SyncVar deserialization not supported for type {type.Name}");
    }

    private static void WriteEnum(NetworkWriter writer, Type enumType, object value)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        if (underlying == typeof(int)) writer.WriteInt((int)value);
        else if (underlying == typeof(byte)) writer.WriteByte((byte)value);
        else if (underlying == typeof(sbyte)) writer.WriteSByte((sbyte)value);
        else if (underlying == typeof(short)) writer.WriteShort((short)value);
        else if (underlying == typeof(ushort)) writer.WriteUShort((ushort)value);
        else if (underlying == typeof(uint)) writer.WriteUInt((uint)value);
        else if (underlying == typeof(long)) writer.WriteLong((long)value);
        else if (underlying == typeof(ulong)) writer.WriteULong((ulong)value);
        else throw new NotSupportedException($"Enum underlying type {underlying} is not supported.");
    }

    private static object ReadEnum(NetworkReader reader, Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        if (underlying == typeof(int)) return Enum.ToObject(enumType, reader.ReadInt());
        if (underlying == typeof(byte)) return Enum.ToObject(enumType, reader.ReadByte());
        if (underlying == typeof(sbyte)) return Enum.ToObject(enumType, reader.ReadSByte());
        if (underlying == typeof(short)) return Enum.ToObject(enumType, reader.ReadShort());
        if (underlying == typeof(ushort)) return Enum.ToObject(enumType, reader.ReadUShort());
        if (underlying == typeof(uint)) return Enum.ToObject(enumType, reader.ReadUInt());
        if (underlying == typeof(long)) return Enum.ToObject(enumType, reader.ReadLong());
        if (underlying == typeof(ulong)) return Enum.ToObject(enumType, reader.ReadULong());
        throw new NotSupportedException($"Enum underlying type {underlying} is not supported.");
    }
}
