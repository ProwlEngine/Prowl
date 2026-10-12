// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Ember;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using PropertyID = Prowl.Graphite.PropertyID;
using PropertySet = Prowl.Graphite.PropertySet;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// One setting of a <see cref="VolumeComponent"/>. A volume only changes the settings it overrides, every other one
/// keeps what the volumes under it decided.
/// </summary>
public abstract class VolumeParameter
{
    /// <summary>Whether a volume holding this parameter changes the setting.</summary>
    public bool Override;

    public abstract Type ValueType { get; }

    public abstract object? BoxedValue { get; set; }

    /// <summary>Moves this value toward <paramref name="to"/>'s by <paramref name="t"/>, 0 to 1.</summary>
    public abstract void Interpolate(VolumeParameter to, float t);

    /// <summary>Takes <paramref name="from"/>'s value, leaving the override flag alone.</summary>
    public abstract void CopyValue(VolumeParameter from);
}

/// <summary>
/// A setting of type <typeparamref name="T"/>. Floats, ints, colours and vectors blend linearly, everything else
/// (bools, enums, textures) switches over at half way.
/// </summary>
public class VolumeParameter<T> : VolumeParameter
{
    private static readonly Func<T, T, float, T>? s_lerp = VolumeLerp.For<T>();

    public T Value = default!;

    public VolumeParameter() { }

    public VolumeParameter(T value, bool overridden = false)
    {
        Value = value;
        Override = overridden;
    }

    public override Type ValueType => typeof(T);

    public override object? BoxedValue
    {
        get => Value;
        set => Value = (T)value!;
    }

    /// <summary>Sets the value and overrides it.</summary>
    public void Set(T value)
    {
        Value = value;
        Override = true;
    }

    public virtual T Interpolate(T from, T to, float t) => s_lerp != null ? s_lerp(from, to, t) : t >= 0.5f ? to : from;

    public override void Interpolate(VolumeParameter to, float t)
    {
        if (to is VolumeParameter<T> other) Value = t >= 1f ? other.Value : Interpolate(Value, other.Value, t);
    }

    public override void CopyValue(VolumeParameter from)
    {
        if (from is VolumeParameter<T> other) Value = other.Value;
    }

    public static implicit operator T(VolumeParameter<T> parameter) => parameter.Value;

    public override string ToString() => $"{Value}{(Override ? " (overridden)" : "")}";
}

internal static class VolumeLerp
{
    public static Func<T, T, float, T>? For<T>()
    {
        object? lerp = typeof(T) switch
        {
            Type t when t == typeof(float) => (Func<float, float, float, float>)((a, b, x) => a + (b - a) * x),
            Type t when t == typeof(double) => (Func<double, double, float, double>)((a, b, x) => a + (b - a) * x),
            Type t when t == typeof(int) => (Func<int, int, float, int>)((a, b, x) => (int)MathF.Round(a + (b - a) * x)),
            Type t when t == typeof(Color) => (Func<Color, Color, float, Color>)((a, b, x) => Color.Lerp(a, b, x)),
            Type t when t == typeof(Float2) => (Func<Float2, Float2, float, Float2>)((a, b, x) => Maths.Lerp(a, b, x)),
            Type t when t == typeof(Float3) => (Func<Float3, Float3, float, Float3>)((a, b, x) => Maths.Lerp(a, b, x)),
            Type t when t == typeof(Float4) => (Func<Float4, Float4, float, Float4>)((a, b, x) => Maths.Lerp(a, b, x)),
            _ => null,
        };
        return lerp as Func<T, T, float, T>;
    }
}

/// <summary>Where a <see cref="VolumeComponent"/> sits in the editor's Add Override menu, such as "Image/Bloom".</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class VolumeComponentMenuAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

