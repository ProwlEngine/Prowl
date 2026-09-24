// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Stands in for a component whose type could not be loaded. It saves back out as the original
/// component's data, with every reference renumbered into the file being written, so the component
/// loads normally with its references intact once its type exists again.
/// </summary>
[ComponentIcon("\uf059")] // CircleQuestion
public class MissingMonobehaviour : MonoBehaviour, ISerializable
{
    public EchoObject ComponentData;

    // Live objects the $id and $extern nodes in ComponentData resolved to, keyed by $id, or by the
    // negative walk index of a $extern node.
    private Dictionary<int, object> _references = new();

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        if (ComponentData != null)
        {
            int externIndex = 0;
            var newIds = new Dictionary<int, int>();
            foreach (string key in ComponentData.GetNames())
            {
                if (key == "$id") continue;
                EchoObject value = ComponentData[key];
                compound[key] = key == "$type" ? value.Clone() : Rewrite(value, ctx, newIds, ref externIndex);
            }
        }

        compound["Name"] = Serializer.Serialize(typeof(string), Name, ctx);
        compound["AssetPath"] = Serializer.Serialize(typeof(string), AssetPath, ctx);
        compound["AssetID"] = Serializer.Serialize(typeof(Guid), AssetID, ctx);
        compound["_identifier"] = Serializer.Serialize(typeof(Guid), Identifier, ctx);
        compound["_enabled"] = new EchoObject(_enabled);
        compound["_enabledInHierarchy"] = new EchoObject(_enabledInHierarchy);
        compound["HideFlags"] = Serializer.Serialize(typeof(HideFlags), HideFlags, ctx);
        if (IsFromPrefab)
            compound["_prefabTemplateIdentity"] = Serializer.Serialize(typeof(Guid), SourceIdentifier, ctx);
        else
            compound.Remove("_prefabTemplateIdentity");
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        _references.Clear();

        // Saved before missing components kept their original shape: the data sits in a ComponentData
        // field and its ids belong to an older file, so they are not resolved against this one.
        if (RuntimeUtils.FindType(value.Get("$type")?.StringValue ?? "") == typeof(MissingMonobehaviour))
        {
            Load(value, value.Get("ComponentData"), ctx);
            return;
        }

        // Only a reference reaches here when the definition was written inside a field that could not load it.
        if (value.TryGet("$id", out EchoObject? idTag) && !value.GetNames().Any(n => n != "$id" && n != "$type"))
        {
            int id = idTag!.IntValue;
            ctx.Defer(() =>
            {
                if (!ctx.unresolvedDefinitions.TryGetValue(id, out EchoObject? definition)) return;
                Load(definition, definition, ctx);
                LoadedIdentifier = Identifier;
                if (!GameObject.PreservingIdentifiers)
                    Identifier = Guid.NewGuid();
                CaptureReferences(ctx);
            });
            return;
        }

