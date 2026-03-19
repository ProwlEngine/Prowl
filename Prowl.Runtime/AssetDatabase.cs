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
    /// Returns null if no database is set or the asset is not found.
    /// </summary>
    public static EngineObject? Get(Guid assetId)
    {
        return Current?.Get(assetId);
    }

    /// <summary>
    /// Configures the given <see cref="SerializationContext"/> with OnSerialize/OnDeserialize
    /// callbacks that handle asset references via <c>$assetId</c> tags.
    /// </summary>
    public static void ConfigureContext(SerializationContext ctx)
    {
        ctx.OnSerialize = (obj, c) =>
        {
            if (obj is EngineObject eo && eo.AssetID != Guid.Empty)
            {
                var compound = EchoObject.NewCompound();
                compound["$assetId"] = new EchoObject(eo.AssetID.ToString());
                return compound; // serialize as just a reference
            }
            return null; // normal serialization
        };

        ctx.OnDeserialize = (data, type, c) =>
        {
            if (typeof(EngineObject).IsAssignableFrom(type)
                && data.TryGet("$assetId", out var assetIdTag))
            {
                var assetId = Guid.Parse(assetIdTag.StringValue);
                return (true, Get(assetId)); // resolve from DB, may return null
            }
            return (false, null); // normal deserialization
        };
    }
}