/// <summary>
/// The settings of one effect a camera draws with, as <see cref="VolumeParameter{T}"/> fields that volumes blend.
/// A component makes its Graphite <see cref="RenderFeature"/> through <see cref="VolumeComponent{TFeature}"/>; each
/// camera keeps its own feature per component type. The values a component is made with are its defaults, which
/// apply where no volume overrides them and should leave the effect off (<see cref="IsActive"/> false).
/// </summary>
public abstract class VolumeComponent
{
    private static readonly ReloadCache<Type, FieldInfo[]> s_parameterFields = new(CollectParameters);

    /// <summary>When false the component is left out of its profile's blending, as if it were removed.</summary>
    public bool Active = true;

    /// <summary>Whether the blended values call for the effect at all, such as a bloom intensity above 0.</summary>
    public virtual bool IsActive => true;

    /// <summary>The parameter fields of a component type, in declaration order.</summary>
    public static FieldInfo[] ParameterFields(Type type) => s_parameterFields[type];

    /// <summary>The component's parameters, in declaration order.</summary>
    public IEnumerable<VolumeParameter> Parameters
    {
        get
        {
            foreach (FieldInfo info in ParameterFields(GetType()))
                if (info.GetValue(this) is VolumeParameter parameter) yield return parameter;
        }
    }

    /// <summary>Sets every parameter's override flag.</summary>
    public void SetAllOverrides(bool overridden)
    {
        foreach (VolumeParameter parameter in Parameters) parameter.Override = overridden;
    }

    /// <summary>The menu path the editor lists the component under.</summary>
    public static string MenuPath(Type type)
        => type.GetCustomAttribute<VolumeComponentMenuAttribute>()?.Path ?? "Custom/" + type.Name;

    /// <summary>Makes the component's feature for a camera. Each camera makes its own.</summary>
    protected internal abstract RenderFeature CreateRenderFeature(Renderer renderer);

    /// <summary>Copies the blended values into the camera's feature, before each frame it draws. Main thread.</summary>
    protected internal abstract void ApplyToFeature(RenderFeature feature, Camera camera);

    private static FieldInfo[] CollectParameters(Type type)
    {
        var chain = new List<Type>();
        for (Type? current = type; current != null && current != typeof(VolumeComponent); current = current.BaseType)
            chain.Add(current);
        chain.Reverse();

        var fields = new List<FieldInfo>();
        foreach (Type declaring in chain)
            foreach (FieldInfo field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.MetadataToken))
                if (typeof(VolumeParameter).IsAssignableFrom(field.FieldType) && !field.IsDefined(typeof(SerializeIgnoreAttribute)))
                    fields.Add(field);
        return [.. fields];
    }
}

/// <summary>A volume component drawn by one Graphite feature of type <typeparamref name="TFeature"/>.</summary>
public abstract class VolumeComponent<TFeature> : VolumeComponent where TFeature : RenderFeature
{
    protected abstract TFeature CreateFeature(Renderer renderer);

    protected abstract void Apply(TFeature feature, Camera camera);

    protected internal sealed override RenderFeature CreateRenderFeature(Renderer renderer) => CreateFeature(renderer);

    protected internal sealed override void ApplyToFeature(RenderFeature feature, Camera camera) => Apply((TFeature)feature, camera);
}

/// <summary>
/// A set of <see cref="VolumeComponent"/>s, at most one of each type, which a <see cref="Volume"/> or a camera's base
/// profile applies. Their order does not matter: features run in the order of their Graphite slot.
/// </summary>
[CreateAssetMenu("Volume Profile", Extension = ".volume", Order = 1210)]
public sealed class VolumeProfile : Asset
{
    public List<VolumeComponent> Components = [];

    public VolumeProfile() : base("Volume Profile") { }

    /// <summary>The components, loading the profile first if it is not loaded.</summary>
    public IReadOnlyList<VolumeComponent> Loaded
    {
        get
        {
            EnsureLoaded();
            return Components;
        }
    }

    public bool Has<T>() where T : VolumeComponent => Has(typeof(T));

