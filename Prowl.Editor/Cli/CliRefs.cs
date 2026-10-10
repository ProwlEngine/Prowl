// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// The one addressing scheme every CLI command uses, and the summaries results are made of.
/// <para/>
/// A ref is <c>@selection</c>, <c>@active</c>, <c>#guid</c> (a scene object or an asset), a hierarchy path like
/// <c>/Player/Camera</c> (<c>Name[1]</c> picks among same named siblings), or an asset path relative to Assets like
/// <c>Guns/Rifle.fbx</c> (<c>Rifle.fbx#Fire</c> for a sub asset). A scene ref takes <c>:Type</c> or <c>:Type[1]</c>
/// to address one of its components.
/// </summary>
public static class CliRefs
{
    private static readonly Regex s_componentSuffix = new(@":(?<type>[A-Za-z_][A-Za-z0-9_.]*)(\[(?<index>\d+)\])?$", RegexOptions.Compiled);
    private static readonly Regex s_indexedName = new(@"^(?<name>.*)\[(?<index>\d+)\]$", RegexOptions.Compiled);

    public static Scene Scene => Scene.Current.IsValid() ? Scene.Current : throw new CliException("No scene is open.");

    /// <summary> Resolves a ref to a GameObject, a component, or an asset. </summary>
    public static object Resolve(string text)
    {
        text = text.Trim();
        if (text.Length == 0) throw new CliException("Empty ref.");

        var suffix = s_componentSuffix.Match(text);
        if (suffix.Success && LooksLikeSceneRef(text[..suffix.Index]))
        {
            var go = ResolveGameObject(text[..suffix.Index]);
            int index = suffix.Groups["index"].Success ? int.Parse(suffix.Groups["index"].Value) : 0;
            return Component(go, suffix.Groups["type"].Value, index);
        }

        if (text is "@selection" or "@active")
        {
            object? selected = text == "@active" ? Selection.ActiveObject : Selection.Selected.FirstOrDefault();
            return selected is GameObject go && go.IsValid() ? go : throw new CliException($"{text} is not a GameObject. Select one, or use 'select'.");
        }

        if (text.StartsWith('/')) return FindByPath(text);

        string guidText = text.StartsWith('#') ? text[1..] : text;
        if (Guid.TryParse(guidText, out Guid guid))
        {
            if (FindInScene(guid) is { } sceneObject) return sceneObject;
            var asset = LoadAsset(guid);
            return asset.IsValid() ? asset : throw new CliException($"Nothing in the scene or the asset database has id {guid}.");
        }

        return ResolveAsset(text);
    }

    private static EngineObject? FindInScene(Guid id)
    {
        if (Scene.Current.IsNotValid()) return null;
        var found = Scene.Current.FindObjectByIdentifier<EngineObject>(id);
        return found.IsValid() ? found : null;
    }

    private static bool LooksLikeSceneRef(string text)
        => text.StartsWith('/') || text.StartsWith('@') || text.StartsWith('#') || Guid.TryParse(text, out _);

    public static GameObject ResolveGameObject(string text) => Resolve(text) switch
    {
        GameObject go => go,
        MonoBehaviour mb => mb.GameObject,
        var other => throw new CliException($"'{text}' is a {other.GetType().Name}, not a GameObject."),
    };

    /// <summary> A comma separated list of refs, or @selection for every selected GameObject. </summary>
    public static List<GameObject> ResolveGameObjects(string text)
    {
        if (text.Trim() == "@selection")
            return Selection.GetSelected<GameObject>().ToList() is { Count: > 0 } selected ? selected : throw new CliException("Nothing is selected.");
        return SplitList(text).Select(ResolveGameObject).ToList();
    }

