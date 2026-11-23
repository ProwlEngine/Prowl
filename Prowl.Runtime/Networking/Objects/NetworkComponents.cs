// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime.Networking;

/// <summary>The networked components of a networked object: those with [Sync] values, RPCs or their own state.</summary>
internal static class NetworkComponents
{
    /// <summary>The most a networked object can hold.</summary>
    public const int MaxPerObject = 64;

    /// <summary>A tree's networked components in build order: each object's own, then its children's. Disabled ones count.</summary>
    public static Component[] Gather(GameObject root)
    {
        List<Component> found = new();
        Gather(root, found);
        return [.. found];
    }

    private static void Gather(GameObject go, List<Component> found)
    {
        foreach (Component component in go.GetComponents<Component>())
        {
            if (NetworkMembers.Of(component).Any)
            {
                found.Add(component);
            }
        }

        foreach (GameObject child in go.Children)
        {
            Gather(child, found);
        }
    }

    /// <summary>The most NetworkTransforms a networked object can hold, since one goes by a byte index.</summary>
    public const int MaxTransformsPerObject = byte.MaxValue;

    /// <summary>A tree's NetworkTransform components in build order.</summary>
    public static NetworkTransform[] GatherTransforms(GameObject root)
    {
        List<NetworkTransform> found = new();
        GatherTransforms(root, found);
        return [.. found];
    }

    private static void GatherTransforms(GameObject go, List<NetworkTransform> found)
    {
        foreach (Component component in go.GetComponents<Component>())
        {
            if (component is NetworkTransform transform)
            {
                found.Add(transform);
            }
        }

        foreach (GameObject child in go.Children)
        {
            GatherTransforms(child, found);
        }
    }

    /// <summary>A hash of the components' type names in order, for peers to check they built the same list.</summary>
    public static uint Hash(IReadOnlyList<Component> components)
    {
        uint hash = 2166136261;
        foreach (Component component in components)
        {
            foreach (char c in component.GetType().FullName ?? component.GetType().Name)
            {
                hash = (hash ^ c) * 16777619;
            }

            hash = (hash ^ 0xFFFF) * 16777619;
        }

        return hash;
    }

    public static string Describe(IReadOnlyList<Component> components)
    {
        List<string> names = new(components.Count);
        foreach (Component component in components)
        {
            names.Add(component.GetType().Name);
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