    public bool Has(Type type) => Get(type) != null;

    public T? Get<T>() where T : VolumeComponent => (T?)Get(typeof(T));

    public VolumeComponent? Get(Type type)
    {
        foreach (VolumeComponent component in Loaded)
            if (component != null && component.GetType() == type) return component;
        return null;
    }

    public bool TryGet<T>(out T component) where T : VolumeComponent
    {
        component = Get<T>()!;
        return component != null;
    }

    /// <summary>Adds a component of type <typeparamref name="T"/>, or returns the one already there.</summary>
    public T Add<T>(bool overrideAll = false) where T : VolumeComponent => (T)Add(typeof(T), overrideAll);

    /// <inheritdoc cref="Add{T}(bool)"/>
    [UnconditionalSuppressMessage("Trimming", "IL2067:DynamicallyAccessedMembers", Justification = "Volume components keep their parameterless constructor by contract.")]
    public VolumeComponent Add(Type type, bool overrideAll = false)
    {
        if (!typeof(VolumeComponent).IsAssignableFrom(type) || type.IsAbstract)
            throw new ArgumentException($"{type.Name} is not a volume component.", nameof(type));
        VolumeComponent? existing = Get(type);
        if (existing != null) return existing;

        var component = (VolumeComponent)Activator.CreateInstance(type)!;
        if (overrideAll) component.SetAllOverrides(true);
        Components.Add(component);
        return component;
    }

    public bool Remove<T>() where T : VolumeComponent => Remove(typeof(T));

    public bool Remove(Type type)
    {
        VolumeComponent? existing = Get(type);
        return existing != null && Components.Remove(existing);
    }

    /// <summary>A runtime copy of this profile, owned by the caller.</summary>
    public VolumeProfile Clone()
    {
        EnsureLoaded();
        VolumeProfile copy = ObjectCopy.Clone(this);
        copy.Name = Name;
        return copy;
    }
}

/// <summary>
/// Applies a <see cref="VolumeProfile"/> to the cameras that listen to its layer: everywhere when global, otherwise
/// inside its box, fading in over <see cref="BlendDistance"/> outside it. Volumes apply from lowest to highest
/// <see cref="Priority"/>, over the camera's base profile.
/// </summary>
[AddComponentMenu("Rendering/Volume")]
[ExecuteAlways]
public sealed class Volume : Component
{
    private static readonly List<Volume> s_active = [];

    /// <summary>Applies everywhere rather than in its box.</summary>
    public bool IsGlobal = true;

    /// <summary>The box the volume applies in, in the GameObject's local space.</summary>
    [ShowIf(nameof(IsLocal))] public Float3 Size = new(10f, 10f, 10f);

    /// <summary>How far outside the box the volume fades in from nothing, in world units.</summary>
    [ShowIf(nameof(IsLocal))] public float BlendDistance;

    [Range(0f, 1f)] public float Weight = 1f;

    /// <summary>Volumes apply in ascending priority, so the highest has the last word.</summary>
    public float Priority;

    /// <summary>The profile as saved: an asset, or a profile embedded in the scene.</summary>
    public VolumeProfile? SharedProfile;

    [SerializeIgnore, NonSerialized, NotHeld] private VolumeProfile? _instance;

    private bool IsLocal => !IsGlobal;

    /// <summary>The enabled volumes, for the cameras resolving them.</summary>
    internal static IReadOnlyList<Volume> ActiveVolumes => s_active;

    /// <summary>
    /// This volume's own copy of its profile, made from <see cref="SharedProfile"/> on first use so runtime changes
    /// never reach the asset or other volumes.
    /// </summary>
    public VolumeProfile Profile
    {
        get
        {
            if (_instance.IsNotValid())
                _instance = SharedProfile.IsValid() ? SharedProfile.Clone() : new VolumeProfile();
            return _instance;
        }
        set => _instance = value;
    }