    public static IEnumerable<string> SplitList(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static T Resolve<T>(string text) where T : class
        => Resolve(text) as T ?? throw new CliException($"'{text}' is not a {typeof(T).Name}.");

    private static GameObject FindByPath(string path)
    {
        string[] segments = path.Trim('/').Split('/');
        IEnumerable<GameObject> level = Scene.RootObjects;
        GameObject? current = null;

        foreach (string segment in segments)
        {
            string name = segment;
            int? index = null;
            var indexed = s_indexedName.Match(segment);
            if (indexed.Success)
            {
                name = indexed.Groups["name"].Value;
                index = int.Parse(indexed.Groups["index"].Value);
            }

            var matches = level.Where(g => g.Name == name).ToList();
            if (matches.Count == 0)
            {
                string where = current == null ? "the scene root" : PathOf(current);
                var names = level.Select(g => g.Name).Distinct().Take(20).ToList();
                throw new CliException($"No '{name}' under {where}. There is: {(names.Count == 0 ? "nothing" : string.Join(", ", names))}.");
            }
            if (index is { } i)
            {
                if (i >= matches.Count) throw new CliException($"There are only {matches.Count} objects named '{name}' there.");
                current = matches[i];
            }
            else if (matches.Count > 1)
                throw new CliException($"{matches.Count} objects are named '{name}' there. Use {name}[0] to {name}[{matches.Count - 1}], or an id: {string.Join(", ", matches.Select(m => "#" + m.Identifier))}.");
            else
                current = matches[0];

            level = current.Children;
        }

        return current!;
    }

    private static MonoBehaviour Component(GameObject go, string typeName, int index)
    {
        Type type = FindType(typeName, typeof(MonoBehaviour));
        var matches = go.GetComponents().Where(c => type.IsInstanceOfType(c)).ToList();
        if (index < matches.Count) return matches[index];
        string have = string.Join(", ", go.GetComponents().Select(c => c.GetType().Name));
        throw new CliException(matches.Count == 0
            ? $"{PathOf(go)} has no {type.Name}. It has: {(have.Length == 0 ? "nothing" : have)}."
            : $"{PathOf(go)} has only {matches.Count} {type.Name}.");
    }

    /// <summary> Finds a type assignable to <paramref name="baseType"/> by short or full name, among the engine and the project's scripts. </summary>
    public static Type FindType(string name, Type baseType)
    {
        var candidates = ScriptAssemblyManager.GetAllTypes()
            .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t) && (t.FullName == name || t.Name == name))
            .Distinct()
            .ToList();

        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count > 1)
        {
            var exact = candidates.Where(t => t.FullName == name).ToList();
            if (exact.Count == 1) return exact[0];
            throw new CliException($"'{name}' is ambiguous: {string.Join(", ", candidates.Select(t => t.FullName))}.");
        }

