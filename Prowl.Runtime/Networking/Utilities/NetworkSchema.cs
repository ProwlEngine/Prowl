// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Every [Sync] property and RPC in the loaded scripts, with its slot and types. Sessions fold its hash into the
///     app version, so a build whose networked members differ is turned away at join.
/// </summary>
internal static class NetworkSchema
{
    private const BindingFlags DescriptorFields =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The app version a session sends and expects: <paramref name="appVersion" /> mixed with the schema hash.</summary>
    internal static uint AppVersion(uint appVersion)
    {
        uint hash = Fnv(2166136261, BitConverter.GetBytes(appVersion));
        foreach (string entry in Entries())
        {
            hash = Fnv(hash, Encoding.UTF8.GetBytes(entry + "\n"));
        }

        return hash;
    }

    internal static IEnumerable<string> Entries()
    {
        string runtime = typeof(Component).Assembly.GetName().Name!;
        List<string> entries = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (Assembly assembly in RuntimeUtils.AssemblySource())
        {
            // The assembly source lists the live build first, so a later one by the same name is a superseded build.
            string name = assembly.GetName().Name ?? "";
            if (seen.Add(name) && !assembly.IsDynamic &&
                (name == runtime || assembly.GetReferencedAssemblies().Any(a => a.Name == runtime)))
            {
                foreach (Type type in TypesOf(assembly))
                {
                    entries.AddRange(Describe(type));
                }
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Player builds preserve script assemblies and their generated network descriptor fields.")]
    internal static IEnumerable<string> Describe(Type type)
    {
        foreach (FieldInfo field in type.GetFields(DescriptorFields))
        {
            bool sync = typeof(SyncMember).IsAssignableFrom(field.FieldType);
            if (!sync && !typeof(RpcMember).IsAssignableFrom(field.FieldType) &&
                field.FieldType != typeof(StaticRpcMember))
            {
                continue;
            }

            if (type.ContainsGenericParameters)
            {
                yield return $"{type} {field.Name} {field.FieldType}";
                continue;
            }

            switch (field.GetValue(null))
            {
                case SyncMember member:
                    yield return $"{type} sync {member.Name}#{member.Slot} {field.FieldType} {member.Flags}";
                    break;
                case RpcMember rpc:
                    yield return $"{type} rpc {rpc.Signature}#{rpc.Id} {rpc.Mode} {rpc.Flags} {rpc.Channel}";
                    break;
                case StaticRpcMember rpc when rpc.DeclaringType == type:
                    yield return $"{type} static {rpc.Signature}#{rpc.Id} {rpc.Mode} {rpc.Flags} {rpc.Channel}";
                    break;
            }
        }
    }

    private static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null).ToArray()!;
        }
    }

    private static uint Fnv(uint hash, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= 16777619;
        }

        return hash;
    }
}
