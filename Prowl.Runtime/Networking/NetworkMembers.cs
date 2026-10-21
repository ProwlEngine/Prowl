// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

// Prowl's script compiler gives each [Sync] property and RPC a static descriptor field, next to the member
// it describes, with typed accessors. The runtime finds a component type's descriptors by reflection, once.
// Everything typed lives in the generated code, so nothing has to be instantiated at run time (which Native
// AOT can't do), and each partial class part brings its own members without knowing about the others.

/// <summary>A [Sync] property, as the script compiler describes it. Not meant to be created by hand.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class SyncMember(uint slot, string name, SyncFlags flags)
{
    public uint Slot { get; } = slot;

    public string Name { get; } = name;

    public SyncFlags Flags { get; } = flags;

    internal abstract void Write(Component component, NetWriter writer);

    internal abstract void Read(Component component, ref NetReader reader);

    internal abstract string FormatValue(Component component);

    internal virtual string FormatPayload(ref NetReader reader) => "Value unavailable";
}

/// <inheritdoc />
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SyncMember<TComponent, T>(
    uint slot,
    string name,
    SyncFlags flags,
    Func<TComponent, T> get,
    Action<TComponent, T> set)
    : SyncMember(slot, name, flags) where TComponent : Component
{
    internal override void Write(Component component, NetWriter writer) =>
        NetSerializer<T>.Write(writer, get((TComponent)component));

    internal override void Read(Component component, ref NetReader reader) =>
        set((TComponent)component, NetSerializer<T>.Read(ref reader));

    internal override string FormatValue(Component component) =>
        NetworkDiagnostics.FormatValue(get((TComponent)component));

    internal override string FormatPayload(ref NetReader reader) =>
        NetworkDiagnostics.FormatValue(NetSerializer<T>.Read(ref reader));
}

/// <summary>An RPC, as the script compiler describes it. Not meant to be created by hand.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class RpcMember(uint id, string signature, RpcMode mode, RpcFlags flags)
{
    public uint Id { get; } = id;

    /// <summary>Name and parameter types, as in <c>Fire(int,Float3)</c>.</summary>
    public string Signature { get; } = signature;

    public RpcMode Mode { get; } = mode;

    public RpcFlags Flags { get; } = flags;

    /// <summary>Reads the arguments and calls the method.</summary>
    internal abstract void Invoke(Component component, ref NetReader reader);

    internal abstract void ValidateArguments(Component component, ref NetReader reader);
}

/// <summary>Reads arguments, then optionally executes the RPC. Validation never runs game code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void RpcInvoker<in TComponent>(TComponent component, ref NetReader reader, bool execute);

/// <inheritdoc />
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcMember<TComponent>(
    uint id,
    string signature,
    RpcMode mode,
    RpcFlags flags,
    RpcInvoker<TComponent> invoke)
    : RpcMember(id, signature, mode, flags) where TComponent : Component
{
    internal override void Invoke(Component component, ref NetReader reader) =>
        invoke((TComponent)component, ref reader, true);

    internal override void ValidateArguments(Component component, ref NetReader reader)
    {
        invoke((TComponent)component, ref reader, false);
        if (!reader.IsAtEnd)
            throw new InvalidDataException("RPC arguments contain trailing bytes.");
    }
}

/// <summary>The [Sync] properties and RPCs of a component type, its base types' included.</summary>
internal sealed class NetworkMembers
{
    // Weak, so a script assembly that hot reload replaced can still unload.
    private static readonly ConditionalWeakTable<Type, NetworkMembers> s_byType = new();

    private static readonly NetworkMembers s_none = new();

    /// <summary>Every [Sync] property, base types' first.</summary>
    public readonly List<SyncMember> Syncs = new();

    private readonly Dictionary<uint, RpcMember> _rpcs = new();

    private readonly Dictionary<uint, SyncMember> _syncs = new();

    public static NetworkMembers Of(Component component) => Of(component.GetType());

    public static NetworkMembers Of(Type type) => s_byType.GetValue(type, Build);

    public SyncMember? Sync(uint slot) => _syncs.GetValueOrDefault(slot);

    public RpcMember? Rpc(uint id) => _rpcs.GetValueOrDefault(id);

    [UnconditionalSuppressMessage("Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Player builds preserve component assemblies and their network descriptor fields for reflection.")]
    private static NetworkMembers Build(Type type)
    {
        Stack<Type> hierarchy = new();
        for (Type? t = type; t != null && t != typeof(Component); t = t.BaseType)
        {
            hierarchy.Push(t);
        }

        NetworkMembers? members = null;
        foreach (Type t in hierarchy)
        {
            foreach (FieldInfo field in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public |
                                                    BindingFlags.DeclaredOnly))
            {
                if (!typeof(SyncMember).IsAssignableFrom(field.FieldType) &&
                    !typeof(RpcMember).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                switch (field.GetValue(null))
                {
                    case SyncMember sync:
                        members ??= new NetworkMembers();
                        // A derived class redeclaring a property under the same name takes its slot over.
                        if (members._syncs.TryGetValue(sync.Slot, out SyncMember? hidden))
                        {
                            members.Syncs.Remove(hidden);
                        }

                        members._syncs[sync.Slot] = sync;
                        members.Syncs.Add(sync);
                        break;

                    case RpcMember rpc:
                        // An override is the same RPC; the derived class's call is virtual either way.
                        (members ??= new NetworkMembers())._rpcs[rpc.Id] = rpc;
                        break;
                }
            }
        }

        return members ?? s_none;
    }
}