    public bool HasInstantiatedProfile => _instance.IsValid();

    /// <summary>The profile blending reads: the instance when one was made, else the shared profile.</summary>
    public VolumeProfile? EffectiveProfile => _instance.IsValid() ? _instance : SharedProfile;

    public override void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    public override void OnDisable() => s_active.Remove(this);

    protected override void OnDispose()
    {
        s_active.Remove(this);
        if (_instance.IsValid() && !_instance.IsFromDatabase) _instance.Dispose();
        _instance = null;
        base.OnDispose();
    }

    /// <summary>How much of the volume reaches a point: 1 inside or when global, falling to 0 at the blend distance.</summary>
    public float Falloff(Float3 point)
    {
        if (IsGlobal) return 1f;
        Float3 local = Transform.InverseTransformPoint(point);
        Float3 half = Maths.Abs(Size) * 0.5f;
        Float3 closest = Maths.Clamp(local, -half, half);
        if (closest == local) return 1f;

        float distance = Float3.Distance(Transform.TransformPoint(closest), point);
        float blend = MathF.Max(BlendDistance, 0f);
        return blend <= 0f || distance >= blend ? 0f : 1f - distance / blend;
    }

    public override void DrawGizmos()
    {
        if (IsGlobal) return;
        DrawBox(Maths.Abs(Size) * 0.5f, 0f, new Color(0.35f, 0.85f, 0.45f, 1f));
    }

    public override void DrawGizmosSelected()
    {
        if (IsGlobal || BlendDistance <= 0f) return;
        DrawBox(Maths.Abs(Size) * 0.5f, BlendDistance, new Color(0.35f, 0.85f, 0.45f, 0.4f));
    }

    // The box in local space, grown by a world distance along each local axis
    private void DrawBox(Float3 half, float grow, Color color)
    {
        Float3 scale = Maths.Max(Maths.Abs(Transform.LossyScale), new Float3(1e-4f));
        Float3 extent = half + new Float3(grow / scale.X, grow / scale.Y, grow / scale.Z);
        Span<Float3> c = stackalloc Float3[8];
        for (int i = 0; i < 8; i++)
            c[i] = Transform.TransformPoint(new Float3((i & 1) != 0 ? extent.X : -extent.X, (i & 2) != 0 ? extent.Y : -extent.Y, (i & 4) != 0 ? extent.Z : -extent.Z));
        for (int i = 0; i < 8; i++)
            for (int axis = 1; axis < 8; axis <<= 1)
                if ((i & axis) == 0)
                    Debug.DrawLine(c[i], c[i | axis], color);
    }
}

/// <summary>
/// A camera's blended volume settings: one component per type, from the type's defaults, then the camera's base
/// profile, then each volume it listens to, and the Graphite features those values drive. Features belong to the
/// camera and are never shared. A component whose values turn it off keeps its feature for a short while
/// (<see cref="DeactivationFrames"/>), so a fade through zero does not rebuild the view each frame.
/// </summary>
public sealed class VolumeStack : IDisposable
{
    /// <summary>Resolves an effect stays on for after its values turned it off.</summary>
    public const int DeactivationFrames = 30;

    private static readonly ReloadCache<Type, VolumeComponent> s_defaults = new(type => (VolumeComponent)Activator.CreateInstance(type)!);

    private sealed class Entry(VolumeComponent blended, FieldInfo[] fields)
    {
        public readonly VolumeComponent Blended = blended;
        public readonly FieldInfo[] Fields = fields;
        public bool Present;
        public bool Active;
        public int InactiveFrames;
        public RenderFeature? Feature;
        public Renderer? FeatureRenderer;
    }

    private readonly Dictionary<Type, Entry> _entries = [];
    private readonly List<Entry> _order = [];
    private readonly List<(Volume Volume, VolumeProfile Profile, float Weight)> _contributing = [];