        var close = ScriptAssemblyManager.GetAllTypes()
            .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t) && t.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name).Distinct().Take(10).ToList();
        throw new CliException($"No {baseType.Name} type named '{name}'.{(close.Count > 0 ? $" Did you mean: {string.Join(", ", close)}?" : "")}");
    }

    /// <summary> An asset type by short or full name, abstract ones included so a whole family can be searched. </summary>
    public static Type FindAssetType(string name)
    {
        var match = ScriptAssemblyManager.GetAllTypes()
            .Where(t => typeof(Asset).IsAssignableFrom(t) && (t.FullName == name || t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToList();
        if (match.Count == 1) return match[0];
        if (match.Count > 1) throw new CliException($"'{name}' is ambiguous: {string.Join(", ", match.Select(t => t.FullName))}.");
        throw new CliException($"No asset type named '{name}'.");
    }

    // ================================================================
    //  Assets
    // ================================================================

    public static EditorAssetBackend Assets => EditorAssetBackend.Instance ?? throw new CliException("No asset database is open.");

    /// <summary> Normalizes an asset path to the backend's form: relative to Assets, forward slashes. </summary>
    public static string AssetPath(string text)
    {
        string path = EditorAssetBackend.NormalizePath(text.Trim());
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = path[7..];
        return path;
    }

    public static Guid ResolveAssetGuid(string text)
    {
        string guidText = text.StartsWith('#') ? text[1..] : text;
        if (Guid.TryParse(guidText, out Guid guid)) return guid;

        string path = AssetPath(text);
        string? subName = null;
        int hash = path.IndexOf('#');
        if (hash >= 0)
        {
            subName = path[(hash + 1)..];
            path = path[..hash];
        }

        var entry = Assets.GetEntry(path) ?? throw new CliException($"No asset at '{path}'. Paths are relative to the Assets folder.");
        if (subName == null) return entry.Guid;

        var sub = entry.SubAssets.FirstOrDefault(s => s.Name == subName)
            ?? throw new CliException($"'{path}' has no sub asset '{subName}'. It has: {string.Join(", ", entry.SubAssets.Select(s => s.Name))}.");
        return sub.Guid;
    }

    public static Asset ResolveAsset(string text)
    {
        Guid guid = ResolveAssetGuid(text);
        var asset = LoadAsset(guid);
        return asset.IsValid() ? asset : throw new CliException($"Asset {guid} could not be loaded.");
    }

    private static Asset? LoadAsset(Guid guid) => AssetDatabase.Load<Asset>(guid);

    // ================================================================
    //  Summaries
    // ================================================================

    public static string PathOf(GameObject go)
    {
        var names = new List<string>();
        for (var current = go; current != null; current = current.Parent)
        {
            IEnumerable<GameObject> siblings = current.Parent.IsValid() ? current.Parent.Children
                : current.Scene.IsValid() ? current.Scene.RootObjects : [current];
            var same = siblings.Where(s => s.Name == current.Name).ToList();
            names.Add(same.Count > 1 ? $"{current.Name}[{same.IndexOf(current)}]" : current.Name);
        }
        names.Reverse();
        return "/" + string.Join("/", names);
    }

    public static JsonObject Summary(object value) => value switch
    {
        GameObject go => new JsonObject { ["id"] = "#" + go.Identifier, ["path"] = PathOf(go), ["name"] = go.Name },
        MonoBehaviour mb => new JsonObject
        {
            ["id"] = "#" + mb.Identifier,
            ["ref"] = ComponentRef(mb),
            ["type"] = mb.GetType().Name,
            ["enabled"] = mb.Enabled,
        },
        Asset asset => AssetSummary(asset.AssetID, asset.GetType()),
        _ => new JsonObject { ["value"] = value.ToString() },
    };

    public static string ComponentRef(MonoBehaviour mb)
    {
        var sameType = mb.GameObject.GetComponents().Where(c => c.GetType() == mb.GetType()).ToList();
        int index = sameType.IndexOf(mb);
        return $"{PathOf(mb.GameObject)}:{mb.GetType().Name}{(index > 0 ? $"[{index}]" : "")}";
    }

    public static JsonObject AssetSummary(Guid guid, Type? type)
    {
        string? path = EditorAssetBackend.Instance?.GuidToPath(guid);
        if (path == null && EditorAssetBackend.Instance?.TryGetParentGuid(guid, out Guid parent) == true)
        {
            var sub = Assets.GetSubAssets(parent).FirstOrDefault(s => s.Guid == guid);
            path = $"{Assets.GuidToPath(parent)}#{sub?.Name}";
        }
        return new JsonObject { ["guid"] = guid.ToString(), ["path"] = path, ["type"] = type?.Name };
    }

    // ================================================================
    //  Fields, through Echo so the editable set is exactly what is saved
    // ================================================================

    /// <summary> The serialized fields of a component or asset, or of one field path inside it, as JSON. </summary>
    public static JsonNode? GetFields(object target, string path)
    {
        EchoObject root = Serialize(target);
        EchoObject node = Navigate(root, ParsePath(path), path);
        var json = EchoToJson(node);
        if (path.Length == 0 && json is JsonObject fields)
            foreach (string hidden in s_hiddenFields) fields.Remove(hidden);
        return json;
    }

    // Identity and derived state, already in the summary or not meant to be set.
    private static readonly string[] s_hiddenFields = ["_identifier", "_enabledInHierarchy", "_instanceID"];

    /// <summary>
    /// Writes one field path from JSON. A string is accepted for a scene reference (any ref) or an asset reference (a
    /// path or guid). Undoable, notifies the object like an inspector edit, and saves an edited asset.
    /// </summary>
    public static JsonObject SetFields(object target, IReadOnlyList<(string Path, string Json)> values)
    {
        if (target is Asset { IsFromDatabase: true } asset)
            CliEdit.EditAsset(asset, "Set", () => WriteFields(target, values));
        else
            CliEdit.Run("Set", () =>
            {
                Undo.Snapshot(target);
                WriteFields(target, values);
            });

        var result = new JsonObject();
        foreach (var (path, _) in values) result[path] = GetFields(target, path);
        return result;
    }

    /// <summary> Writes field paths with no undo record, for a caller that records the change itself. Every path is checked before anything is written. </summary>
    public static void WriteFields(object target, IReadOnlyList<(string Path, string Json)> values)
    {
        if (target is GameObject)
            throw new CliException("Address a component, for example /Player:CharacterController, or use 'go' for name, tag, layer, active and transform.");
        if (target is Transform)
            throw new CliException("Use 'go transform' to move, rotate or scale.");
        if (target is Material)
            throw new CliException("Use 'material' to edit material properties, so they stay marked as overridden.");
        if (values.Count == 0) throw new CliException("Give a field path and a value.");

        EchoObject root = Serialize(target);
        var topFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, json) in values)
        {
            var segments = ParsePath(path);
            if (segments.Count == 0) throw new CliException("Give a field path, for example speed or settings.radius.");
            if (KeyFor(Unwrap(root), segments[0].Name) == null)
                throw new CliException($"{target.GetType().Name} has no field '{segments[0].Name}'. Fields: {string.Join(", ", FieldNames(Unwrap(root)).Where(f => !s_hiddenFields.Contains(f)))}.");

            Type memberType = MemberTypeAt(target.GetType(), segments, path);
            Replace(root, segments, ValueFromJson(json, memberType), path);
            topFields.Add(segments[0].Name);
        }

        Apply(target, root, topFields);
    }

    private static void Apply(object target, EchoObject root, IEnumerable<string> topFields)
    {
        if (target is Asset { IsFromDatabase: true } asset)
        {
            AssetDatabase.Refill(asset, root, ReloadReason.Reimport);
            return;
        }

        Type type = target.GetType();
        object temp = Serializer.Deserialize(root, type, SceneReferenceResolver.ContextForLinking())
            ?? throw new CliException($"Echo could not read the edited {type.Name} back.");

        foreach (string topField in topFields)
        {
            var field = FindField(type, topField) ?? throw new CliException($"{type.Name} has no field '{topField}'.");
            field.SetValue(target, field.GetValue(temp));
        }

        if (target is EngineObject eo && eo.IsValid())
        {
            try { eo.OnValidate(); }
            catch (Exception ex) { Debug.LogError($"OnValidate threw on {type.Name}: {ex}"); }
            PrefabUtility.NotifyEdited(target);
        }
    }

    private static EchoObject Serialize(object target)
    {
        var context = target is EngineObject and not Asset
            ? new SerializationContext { ExternalReferences = new SceneReferenceResolver(target) }
            : new SerializationContext();
        return Serializer.Serialize(target.GetType(), target, context);
    }

    private readonly record struct Segment(string Name, int? Index);

    private static List<Segment> ParsePath(string path)
    {
        var segments = new List<Segment>();
        foreach (string part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(part, @"^(?<name>[^\[]+)(?<indices>(\[\d+\])*)$");
            if (!match.Success) throw new CliException($"Bad field path '{path}'.");
            var indices = Regex.Matches(match.Groups["indices"].Value, @"\d+").Select(m => int.Parse(m.Value)).ToList();
            segments.Add(new Segment(match.Groups["name"].Value, indices.Count > 0 ? indices[0] : null));
            foreach (int extra in indices.Skip(1)) segments.Add(new Segment("", extra));
        }
        return segments;
    }

    private static EchoObject Unwrap(EchoObject node)
    {
        while (node.TagType == EchoType.Compound)
        {
            if (node.TryGet("$value", out var value) && node.Contains("$type")) node = value!;
            else if (node.TryGet("$v", out var compact) && node.Contains("$t")) node = compact!;
            else if (node.TryGet("$values", out var values)) node = values!;
            else break;
        }
        return node;
    }

    private static EchoObject Navigate(EchoObject root, List<Segment> segments, string path)
    {
        EchoObject node = root;
        foreach (var segment in segments)
        {
            node = Unwrap(node);
            if (segment.Name.Length > 0) node = Child(node, segment.Name, path);
            if (segment.Index is { } index) node = Element(Unwrap(node), index, path);
        }
        return node;
    }

    private static void Replace(EchoObject root, List<Segment> segments, EchoObject value, string path)
    {
        EchoObject parent = Navigate(root, segments.Take(segments.Count - 1).ToList(), path);
        var last = segments[^1];
        parent = Unwrap(parent);

        if (last.Index is { } index)
        {
            var list = Unwrap(last.Name.Length > 0 ? Child(parent, last.Name, path) : parent);
            Element(list, index, path);
            list.List[index] = value;
        }
        else
        {
            string key = KeyFor(parent, last.Name) ?? throw new CliException($"No field '{last.Name}' in {path}. Fields: {string.Join(", ", FieldNames(parent))}.");
            parent[key] = value;
        }
    }

    private static EchoObject Child(EchoObject node, string name, string path)
    {
        if (node.TagType != EchoType.Compound) throw new CliException($"'{name}' in {path} is inside a value that has no fields.");
        string key = KeyFor(node, name) ?? throw new CliException($"No field '{name}' in {path}. Fields: {string.Join(", ", FieldNames(node))}.");
        return node[key];
    }

    private static EchoObject Element(EchoObject node, int index, string path)
    {
        if (node.TagType != EchoType.List) throw new CliException($"[{index}] in {path} indexes something that is not a list.");
        if (index >= node.List.Count) throw new CliException($"Index {index} in {path} is past the end of a list of {node.List.Count}.");
        return node.List[index];
    }

    private static string? KeyFor(EchoObject compound, string name)
        => compound.Tags.Keys.FirstOrDefault(k => k == name) ?? compound.Tags.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> FieldNames(EchoObject node)
        => node.TagType == EchoType.Compound ? node.Tags.Keys.Where(k => !k.StartsWith('$')) : [];

    private static FieldInfo? FindField(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            var field = t.GetField(name, flags) ?? t.GetFields(flags).FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (field != null) return field;
        }
        return null;
    }

    private static Type MemberTypeAt(Type type, List<Segment> segments, string path)
    {
        foreach (var segment in segments)
        {
            if (segment.Name.Length > 0)
                type = FindField(type, segment.Name)?.FieldType ?? throw new CliException($"{type.Name} has no field '{segment.Name}' ({path}).");
            if (segment.Index != null)
                type = ElementType(type) ?? throw new CliException($"{type.Name} in {path} is not a list.");
        }
        return type;
    }

    private static Type? ElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        return type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))
            ?.GetGenericArguments()[0];
    }

    /// <summary> JSON to a value of <paramref name="type"/>, with the same ref, asset and vector conveniences as 'set'. </summary>
    public static object? ConvertJson(string json, Type type)
        => Serializer.Deserialize(ValueFromJson(json, type), type, SceneReferenceResolver.ContextForLinking());

    /// <summary> Writes a dotted key path into raw Echo data, creating missing blocks, for data with no class behind it such as import settings. </summary>
    public static void SetEchoPath(EchoObject root, string path, string json)
    {
        string[] keys = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0) throw new CliException("Give a settings key.");

        EchoObject node = root;
        foreach (string key in keys[..^1])
        {
            node = Unwrap(node);
            string? existing = KeyFor(node, key);
            if (existing == null) node[key] = EchoObject.NewCompound();
            node = node[existing ?? key];
        }

        node = Unwrap(node);
        EchoObject value;
        try { value = EchoObject.ReadFromJson(json); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or FormatException) { value = new EchoObject(json); }
        node[KeyFor(node, keys[^1]) ?? keys[^1]] = value;
    }

    private static EchoObject ValueFromJson(string json, Type type)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException)
        {
            // A bare word is a string, so set name Bob and set target /Player both work unquoted.
            node = JsonValue.Create(json);
        }

        if (node is JsonObject { Count: 1 } refObject && refObject.TryGetPropertyValue("ref", out var refValue) && refValue is JsonValue)
            node = refValue;

        if (node is JsonValue v && v.TryGetValue(out string? text))
        {
            object? reference = ReferenceFromString(text, type);
            if (reference != null)
                return Serializer.Serialize(type, reference, new SerializationContext { ExternalReferences = new SceneReferenceResolver(), RootByReference = true });
        }

        if (node == null) return new EchoObject(EchoType.Null, null);
        if (type == typeof(Float2) || type == typeof(Float3) || type == typeof(Float4) || type == typeof(Quaternion) || type == typeof(Color))
            node = VectorObject(node, type);

        return EchoObject.ReadFromJson(node.ToJsonString());
    }

    private static object? ReferenceFromString(string text, Type type)
    {
        if (text.Length == 0) return null;

        if (typeof(GameObject).IsAssignableFrom(type) || typeof(MonoBehaviour).IsAssignableFrom(type) || type == typeof(Transform))
        {
            object resolved = Resolve(text);
            if (type == typeof(Transform)) return ResolveGameObject(text).Transform;
            if (type.IsInstanceOfType(resolved)) return resolved;
            if (resolved is GameObject go && typeof(MonoBehaviour).IsAssignableFrom(type))
                return go.GetComponents().FirstOrDefault(type.IsInstanceOfType) is { } component && component.IsValid()
                    ? component : throw new CliException($"{PathOf(go)} has no {type.Name}.");
            throw new CliException($"'{text}' is a {resolved.GetType().Name}, the field wants a {type.Name}.");
        }

        if (typeof(Asset).IsAssignableFrom(type))
        {
            var asset = ResolveAsset(text);
            return type.IsInstanceOfType(asset) ? asset : throw new CliException($"'{text}' is a {asset.GetType().Name}, the field wants a {type.Name}.");
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AssetRef<>))
            return Activator.CreateInstance(type, ResolveAssetGuid(text));

        return null;
    }

    // Vectors and colors accept [x, y, z] as well as Echo's own field form. A color given three values is opaque.
    private static JsonNode VectorObject(JsonNode node, Type type)
    {
        if (node is not JsonArray array) return node;
        var names = Serializer.Serialize(type, Activator.CreateInstance(type)!).Tags.Keys.Where(k => !k.StartsWith('$')).ToList();
        if (array.Count > names.Count) throw new CliException($"{type.Name} has {names.Count} components, got {array.Count}.");

        var obj = new JsonObject();
        for (int i = 0; i < array.Count; i++) obj[names[i]] = array[i]?.DeepClone();
        if (type == typeof(Color) && array.Count == 3 && names.Count == 4) obj[names[3]] = 1.0;
        return obj;
    }

    /// <summary> Echo data as JSON for an agent to read: ids dropped, type envelopes unwrapped, scene and asset links shown as refs. </summary>
    public static JsonNode? EchoToJson(EchoObject node)
    {
        switch (node.TagType)
        {
            case EchoType.Null: return null;
            case EchoType.List:
                var array = new JsonArray();
                foreach (var item in node.List) array.Add(EchoToJson(item));
                return array;
            case EchoType.Compound:
                if (node.TryGet("$v", out var compact) && node.Contains("$t")) return EchoToJson(compact!);
                if (node.TryGet("$value", out var wrapped) && node.Contains("$type")) return EchoToJson(wrapped!);
                if (node.TryGet("$values", out var items)) return EchoToJson(items!);
                if (node.TryGet("$extern", out var key)) return SceneLink(key!);
                if (node.TryGet("$asset", out var asset) && Guid.TryParse(asset!.StringValue, out Guid assetId)) return AssetLink(assetId);
                if (node.TryGet("$assetRef", out var assetRef) && Guid.TryParse(assetRef!.StringValue, out Guid refId)) return assetRef.StringValue == Guid.Empty.ToString() ? null : AssetLink(refId);

                var obj = new JsonObject();
                foreach (var (name, child) in node.Tags)
                {
                    if (name == "$id") continue;
                    obj[name == "$type" ? "$type" : name] = name == "$type" ? ShortTypeName(child.StringValue) : EchoToJson(child);
                }
                return obj;
            default:
                return CliCommands.ToJson(node.Value);
        }
    }

    private static JsonNode? SceneLink(EchoObject key)
    {
        if (Serializer.Deserialize(key, typeof(object)) is not Guid id || id == Guid.Empty) return null;
        return FindInScene(id) switch
        {
            GameObject go => new JsonObject { ["ref"] = PathOf(go), ["id"] = "#" + id },
            MonoBehaviour mb => new JsonObject { ["ref"] = ComponentRef(mb), ["id"] = "#" + id },
            _ => new JsonObject { ["ref"] = "#" + id, ["missing"] = true },
        };
    }

    private static JsonNode AssetLink(Guid id)
    {
        var summary = AssetSummary(id, AssetDatabase.GetAssetType(id));
        return new JsonObject { ["ref"] = summary["path"]?.GetValue<string>() ?? "#" + id, ["guid"] = id.ToString(), ["type"] = summary["type"]?.DeepClone() };
    }

    private static string ShortTypeName(string fullName)
    {
        int comma = fullName.IndexOf(',');
        string name = comma >= 0 ? fullName[..comma] : fullName;
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    // ================================================================
    //  Vectors
    // ================================================================

    public static Float3 ParseFloat3(string json, string argName)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonArray { Count: 3 } a) return new Float3(a[0]!.GetValue<float>(), a[1]!.GetValue<float>(), a[2]!.GetValue<float>());
            if (node is JsonObject o) return new Float3(Num(o, "x"), Num(o, "y"), Num(o, "z"));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
        throw new CliException($"'{argName}' expects [x, y, z], got {json}.");

        static float Num(JsonObject o, string key)
            => (o[key] ?? o[key.ToUpperInvariant()])?.GetValue<float>() ?? throw new FormatException();
    }

    public static JsonArray Json(Float3 v) => [v.X, v.Y, v.Z];
}

