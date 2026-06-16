// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Reflection;
using System.Collections.Generic;

namespace Prowl.Editor;

/// <summary>
/// Snapshots and restores static field values across play-mode sessions.
/// Instead of resetting all static fields to their defaults (which destroys editor state),
/// we capture values before entering play mode and restore them when exiting.
/// </summary>
public static class StaticFieldCrawler
{
    private static readonly Dictionary<FieldInfo, object?> s_snapshot = [];

    /// <summary>
    /// Captures the current values of all mutable static fields in the given assembly.
    /// Call this before entering play mode.
    /// </summary>
    public static void SnapshotStaticFields(Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes())
        {
            var staticFields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (FieldInfo field in staticFields)
            {
                if (field.IsLiteral || field.IsInitOnly)
                    continue;

                try
                {
                    s_snapshot[field] = field.GetValue(null);
                }
                catch (Exception ex)
                {
                    //Runtime.Debug.LogWarning($"[StaticFieldCrawler] Could not snapshot '{type.Name}.{field.Name}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Restores all previously snapshotted static fields to their pre-play-mode values.
    /// Call this after exiting play mode.
    /// </summary>
    public static void RestoreStaticFields()
    {
        foreach (var (field, value) in s_snapshot)
        {
            try
            {
                field.SetValue(null, value);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"[StaticFieldCrawler] Could not restore '{field.DeclaringType?.Name}.{field.Name}': {ex.Message}");
            }
        }

        s_snapshot.Clear();
    }

    /// <summary>
    /// Discards the snapshot without restoring it. The snapshot holds <see cref="FieldInfo"/>
    /// handles (which pin their declaring user types) and captured values (which may be user
    /// instances), so it must be cleared before unloading the script AssemblyLoadContext.
    /// Only relevant if a reload is somehow attempted with a live snapshot; normally the
    /// snapshot is already empty outside of play mode.
    /// </summary>
    public static void Clear() => s_snapshot.Clear();
}