    /// <summary>The blended component of type <typeparamref name="T"/> from the last resolve, null when no profile has one.</summary>
    public T? Get<T>() where T : VolumeComponent
        => _entries.TryGetValue(typeof(T), out Entry? entry) && (entry.Present || entry.Active) ? (T)entry.Blended : null;

    /// <summary>Whether the effect of type <typeparamref name="T"/> runs after the last resolve.</summary>
    public bool IsActive<T>() where T : VolumeComponent => IsActive(typeof(T));

    public bool IsActive(Type type) => _entries.TryGetValue(type, out Entry? entry) && entry.Active;

    /// <summary>The component types whose effects run after the last resolve.</summary>
    public IEnumerable<Type> ActiveTypes
    {
        get
        {
            foreach (Entry entry in _order)
                if (entry.Active) yield return entry.Blended.GetType();
        }
    }

    /// <summary>
    /// Blends the volumes of <paramref name="scene"/> on the layers of <paramref name="mask"/> at
    /// <paramref name="position"/>, over <paramref name="baseProfile"/>.
    /// </summary>
    public void Resolve(Scene? scene, Float3 position, LayerMask mask, VolumeProfile? baseProfile)
    {
        foreach (Entry entry in _order) entry.Present = false;

        if (baseProfile.IsValid())
            MarkPresent(baseProfile);

        _contributing.Clear();
        IReadOnlyList<Volume> volumes = Volume.ActiveVolumes;
        for (int i = 0; i < volumes.Count; i++)
        {
            Volume volume = volumes[i];
            if (volume.IsNotValid() || !volume.EnabledInHierarchy || !ReferenceEquals(volume.GameObject.Scene, scene)) continue;
            if (!mask.HasLayer(volume.GameObject.LayerIndex)) continue;
            VolumeProfile? profile = volume.EffectiveProfile;
            if (profile.IsNotValid()) continue;
            float weight = Maths.Clamp(volume.Weight, 0f, 1f);
            if (weight <= 0f) continue;
            weight *= volume.Falloff(position);
            if (weight <= 0f) continue;
            _contributing.Add((volume, profile, weight));
            MarkPresent(profile);
        }
        // Stable, so equal priorities keep the order the volumes were enabled in
        List<(Volume Volume, VolumeProfile Profile, float Weight)> sorted = [.. _contributing.OrderBy(c => c.Volume.Priority)];

        foreach (Entry entry in _order)
        {
            if (!entry.Present && !entry.Active) continue;
            VolumeComponent defaults = s_defaults[entry.Blended.GetType()];
            foreach (FieldInfo field in entry.Fields)
            {
                var target = (VolumeParameter)field.GetValue(entry.Blended)!;
                if (field.GetValue(defaults) is VolumeParameter source) target.CopyValue(source);
                target.Override = false;
            }
        }

        if (baseProfile.IsValid())
            Apply(baseProfile, 1f);
        foreach ((_, VolumeProfile profile, float weight) in sorted)
            Apply(profile, weight);

        foreach (Entry entry in _order)
        {
            if (entry.Present && IsActiveSafe(entry.Blended))
            {
                entry.Active = true;
                entry.InactiveFrames = 0;
            }
            else if (entry.Active && ++entry.InactiveFrames > DeactivationFrames)
            {
                entry.Active = false;
                entry.InactiveFrames = 0;
            }
        }
    }

    private static bool IsActiveSafe(VolumeComponent component)
    {
        try { return component.IsActive; }
        catch (Exception e)
        {
            Debug.LogErrorOnce($"VolumeComponent.IsActive.{component.GetType().Name}", $"{component.GetType().Name}.IsActive threw: {e}");
            return false;
        }
    }

    private void MarkPresent(VolumeProfile profile)
    {
        foreach (VolumeComponent component in profile.Loaded)
            if (component != null && component.Active) GetEntry(component.GetType()).Present = true;
    }