        Load(value, value, ctx);
        ctx.Defer(() => CaptureReferences(ctx));
    }

    private void Load(EchoObject fields, EchoObject? data, SerializationContext ctx)
    {
        ComponentData = data is { TagType: not EchoType.Null } ? data.Clone() : null!;
        Name = Read(fields, "Name", Name, ctx);
        AssetPath = Read(fields, "AssetPath", string.Empty, ctx);
        AssetID = Read(fields, "AssetID", Guid.Empty, ctx);
        Identifier = Read(fields, "_identifier", Guid.Empty, ctx);
        _enabled = Read(fields, "_enabled", true, ctx);
        _enabledInHierarchy = Read(fields, "_enabledInHierarchy", true, ctx);
        HideFlags = Read(fields, "HideFlags", default(HideFlags), ctx);
        SourceIdentifier = Read(fields, "_prefabTemplateIdentity", Guid.Empty, ctx);
    }

    private static T Read<T>(EchoObject compound, string key, T fallback, SerializationContext ctx)
        => compound.TryGet(key, out EchoObject? tag) && tag!.TagType != EchoType.Null && Serializer.Deserialize(tag, typeof(T), ctx) is T value
            ? value
            : fallback;

    private void CaptureReferences(SerializationContext ctx)
    {
        if (ComponentData == null) return;
        int externIndex = 0;
        Capture(ComponentData, ctx, true, ref externIndex);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Resolves references to objects that were already deserialized by the same load.")]
    private void Capture(EchoObject node, SerializationContext ctx, bool isRoot, ref int externIndex)
    {
        if (node.TagType == EchoType.List)
        {
            foreach (EchoObject item in node.List)
                Capture(item, ctx, false, ref externIndex);
            return;
        }
        if (node.TagType != EchoType.Compound) return;

        if (node.TryGet("$id", out EchoObject? idTag))
        {
            if (isRoot)
                _references[idTag!.IntValue] = this;
            else if (ctx.idToObject.TryGetValue(idTag!.IntValue, out object? live))
            {
                _references[idTag.IntValue] = live;
                return;
            }
        }
        else if (node.Contains("$extern"))
        {
            int key = -++externIndex;
            // Without a $type the declared type is unknown, and a Transform link resolves differently to a GameObject one.
            if (node.Contains("$type") && ctx.ExternalReferences != null)
            {
                try
                {
                    if (Serializer.Deserialize(node, typeof(object), ctx) is { } resolved)
                        _references[key] = resolved;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"A reference held by a missing script on '{Name}' could not be resolved. {e.Message}");
                }
            }
            return;
        }

        foreach (string key in node.GetNames())
            if (key != "$id" && key != "$type")
                Capture(node[key], ctx, false, ref externIndex);
    }

    private EchoObject Rewrite(EchoObject node, SerializationContext ctx, Dictionary<int, int> newIds, ref int externIndex)
    {
        if (node.TagType == EchoType.List)
        {
            EchoObject list = EchoObject.NewList();
            foreach (EchoObject item in node.List)
                list.ListAdd(Rewrite(item, ctx, newIds, ref externIndex));
            return list;
        }
        if (node.TagType != EchoType.Compound) return node.Clone();

        if (node.TryGet("$extern", out _))
        {
            int key = -++externIndex;
            if (_references.TryGetValue(key, out object? linked))
                return WriteLive(linked, ctx);
            return ctx.ExternalReferences != null ? node.Clone() : new EchoObject(EchoType.Null, null);
        }

        EchoObject result = EchoObject.NewCompound();
        if (node.TryGet("$id", out EchoObject? idTag))
        {
            int id = idTag!.IntValue;
            if (_references.TryGetValue(id, out object? live))
                return WriteLive(live, ctx);

            bool hasBody = node.GetNames().Any(n => n != "$id" && n != "$type");
            if (!hasBody)
            {
                if (!newIds.TryGetValue(id, out int stubId))
                    return new EchoObject(EchoType.Null, null);
                result["$id"] = new EchoObject(EchoType.Int, stubId);
                if (node.TryGet("$type", out EchoObject? stubType))
                    result["$type"] = stubType!.Clone();
                return result;
            }

            // Only ever existed inside this component's data, so it keeps its body under a fresh id.
            int newId = ctx.nextId++;
            newIds[id] = newId;
            result["$id"] = new EchoObject(EchoType.Int, newId);
        }

        foreach (string key in node.GetNames())
        {
            if (key == "$id") continue;
            result[key] = key == "$type" ? node[key].Clone() : Rewrite(node[key], ctx, newIds, ref externIndex);
        }
        return result;
    }

    // Always typed, since the declared type is unknown here and a link has to carry its type to resolve.
    private static EchoObject WriteLive(object live, SerializationContext ctx)
    {
        if (live is EngineObject engineObject && engineObject.IsNotValid())
            return new EchoObject(EchoType.Null, null);

        return Serializer.Serialize(typeof(object), live, ctx);
    }
}