/// <summary>
/// Wraps an editor change made by a CLI command: one undo step, a dirty scene, and in play mode a once per session
/// warning that the change goes away when play stops.
/// </summary>
public static class CliEdit
{
    private static bool s_warnedThisPlay;
    private static bool s_wasPlaying;

    public static void Run(string description, Action change)
    {
        WarnIfPlaying();
        Undo.IncrementGroup();
        change();
        Undo.IncrementGroup();
        if (!Application.IsPlaying) EditorSceneManager.MarkDirty();
    }

    public static T Run<T>(string description, Func<T> change)
    {
        T result = default!;
        Run(description, () => { result = change(); });
        return result;
    }

    /// <summary>
    /// Changes an asset in memory, saves it, and records one undo step that saves the previous content back, so the
    /// file and the loaded asset always agree.
    /// </summary>
    public static void EditAsset(Asset asset, string description, Action change)
    {
        var assets = CliRefs.Assets;
        Guid ownerId = assets.TryGetParentGuid(asset.AssetID, out Guid parent) ? parent : asset.AssetID;
        Asset owner = AssetDatabase.Get(ownerId) is { } found && found.IsValid() ? found : asset;
        EchoObject before = assets.SerializeForSave(owner) ?? throw new CliException($"'{owner.Name}' could not be read to save it.");

        Run(description, () =>
        {
            change();
            EchoObject after = assets.SerializeForSave(owner) ?? throw new CliException($"'{owner.Name}' could not be serialized after the edit.");
            if (!assets.SaveAsset(ownerId, after)) throw new CliException($"'{owner.Name}' could not be saved. See the logs.");
            Undo.RegisterAction($"CLI: {description}", () => assets.SaveAsset(ownerId, before), () => assets.SaveAsset(ownerId, after));
        });
    }

    /// <summary> Records a GameObject level change as its own undo step, looking the object up again by id on undo and redo. </summary>
    public static void Record(string description, List<(Action undo, Action redo)> actions)
    {
        if (actions.Count > 0) Undo.RegisterActionGroup($"CLI: {description}", actions);
    }

    private static void WarnIfPlaying()
    {
        bool playing = Application.IsPlaying;
        if (playing && !s_wasPlaying) s_warnedThisPlay = false;
        s_wasPlaying = playing;

        if (!playing || s_warnedThisPlay) return;
        s_warnedThisPlay = true;
        Debug.LogWarning("[CLI] Play mode is running, so this change is not undoable and is lost when play stops. Stop play mode to make lasting edits.");
    }
}
