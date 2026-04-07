// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Interface for resolving engine objects by their asset ID.
/// Implement this to provide asset storage and retrieval (e.g., from disk, from a content pipeline, etc.).
/// </summary>
public interface IAssetDatabase
{
    /// <summary>
    /// Retrieves an <see cref="EngineObject"/> by its asset ID.
    /// Returns null if the asset is not found.
    /// </summary>
    EngineObject? Get(Guid assetId);
}

/// <summary>
/// Provides a global asset database accessor and helpers for configuring
/// Echo serialization contexts to automatically serialize/deserialize asset references.
/// </summary>
public static class AssetDatabase
{
    /// <summary>
    /// The current asset database implementation. Set this before serializing/deserializing
    /// objects that contain asset references.
    /// </summary>
    public static IAssetDatabase? Current { get; set; }

    /// <summary>
    /// Resolves an <see cref="EngineObject"/> by asset ID from the current database.
    /// Checks built-in assets first, then falls back to the current database.
    /// Returns null if the asset is not found.
    /// </summary>
    public static EngineObject? Get(Guid assetId)
    {
        // Try built-in assets first (deterministic GUIDs for embedded resources)
        var builtIn = BuiltInAssets.Get(assetId);
        if (builtIn != null) return builtIn;

        return Current?.Get(assetId);
    }
}