    private void Apply(VolumeProfile profile, float weight)
    {
        foreach (VolumeComponent component in profile.Loaded)
        {
            if (component == null || !component.Active) continue;
            Entry entry = GetEntry(component.GetType());
            foreach (FieldInfo field in entry.Fields)
            {
                if (field.GetValue(component) is not VolumeParameter from || !from.Override) continue;
                var target = (VolumeParameter)field.GetValue(entry.Blended)!;
                target.Interpolate(from, weight);
                target.Override = true;
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067:DynamicallyAccessedMembers", Justification = "Volume components keep their parameterless constructor by contract.")]
    private Entry GetEntry(Type type)
    {
        if (!_entries.TryGetValue(type, out Entry? entry))
        {
            var blended = (VolumeComponent)Activator.CreateInstance(type)!;
            entry = new Entry(blended, VolumeComponent.ParameterFields(type));
            _entries[type] = entry;
            _order.Add(entry);
        }
        return entry;
    }

    /// <summary>Adds the features of the active components, configured for <paramref name="camera"/>. Main thread.</summary>
    internal void CollectFeatures(Renderer renderer, Camera camera, List<RenderFeature> features)
    {
        foreach (Entry entry in _order)
        {
            if (!entry.Active) continue;
            VolumeComponent component = entry.Blended;
            try
            {
                if (entry.Feature == null || entry.FeatureRenderer != renderer)
                {
                    ReleaseFeature(entry);
                    entry.Feature = component.CreateRenderFeature(renderer);
                    entry.FeatureRenderer = renderer;
                }
                component.ApplyToFeature(entry.Feature, camera);
                features.Add(entry.Feature);
            }
            catch (Exception e)
            {
                Debug.LogErrorOnce($"VolumeComponent.{component.GetType().Name}", $"{component.GetType().Name} could not set up its feature: {e}");
            }
        }
    }

    private static void ReleaseFeature(Entry entry)
    {
        if (entry.Feature is IDisposable disposable) Graphics.DeferDispose(disposable);
        entry.Feature = null;
        entry.FeatureRenderer = null;
    }

    /// <summary>Frees every feature and forgets every component, for a camera going away or a script reload.</summary>
    public void Dispose()
    {
        foreach (Entry entry in _order) ReleaseFeature(entry);
        _entries.Clear();
        _order.Clear();
    }
}

/// <summary>When a <see cref="PostEffect"/> runs in the camera's frame.</summary>
public enum PostEffectStage
{
    /// <summary>After the opaques and sky, before the transparents, in linear light.</summary>
    AfterOpaques,

    /// <summary>After the transparents and before tonemapping, in linear light, as bloom and depth of field are.</summary>
    BeforeTonemap,

    /// <summary>On the colour as it is displayed, after tonemapping.</summary>
    AfterTonemap,
}

/// <summary>
/// A post effect written as one fragment shader, the simplest kind there is. Each <see cref="VolumeParameter{T}"/>
/// field reaches the shader as a uniform named after it with an underscore, Amount as _Amount: floats, ints and
/// enums as themselves, bools as a float 0 or 1, colours and Float2 to Float4 as vectors, Texture2D as a sampler2D.
/// The shader reads the colour with Graphite_SampleSource(vUV) and writes outColor.
/// <code>
/// [VolumeComponentMenu("Custom/Invert")]
/// public sealed class Invert : PostEffect
/// {
///     public VolumeParameter&lt;float&gt; Amount = new(0f);
///     public override bool IsActive => Amount.Value > 0f;
///     protected override string Shader => """
///         uniform float _Amount;
///         void main()
///         {
///             vec4 c = Graphite_SampleSource(vUV);
///             outColor = vec4(mix(c.rgb, 1.0 - c.rgb, _Amount), c.a);
///         }
///         """;
/// }
/// </code>
/// Effects needing several passes or textures of their own derive from <see cref="VolumeComponent{TFeature}"/> instead.
/// </summary>
public abstract class PostEffect : VolumeComponent<PostEffectFeature>
{
    /// <summary>The fragment shader, after Graphite/Fullscreen.glsl.</summary>
    protected abstract string Shader { get; }

    /// <summary>When the effect runs.</summary>
    protected virtual PostEffectStage Stage => PostEffectStage.AfterTonemap;

    /// <summary>Effects in the same stage run lowest first.</summary>
    protected virtual int Order => 0;

    /// <summary>Binds the scene's depth as _CameraDepthTexture, reversed: 1 at the near plane falling toward 0.</summary>
    protected virtual bool ReadsDepth => false;

    protected sealed override PostEffectFeature CreateFeature(Renderer renderer)
    {
        RenderSlot slot = Stage switch
        {
            PostEffectStage.AfterOpaques => RenderSlot.AfterOpaques,
            PostEffectStage.BeforeTonemap => RenderSlot.HdrPost,
            _ => RenderSlot.DisplayPost,
        };
        return new PostEffectFeature(renderer.CreateFullscreenProgram(Shader, GetType().Name), slot, Order, ReadsDepth, ParameterFields(GetType()));
    }

    protected sealed override void Apply(PostEffectFeature feature, Camera camera) => feature.Capture(this);
}

/// <summary>The feature drawing a <see cref="PostEffect"/>'s shader with its parameters.</summary>
public sealed class PostEffectFeature : RenderFeature, IDisposable
{
    private readonly FullscreenProgram _program;
    private readonly RenderSlot _slot;
    private readonly int _order;
    private readonly bool _depth;
    private readonly FieldInfo[] _fields;
    private readonly PropertyID[] _names;
    private readonly object?[] _pending, _values;
    private readonly PropertySet _properties = new();

    internal PostEffectFeature(FullscreenProgram program, RenderSlot slot, int order, bool depth, FieldInfo[] fields)
    {
        (_program, _slot, _order, _depth, _fields) = (program, slot, order, depth, fields);
        _names = [.. fields.Select(f => (PropertyID)("_" + f.Name))];
        _pending = new object?[fields.Length];
        _values = new object?[fields.Length];
    }

    public override RenderSlot Slot => _slot;
    public override int Order => _order;

    internal void Capture(PostEffect effect)
    {
        for (int i = 0; i < _fields.Length; i++)
            _pending[i] = (_fields[i].GetValue(effect) as VolumeParameter)?.BoxedValue switch
            {
                Texture2D texture => texture.GpuIfLoaded is { } gpu ? (gpu, Graphics.GetSampler(texture.SamplerUnchecked)) : null,
                object value => value,
                null => null,
            };
    }

    public override void Extract() => Array.Copy(_pending, _values, _values.Length);

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        if (_depth) builder.ReadScene();
        builder.WriteColor();
    }

    public override void Render(FeatureContext context)
    {
        for (int i = 0; i < _values.Length; i++)
        {
            PropertyID name = _names[i];
            switch (_values[i])
            {
                case float v: _properties.SetFloat(name, v); break;
                case int v: _properties.SetInt(name, v); break;
                case bool v: _properties.SetFloat(name, v ? 1f : 0f); break;
                case Enum v: _properties.SetInt(name, Convert.ToInt32(v)); break;
                case Color v: _properties.SetFloat4(name, v.ToFloat4()); break;
                case Float2 v: _properties.SetFloat2(name, v); break;
                case Float3 v: _properties.SetFloat3(name, v); break;
                case Float4 v: _properties.SetFloat4(name, v); break;
                case ValueTuple<Graphite.Texture, Graphite.Sampler> t: _properties.SetTexture(name, t.Item1, t.Item2); break;
            }
        }
        if (_depth) _properties.SetTexture("_CameraDepthTexture", context.Depth!);
        context.Fullscreen(_program, _properties);
    }

    public void Dispose() => _program.Dispose();
}
