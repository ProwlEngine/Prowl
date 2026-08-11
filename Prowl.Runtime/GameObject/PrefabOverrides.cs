// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Addressing and writing the values a prefab instance overrides.
/// <para/>
/// An override path names the object by the identifier of the prefab object it came from, then the
/// component the same way, then the member. Both an instance and the prefab source resolve the same
/// path, because both record where each of their objects came from.
/// <para/>
/// This lives in the runtime rather than the editor because a prefab that contains another stores
/// the outer's overrides on the inner, and resolving that happens wherever the prefab is
/// instantiated, which includes a built game.
/// </summary>
public static class PrefabOverrides
{
    public const char PathSeparator = '/';
    public const string GameObjectMarker = "$";

    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// A field or property addressed by an override path. GameObject-level overrides name properties
    /// (Enabled) while component overrides name fields, and writing through a property setter is what
    /// keeps side effects like enable/disable propagation working.
    /// </summary>
    public readonly struct Member
    {
        private readonly FieldInfo? _field;
        private readonly PropertyInfo? _property;

        private Member(FieldInfo field) { _field = field; _property = null; }
        private Member(PropertyInfo property) { _field = null; _property = property; }

        public bool IsValid => _field != null || _property != null;
        public Type MemberType => _field?.FieldType ?? _property!.PropertyType;
        public object? GetValue(object target) => _field != null ? _field.GetValue(target) : _property!.GetValue(target);

        public void SetValue(object target, object? value)
        {
            if (_field != null) _field.SetValue(target, value);
            else if (_property!.CanWrite) _property.SetValue(target, value);
        }

        public static Member Find(object target, string name)
        {
            var field = target.GetType().GetField(name, InstanceMembers);
            if (field != null) return new Member(field);

            var property = target.GetType().GetProperty(name, InstanceMembers);
            return property != null ? new Member(property) : default;
        }
    }

    public static bool TraverseToParent(object target, string[] parts, out object parent)
    {
        parent = target;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var member = Member.Find(parent, parts[i]);
            if (!member.IsValid) return false;

            var next = member.GetValue(parent);
            if (next == null) return false;

            parent = next;
        }
        return true;
    }

    public static Member GetMemberByPath(object target, string memberPath)
    {
        string[] parts = memberPath.Split('.');
        if (!TraverseToParent(target, parts, out var parent)) return default;
        return Member.Find(parent, parts[^1]);
    }

    /// <summary>
    /// Resolve a path against a tree, giving the object holding the member and the remaining member
    /// path.
    /// </summary>
    public static void ParseOverridePath(GameObject root, string path, out object? target, out string memberPath)
    {
        target = null;
        memberPath = "";

        string[] parts = path.Split(PathSeparator, 3);
        if (parts.Length != 3) return;
        if (!Guid.TryParse(parts[0], out Guid goSourceId)) return;

        GameObject? go = FindBySourceIdentifier(root, goSourceId, root.PrefabAssetId);
        if (go == null) return;

        memberPath = parts[2];

        if (parts[1] == GameObjectMarker)
        {
            target = go;
            return;
        }

        if (!Guid.TryParse(parts[1], out Guid componentSourceId)) return;

        foreach (MonoBehaviour component in go.GetComponents<MonoBehaviour>())
        {
            if (go.GetComponentSourceIdentifier(component) != componentSourceId) continue;

            target = component;
            return;
        }
    }

    /// <summary>
    /// The object within one prefab instance that came from a given source object. The search stops at
    /// nested instances of other prefabs, which own their objects and their own overrides.
    /// </summary>
    public static GameObject? FindBySourceIdentifier(GameObject root, Guid sourceId, Guid boundaryPrefabId)
    {
        if (root.SourceIdentifier == sourceId) return root;

        foreach (GameObject child in root.Children)
        {
            if (child.IsPrefabInstance && child.PrefabAssetId != boundaryPrefabId) continue;

            GameObject? found = FindBySourceIdentifier(child, sourceId, boundaryPrefabId);
            if (found != null) return found;
        }

        return null;
    }

    public static void ApplyFieldValue(object target, string memberPath, EchoObject value)
    {
        string[] parts = memberPath.Split('.');
        if (!TraverseToParent(target, parts, out var parent)) return;

        var member = Member.Find(parent, parts[^1]);
        if (!member.IsValid) return;

        // An override value is serialized on its own, detached from any scene graph, so Echo cannot
        // tell that a GameObject or component field points at another scene object rather than at
        // content to copy. Keying the reference by identifier lets it re-link to the live object.
        var context = new SerializationContext { ExternalReferences = new SceneReferenceResolver() };
        object? deserialized = Serializer.Deserialize(value, member.MemberType, context);

        // Null is a valid override for a reference field. Only skip it where the member cannot hold
        // null, which means deserialization failed rather than the value being null.
        bool allowsNull = !member.MemberType.IsValueType || Nullable.GetUnderlyingType(member.MemberType) != null;
        if (deserialized == null && !allowsNull) return;

        member.SetValue(parent, deserialized);

        // Component enabled state is a serialized field, so an override writes it directly and skips
        // the Enabled setter. Re-derive so dispatch registration matches what was just written.
        if (parent is MonoBehaviour behaviour)
            behaviour.HierarchyStateChanged();
    }

    /// <summary>Write a set of overrides onto an instance tree.</summary>
    public static void ApplyTo(GameObject root, List<PropertyOverride> overrides)
    {
        // Collected rather than validated per member, so a component with several overridden members
        // rebuilds its derived state once, after all of them have been written.
        var touched = new HashSet<MonoBehaviour>();

        foreach (PropertyOverride ov in overrides)
        {
            try
            {
                ParseOverridePath(root, ov.Path, out object? target, out string memberPath);
                if (target == null || string.IsNullOrEmpty(memberPath))
                {
                    // Once per path: every instance of the prefab reports the same broken path.
                    Debug.LogWarningOnce($"prefab.path.{ov.Path}",
                        $"[Prefab] Override path '{ov.Path}' no longer resolves on the instance; skipping.");
                    continue;
                }

                // Checked before writing, so a path that now points at a different component type does
                // not silently land on the wrong member.
                if (!GetMemberByPath(target, memberPath).IsValid)
                {
                    Debug.LogWarningOnce($"prefab.member.{ov.Path}",
                        $"[Prefab] Override '{ov.Path}' has no matching field on the current instance; skipping.");
                    continue;
                }

                ApplyFieldValue(target, memberPath, ov.Value);
                if (target is MonoBehaviour behaviour)
                    touched.Add(behaviour);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Prefab] Failed to apply override '{ov.Path}': {ex.Message}");
            }
        }

        // An override writes fields directly, so components deriving state from them would otherwise
        // keep whatever the prefab source had until something else happened to touch them.
        foreach (MonoBehaviour behaviour in touched)
        {
            try
            {
                behaviour.OnValidate();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Prefab] OnValidate threw on {behaviour.GetType().Name}: {ex.Message}");
            }
        }
    }
}
