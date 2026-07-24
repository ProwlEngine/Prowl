// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Core;

/// <summary>
/// Handles copy / paste-as-new / paste-values for individual components, using Echo serialization.
/// The payload lives on the system clipboard as text so it survives a scene swap - which matters,
/// because <see cref="Prowl.Editor.GUI.SceneView.PrefabEditingMode"/> discards and reloads the whole
/// scene on enter/exit. That's what makes "copy in prefab mode, paste into the scene" work.
///
/// Payload format (first line is a header so the type can be peeked without parsing the body):
/// <code>
/// ProwlComponent:&lt;AssemblyQualifiedName&gt;
/// { Data = {...}, Refs = {...} }
/// </code>
///
/// Scene-object references (fields typed GameObject / MonoBehaviour / Transform) are NOT serialized
/// by value - Echo would deep-clone the target and paste would produce a detached orphan. They're
/// nulled in Data and recorded in Refs as identifier Guids, then re-resolved against the current
/// scene on paste. Unresolvable (different scene, deleted object, cross-project) simply stays null,
/// which mirrors Unity. Identifiers survive the prefab-mode round trip because
/// <c>Scene.OnBeforeSerialize/OnAfterDeserialize</c> capture and restore them.
/// </summary>
public static class ComponentClipboard
{
    private const string ClipboardHeader = "ProwlComponent:";

    // Reference-kind prefixes used in the Refs compound.
    private const string RefGameObject = "go:";
    private const string RefComponent = "c:";
    private const string RefTransform = "t:";

    // ================================================================
    //  Copy
    // ================================================================

    /// <summary>Serialize a component onto the system clipboard.</summary>
    public static void Copy(MonoBehaviour comp)
    {
        if (comp == null) return;

        var (data, refs) = CaptureState(comp);

        var root = EchoObject.NewCompound();
        root.Add("Data", data);
        if (refs.Count > 0)
        {
            var refsTag = EchoObject.NewCompound();
            foreach (var (field, token) in refs)
                refsTag.Add(field, new EchoObject(token));
            root.Add("Refs", refsTag);
        }

        Input.Clipboard = $"{ClipboardHeader}{comp.GetType().AssemblyQualifiedName}\n{root.WriteToString()}";
    }

    // ================================================================
    //  Clipboard inspection
    // ================================================================

    /// <summary>
    /// Split the clipboard into its type-name header and Echo body. False when the clipboard holds
    /// anything that isn't a component payload.
    /// </summary>
    private static bool TryParseHeader(out string typeName, out string body)
    {
        typeName = "";
        body = "";

        string text = Input.Clipboard;
        if (string.IsNullOrEmpty(text) || !text.StartsWith(ClipboardHeader)) return false;

        int newline = text.IndexOf('\n');
        if (newline < 0) return false;

        typeName = text[ClipboardHeader.Length..newline].Trim();
        body = text[(newline + 1)..];
        return typeName.Length > 0;
    }

    /// <summary>
    /// The component type currently on the clipboard, or null if the clipboard holds something else
    /// or the type can't be resolved in this project. Cheap enough to call from menu-build code.
    /// </summary>
    public static Type? PeekType()
    {
        // ResolveType searches every load context (so it finds user scripts in the collectible one)
        // and honors the recorded assembly - unlike FindType, whose loose name match could bind a
        // same-named type from the wrong assembly and paste the wrong component.
        return TryParseHeader(out string typeName, out _) ? RuntimeUtils.ResolveType(typeName) : null;
    }

    /// <summary>True if the clipboard holds a component that can be pasted as a new component.</summary>
    public static bool CanPasteAsNew() => PeekType() != null;

    /// <summary>True if the clipboard holds a component of exactly <paramref name="type"/>.</summary>
    public static bool CanPasteValues(Type type) => PeekType() == type;

    // ================================================================
    //  Paste
    // ================================================================

