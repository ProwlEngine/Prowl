// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

// Client: fields of received content linking to other networked objects, pointed at them again as they arrive.
internal sealed partial class NetworkSystem
{
    private readonly Dictionary<Guid, List<Link>> _links = new();

    // The targets linked from each networked root, or from each plain object holding a link.
    private readonly Dictionary<GameObject, List<Guid>> _linksFrom = new(ReferenceEqualityComparer.Instance);

    internal int LinkCount => _links.Values.Sum(links => links.Count);

    private sealed class Link(Component holder, SerializedMember member, bool filled, GameObject from)
    {
        public readonly Component Holder = holder;
        public readonly SerializedMember Member = member;
        public readonly GameObject From = from;
        public bool Filled = filled;
    }

    /// <summary>Notes the links of the components <paramref name="data" /> was read into with <paramref name="context" />.</summary>
    internal void CollectLinks(EchoObject data, SerializationContext context)
    {
        if (IsClient)
        {
            CollectLinks(data, context.idToObject, new HashSet<Component>(ReferenceEqualityComparer.Instance));
        }
    }

    private void CollectLinks(EchoObject node, Dictionary<int, object> read, HashSet<Component> seen)
    {
        if (node.TagType == EchoType.List)
        {
            foreach (EchoObject child in node.List)
            {
                CollectLinks(child, read, seen);
            }

            return;
        }

        if (node.TagType != EchoType.Compound)
        {
            return;
        }

        if (node.TryGet("$id", out EchoObject? id) && id!.TagType == EchoType.Int &&
            read.TryGetValue(id.IntValue, out object? value) && value is Component component && seen.Add(component))
        {
            NoteLinks(component, node);
        }

        foreach (KeyValuePair<string, EchoObject> child in node.Tags)
        {
            CollectLinks(child.Value, read, seen);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Reads the members of components the game already loaded, whose types it keeps.")]
    private void NoteLinks(Component component, EchoObject data)
    {
        if (component.IsNotValid())
        {
            return;
        }

        foreach (SerializedMember member in ReflectionUtils.GetSerializedMembers(component))
        {
            if (!IsLinkType(member.Type) || !data.TryGet(member.Key, out EchoObject? written))
            {
                continue;
            }

            GameObject? holderRoot = NetworkAccessor.RootOf(component.GameObject);
            object? current = member.GetValue(component);
            Guid target = current switch
            {
                null => written!.TagType == EchoType.Compound ? LinkedId(written, member.Type) : Guid.Empty,
                _ => LinkedObject(current) is { } owner && NetworkAccessor.RootOf(owner) is { } root && root.IsValid() &&
                     !ReferenceEquals(root, holderRoot)
                    ? IdOf(current)
                    : Guid.Empty
            };

            if (target == Guid.Empty)
            {
                continue;
            }

            if (!_links.TryGetValue(target, out List<Link>? links))
            {
                _links[target] = links = new List<Link>();
            }

            GameObject from = holderRoot.IsValid() ? holderRoot! : component.GameObject;
            links.Add(new Link(component, member, current is not null, from));
            if (!_linksFrom.TryGetValue(from, out List<Guid>? targets))
            {
                _linksFrom[from] = targets = new List<Guid>();
            }

            if (!targets.Contains(target))
            {
                targets.Add(target);
            }
        }
    }

    // Forgets the links held in go's tree, as it leaves this client.
    private void ForgetLinksFrom(GameObject go)
    {
        if (_linksFrom.Count == 0 || !_linksFrom.Remove(go, out List<Guid>? targets))
        {
            return;
        }

        foreach (Guid target in targets)
        {
            if (_links.TryGetValue(target, out List<Link>? links) &&
                links.RemoveAll(link => ReferenceEquals(link.From, go)) > 0 && links.Count == 0)
            {
                _links.Remove(target);
            }
        }
    }

    private void ForgetLinks()
    {
        _links.Clear();
        _linksFrom.Clear();
    }

    private static bool IsLinkType(Type type) =>
        typeof(GameObject).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) ||
        typeof(Transform).IsAssignableFrom(type);

    private static GameObject? LinkedObject(object value) => value switch
    {
        GameObject go => go,
        Component component => component.GameObject,
        Transform transform => transform.GameObject,
        _ => null
    };

    private static Guid IdOf(object value) => value switch
    {
        GameObject go => go.Identifier,
        Component component => component.Identifier,
        Transform transform when !ReferenceEquals(transform.GameObject, null) => transform.GameObject.Identifier,
        _ => Guid.Empty
    };

    // The identifier a link written by the host names, or Guid.Empty if written is not one.
    private static Guid LinkedId(EchoObject written, Type type)
    {
        KeyCapture capture = new();
        try
        {
            Serializer.Deserialize(written, type, new SerializationContext
            {
                ExternalReferences = capture,
                OnDeserialize = static (_, target, _) => target == typeof(Guid) ? (false, null) : (true, null)
            });
        }
        catch (Exception)
        {
            return Guid.Empty;
        }

        return capture.Key is Guid id ? id : Guid.Empty;
    }

    private sealed class KeyCapture : IExternalReferenceResolver
    {
        public object? Key;

        public object? GetReferenceKey(object value) => null;

        public object? ResolveReference(object key, Type targetType)
        {
            Key = key;
            return null;
        }
    }

    // Points the links to the tree of go at it.
    private void FillLinks(GameObject go)
    {
        if (_links.Count == 0)
        {
            return;
        }

        FillLinks(go.Identifier, go);
        foreach (Component component in go.GetComponents<Component>())
        {
            FillLinks(component.Identifier, component);
        }

        foreach (GameObject child in go.Children)
        {
            FillLinks(child);
        }
    }

    // Points every link whose object this client has at it, and forgets those of components that are gone.
    private void FillAllLinks()
    {
        foreach (Guid id in _links.Keys.ToArray())
        {
            if (FindByIdentifier(id) is { } target)
            {
                FillLinks(id, target);
            }
            else if (_links.TryGetValue(id, out List<Link>? links) &&
                     links.RemoveAll(link => link.Holder.IsDisposed) > 0 && links.Count == 0)
            {
                _links.Remove(id);
            }
        }
    }

    private void FillLinks(Guid id, EngineObject target)
    {
        if (!_links.TryGetValue(id, out List<Link>? links))
        {
            return;
        }

        for (int i = links.Count - 1; i >= 0; i--)
        {
            Link link = links[i];
            if (link.Holder.IsDisposed)
            {
                links.RemoveAt(i);
                continue;
            }

            // A link the game has pointed elsewhere, or cleared since it was filled, is left alone.
            object? current = link.Member.GetValue(link.Holder);
            bool gone = current switch
            {
                null => !link.Filled,
                EngineObject engine => engine.IsNotValid() && IdOf(current) == id,
                _ => LinkedObject(current).IsNotValid() && IdOf(current) == id
            };
            object? value = target is GameObject go && typeof(Transform).IsAssignableFrom(link.Member.Type) ? go.Transform : target;
            if (gone && link.Member.Type.IsInstanceOfType(value))
            {
                link.Member.SetValue(link.Holder, value);
                link.Filled = true;
            }
        }

        if (links.Count == 0)
        {
            _links.Remove(id);
        }
    }
}
