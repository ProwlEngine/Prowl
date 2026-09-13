// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Echo;
using Prowl.Runtime.Audio.Effects;

namespace Prowl.Runtime.Audio;

/// <summary>One parameter of a mixer, as a snapshot recorded it.</summary>
/// <remarks>
/// The path names the bus by its identity rather than its position or its name, so a snapshot keeps
/// pointing at the same bus after a rename or a reorder, the same way a source's output reference does.
/// </remarks>
public sealed class AudioMixerSnapshotValue
{
    public string Path = string.Empty;
    public float Value;
}

/// <summary>
/// A named recording of every level, mute and effect parameter in a mixer, which the mixer can be
/// moved back to over a duration.
/// </summary>
/// <remarks>
/// This is what a change of mix is: going underwater, opening a pause menu, walking into a cave. The
/// alternative is a script that knows every bus and every effect parameter it might want to move and
/// eases each one by hand, which has to be revisited every time the mixer gains anything.
/// </remarks>
public sealed class AudioMixerSnapshot
{
    [SerializeField]
    private string _name = "Snapshot";

    [SerializeField]
    private List<AudioMixerSnapshotValue> _values = [];

    public string Name
    {
        get => _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? "Snapshot" : value;
    }

    /// <summary>What this snapshot holds, one entry per parameter it recorded.</summary>
    public IReadOnlyList<AudioMixerSnapshotValue> Values => _values;

    /// <summary>Records the mixer as it is now, replacing whatever this snapshot held.</summary>
    public void CaptureFrom(AudioMixer mixer)
    {
        if (mixer.IsNotValid()) return;

        _values.Clear();

        MixerParameters.Walk(mixer, (path, current, _) =>
            _values.Add(new AudioMixerSnapshotValue { Path = path, Value = current }));
    }

    /// <summary>The value recorded for <paramref name="path"/>, or null when this snapshot has none.</summary>
    public float? Find(string path)
    {
        foreach (AudioMixerSnapshotValue value in _values)
            if (value.Path == path) return value.Value;

        return null;
    }
}

/// <summary>
/// Walks everything in a mixer that a snapshot can hold, handing each one out as a path, the value it
/// has now, and a way to set it.
/// </summary>
/// <remarks>
/// One walk serves all three things that need it: recording a snapshot reads the value, applying one
/// calls the setter, and starting a transition keeps both so it can move between them. Written as a
/// visit rather than a list of parameter objects so none of the three has to allocate one per
/// parameter just to read it.
/// </remarks>
internal static class MixerParameters
{
    internal const string VolumeKey = "Volume";
    internal const string MuteKey = "Mute";

    /// <summary>
    /// Anything not a number is left out. A filter's type or a clip reference has no midpoint, and a
    /// snapshot that snapped one of those part way through a transition would be a glitch rather than
    /// a mix change. Flags do have a midpoint in practice, so they cross at the half way mark.
    /// </summary>
    private static readonly Dictionary<Type, FieldInfo[]> s_parameters = [];

    internal static void Walk(AudioMixer mixer, Action<string, float, Action<float>> visit)
    {
        foreach (AudioMixerGroup group in mixer.Groups)
        {
            if (group.IsNotValid()) continue;

            AudioMixerGroup bus = group;
            string prefix = bus.Identity;

            visit($"{prefix}/{VolumeKey}", bus.VolumeDB, v => bus.VolumeDB = v);
            visit($"{prefix}/{MuteKey}", bus.Mute ? 1.0f : 0.0f, v => bus.Mute = v >= 0.5f);

            IReadOnlyList<AudioEffect> effects = bus.Effects;

            for (int i = 0; i < effects.Count; i++)
            {
                AudioEffect effect = effects[i];

                if (effect == null) continue;

                // Indexed rather than named: two of the same effect on one bus is ordinary, and the
                // order is what tells them apart. Reordering a chain moves what a snapshot addresses,
                // which is the same thing that happens to the audio.
                string effectPrefix = $"{prefix}/fx{i}";

                foreach (FieldInfo field in Parameters(effect.GetType()))
                {
                    AudioEffect target = effect;
                    bool flag = field.FieldType == typeof(bool);

                    float current = flag
                        ? ((bool)field.GetValue(effect)! ? 1.0f : 0.0f)
                        : (float)field.GetValue(effect)!;

                    visit($"{effectPrefix}/{field.Name}", current, v =>
                    {
                        field.SetValue(target, flag ? v >= 0.5f : v);
                        target.OnValidate();
                    });
                }
            }
        }
    }

    /// <summary>The fields of an effect a snapshot can move, worked out once per type.</summary>
    private static FieldInfo[] Parameters(Type type)
    {
        if (s_parameters.TryGetValue(type, out FieldInfo[] cached)) return cached;

        var fields = new List<FieldInfo>();

        for (Type at = type; at != null && at != typeof(object); at = at.BaseType)
        {
            foreach (FieldInfo field in at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(float) && field.FieldType != typeof(bool)) continue;

                // The same rule the serializer uses, so a snapshot holds what the asset holds.
                bool saved = field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null;

                if (!saved || field.GetCustomAttribute<SerializeIgnoreAttribute>() != null) continue;

                fields.Add(field);
            }
        }

        FieldInfo[] result = fields.ToArray();
        s_parameters[type] = result;
        return result;
    }
}
