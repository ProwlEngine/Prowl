// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;

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
/// { ...serialized component... }
/// </code>
///
/// Scene-object references (fields typed GameObject / MonoBehaviour / Transform, at any depth) are
/// linked by persistence id rather than serialized by value: <see cref="SceneReferenceResolver"/>
/// is handed to Echo as the context's external-reference resolver, so Echo emits a stable key for
/// them instead of deep-cloning the target into an orphan, and resolves that key back to the live
/// instance on paste. References that can't be resolved (different scene, deleted object) become
/// null, which mirrors Unity.
/// </summary>
public static class ComponentClipboard
{
    private const string ClipboardHeader = "ProwlComponent:";

    // ================================================================
    //  Copy
    // ================================================================

    /// <summary>Serialize a component onto the system clipboard.</summary>
    public static void Copy(MonoBehaviour comp)
    {
        if (comp == null) return;

        var data = Serializer.Serialize(comp.GetType(), comp, SerializeContext(comp));
        Input.Clipboard = $"{ClipboardHeader}{comp.GetType().AssemblyQualifiedName}\n{data.WriteToString()}";
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
            if (!TryReadClipboard(out Type? type, out EchoObject? data) || type == null || data == null)
                return null;

            var comp = Serializer.Deserialize(data, type, DeserializeContext()) as MonoBehaviour;
            if (comp == null) return null;

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
                    var restored = Serializer.Deserialize(capturedData, t, DeserializeContext()) as MonoBehaviour;
                    if (restored == null) return;
                    restored.Identifier = compId;
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
            if (!TryReadClipboard(out Type? type, out EchoObject? data) || type == null || data == null)
                return false;
            if (type != target.GetType()) return false;

            // Snapshot the current state through the same reference-linking path, so undo restores
            // scene references as live instances rather than deep-cloned orphans.
            var beforeData = Serializer.Serialize(target.GetType(), target, SerializeContext(target));

            ApplyState(target, data);

            var compId = target.Identifier;
            var afterData = data;

            Undo.RegisterAction("Paste Component Values",
                undo: () => { var c = Undo.FindComponent(compId); if (c != null) ApplyState(c, beforeData); },
                redo: () => { var c = Undo.FindComponent(compId); if (c != null) ApplyState(c, afterData); });

            EditorSceneManager.MarkDirty();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to paste component values: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Overwrite a live component's serializable state from a snapshot, preserving its identity.
    /// Uses DeserializeInto so state reaches the target through the component's own Deserialize - a
    /// field-by-field copy misses types like AudioSource that keep everything behind ISerializable.
    /// Brackets the write with OnDisable/OnEnable so components with native state (rigidbodies, audio
    /// sources) rebuild against the new values.
    /// </summary>
    private static void ApplyState(MonoBehaviour target, EchoObject data)
    {
        bool attached = target.GameObject.IsValid();
        bool inActiveScene = attached && target.Scene.IsValid() && target.Scene!.IsActive;
        bool wasLive = inActiveScene && target.HasBeenEnabled && target.EnabledInHierarchy;

        if (wasLive) target.InternalOnDisable();

        // OnAfterDeserialize regenerates the identifier; preserve it so undo records and scene
        // lookups that key on it survive a values paste.
        Guid identifier = target.Identifier;
        Serializer.DeserializeInto(data, target, DeserializeContext());
        target.Identifier = identifier;

        // AttachToGameObject re-derives _enabledInHierarchy from the pasted _enabled via the engine's
        // own rule rather than restating it here.
        if (attached) target.AttachToGameObject(target.GameObject);

        // Fire OnEnable from the pasted state, matching what assigning Enabled would do.
        if (inActiveScene && target.EnabledInHierarchy) target.InternalOnEnable();

        target.OnValidate();
    }

    // ================================================================
    //  Scene reference linking
    // ================================================================

    // Serializing keys every scene reference except the component being copied (see
    // SceneReferenceResolver); deserializing only resolves keys, so it passes no copy roots.
    private static SerializationContext SerializeContext(MonoBehaviour root)
        => new() { ExternalReferences = new SceneReferenceResolver(root) };

    private static SerializationContext DeserializeContext()
        => new() { ExternalReferences = new SceneReferenceResolver() };

    // ================================================================
    //  Helpers
    // ================================================================

    /// <summary>
    /// Parse the clipboard payload into a component type and its serialized data. Callers wrap this
    /// in their own try/catch - a malformed body is reported there rather than swallowed here.
    /// </summary>
    private static bool TryReadClipboard(out Type? type, out EchoObject? data)
    {
        type = null;
        data = null;

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

        data = EchoObject.ReadFromString(body);
        return data != null;
    }
}