    /// <summary>
    /// Add the clipboard component to <paramref name="go"/> as a new component, with undo.
    /// Returns the new component, or null if the clipboard is empty/unusable.
    /// </summary>
    public static MonoBehaviour? PasteAsNew(GameObject go)
    {
        if (go == null) return null;

        try
        {
            if (!TryReadClipboard(out Type? type, out EchoObject? data, out var refs) || type == null || data == null)
                return null;

            var comp = Deserialize(data, type);
            if (comp == null) return null;

            ResolveRefs(comp, refs);

            // AddComponent(instance) attaches, registers with the scene and fires OnAddedToScene /
            // OnEnable, so the pasted component is live immediately.
            go.AddComponent(comp);
            comp.OnValidate();

            // Capture only strings/echo in the undo closures - no Type, no live references - so an
            // undo entry can't pin the collectible script load context. Re-resolve from the name on
            // redo. (AssemblyQualifiedName is non-null for any concrete type; FullName won't do, as
            // ResolveType needs the qualifier to reach user scripts.)
            var goId = go.Identifier;
            var compId = comp.Identifier;
            string typeName = type.AssemblyQualifiedName!;
            var capturedData = data;
            var capturedRefs = refs;

            Undo.RegisterAction("Paste Component",
                undo: () =>
                {
                    var g = Undo.FindGO(goId);
                    var c = g?.GetComponentByIdentifier(compId);
                    if (g != null && c != null) g.RemoveComponent(c);
                },
                redo: () =>
                {
                    var g = Undo.FindGO(goId);
                    if (g == null) return;
                    var t = RuntimeUtils.ResolveType(typeName);
                    if (t == null) return;
                    var restored = Deserialize(capturedData, t);
                    if (restored == null) return;
                    restored.Identifier = compId;
                    ResolveRefs(restored, capturedRefs);
                    g.AddComponent(restored);
                    restored.OnValidate();
                });

            EditorSceneManager.MarkDirty();
            return comp;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to paste component: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Overwrite <paramref name="target"/>'s field values from the clipboard, with undo. The target
    /// keeps its own identifier, GameObject and sibling index - only data is replaced. Requires the
    /// clipboard type to match exactly.
    /// </summary>
    public static bool PasteValues(MonoBehaviour target)
    {
        if (target == null) return false;

        try
        {
            if (!TryReadClipboard(out Type? type, out EchoObject? data, out var refs) || type == null || data == null)
                return false;
            if (type != target.GetType()) return false;

            // Snapshot via CaptureState (not a plain Serialize) so the undo state stores scene
            // references as identifiers too, otherwise undo would restore deep-cloned orphans.
            var (beforeData, beforeRefs) = CaptureState(target);

            ApplyState(target, data, refs);

            var compId = target.Identifier;
            var afterData = data;
            var afterRefs = refs;

            Undo.RegisterAction("Paste Component Values",
                undo: () => { var c = Undo.FindComponent(compId); if (c != null) ApplyState(c, beforeData, beforeRefs); },
                redo: () => { var c = Undo.FindComponent(compId); if (c != null) ApplyState(c, afterData, afterRefs); });

            EditorSceneManager.MarkDirty();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to paste component values: {ex.Message}");
            return false;
        }
    }

    // ================================================================
    //  State capture / apply
    // ================================================================

    /// <summary>
    /// Serialize a component with its scene-object references swapped out for identifier tokens, so
    /// Echo can't deep-clone them into orphans. Reference fields are nulled only across the serialize
    /// call and always restored. Single-threaded by assumption, like the rest of the editor.
    /// </summary>
    private static (EchoObject data, Dictionary<string, string> refs) CaptureState(MonoBehaviour comp)
    {
        var refs = new Dictionary<string, string>();
        var stashed = new List<(FieldInfo field, object value)>();

        // Top-level fields only. A scene reference nested inside a list or a custom class still
        // deep-clones; fixing that needs identifier-aware reference handling down in Echo itself.
        foreach (var field in comp.GetSerializableFields())
        {
            object? value = field.GetValue(comp);
            if (value is not (GameObject or MonoBehaviour or Transform)) continue;

            // A reference that yields no token (detached Transform) is still nulled - just dropped
            // on paste rather than restored.
            if (TryMakeRefToken(value, out string? token))
                refs[field.Name] = token!;

            stashed.Add((field, value));
            field.SetValue(comp, null);
        }

        try
        {
            return (Serializer.Serialize(comp.GetType(), comp), refs);
        }
        finally
        {
            foreach (var (field, value) in stashed)
                field.SetValue(comp, value);
        }
    }

    /// <summary>
    /// Overwrite a live component's serializable state from a snapshot, preserving its identity.
    /// Uses DeserializeInto so state reaches the target through the component's own Deserialize - a
    /// field-by-field copy misses types like AudioSource that keep everything behind ISerializable.
    /// Brackets the write with OnDisable/OnEnable so components with native state (rigidbodies, audio
    /// sources) rebuild against the new values.
    /// </summary>
    private static void ApplyState(MonoBehaviour target, EchoObject data, Dictionary<string, string> refs)
    {
        bool attached = target.GameObject.IsValid();
        bool inActiveScene = attached && target.Scene.IsValid() && target.Scene!.IsActive;
        bool wasLive = inActiveScene && target.HasBeenEnabled && target.EnabledInHierarchy;

        if (wasLive) target.InternalOnDisable();

        // OnAfterDeserialize regenerates the identifier; preserve it so undo records and scene
        // lookups that key on it survive a values paste.
        Guid identifier = target.Identifier;
        Serializer.DeserializeInto(data, target);
        target.Identifier = identifier;

        ResolveRefs(target, refs);

        // AttachToGameObject re-derives _enabledInHierarchy from the pasted _enabled via the engine's
        // own rule rather than restating it here.
        if (attached) target.AttachToGameObject(target.GameObject);

        // Fire OnEnable from the pasted state, matching what assigning Enabled would do.
        if (inActiveScene && target.EnabledInHierarchy) target.InternalOnEnable();

        target.OnValidate();
    }

    // ================================================================
    //  Scene reference tokens
    // ================================================================

    /// <summary>
    /// Build an identifier token for a scene-object reference. False when the reference can't be
    /// identified, which the caller treats as "drop it" rather than "serialize it by value".
    /// </summary>
    private static bool TryMakeRefToken(object value, out string? token)
    {
        token = null;

        switch (value)
        {
            case GameObject go:
                token = RefGameObject + go.Identifier;
                return true;

            case MonoBehaviour mb:
                token = RefComponent + mb.Identifier;
                return true;

            // Transform has no identifier of its own; anchor it to its GameObject and re-derive on
            // paste. A detached Transform can't be anchored, so it's dropped (never cloned).
            case Transform t:
                if (!t.GameObject.IsValid()) return false;
                token = RefTransform + t.GameObject.Identifier;
                return true;
        }

        return false;
    }

    /// <summary>Re-resolve identifier tokens against the current scene and assign them back.</summary>
    private static void ResolveRefs(MonoBehaviour comp, Dictionary<string, string> refs)
    {
        if (refs.Count == 0) return;

        var fields = comp.GetSerializableFields();
        foreach (var (fieldName, token) in refs)
        {
            FieldInfo? field = fields.FirstOrDefault(f => f.Name == fieldName);
            if (field == null) continue;

            object? resolved = ResolveToken(token);
            // Leave the field null when the target isn't in this scene, or when the reference
            // resolved to something the field can't hold.
            if (resolved != null && field.FieldType.IsInstanceOfType(resolved))
                field.SetValue(comp, resolved);
        }
    }

    private static object? ResolveToken(string token)
    {
        if (token.StartsWith(RefGameObject))
            return Guid.TryParse(token[RefGameObject.Length..], out var goId) ? Undo.FindGO(goId) : null;

        if (token.StartsWith(RefComponent))
            return Guid.TryParse(token[RefComponent.Length..], out var cId) ? Undo.FindComponent(cId) : null;

        if (token.StartsWith(RefTransform))
            return Guid.TryParse(token[RefTransform.Length..], out var tId) ? Undo.FindGO(tId)?.Transform : null;

        return null;
    }

    // ================================================================
    //  Helpers
    // ================================================================

    /// <summary>
    /// Parse the clipboard payload. Callers wrap this in their own try/catch - a malformed body is
    /// reported there rather than swallowed here.
    /// </summary>
    private static bool TryReadClipboard(out Type? type, out EchoObject? data, out Dictionary<string, string> refs)
    {
        type = null;
        data = null;
        refs = [];

        if (!TryParseHeader(out string typeName, out string body)) return false;

        type = RuntimeUtils.ResolveType(typeName);
        if (type == null)
        {
            // The payload is ours but the type isn't here - a script component copied from a project
            // that has it. Worth saying out loud rather than silently doing nothing.
            Debug.LogWarning($"Cannot paste component: type '{typeName}' was not found in this project.");
            return false;
        }

        if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract) return false;

        var root = EchoObject.ReadFromString(body);
        if (root == null || !root.TryGet("Data", out var dataTag) || dataTag == null) return false;

        data = dataTag;

        if (root.TryGet("Refs", out var refsTag) && refsTag != null)
            foreach (var (key, val) in refsTag.Tags)
                if (val?.StringValue is { Length: > 0 } token)
                    refs[key] = token;

        return true;
    }

    private static MonoBehaviour? Deserialize(EchoObject data, Type type)
        => Serializer.Deserialize(data, type) as MonoBehaviour;
}
