// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Editor.Build;

/// <summary>
/// Turns one imported asset into the form a given target wants.
/// </summary>
/// <remarks>
/// Desktop wants BCn where Android wants ASTC, and the universal imported form is neither. A processor
/// declares what it produces and which assets it applies to; the resolver picks one by the target's
/// stated preference and caches the result by content.
/// </remarks>
public interface IAssetVariantProcessor
{
    /// <summary>Stable, because it is part of the cache key.</summary>
    string Id { get; }

    /// <summary>Raise this when the processor's output changes, which invalidates only its own entries.</summary>
    int Version { get; }

    /// <summary>The texture or data format id this produces, matched against the target's preferences.</summary>
    string Format { get; }

    bool AppliesTo(AssetEntry asset);

    Task ProcessAsync(AssetEntry asset, Stream source, Stream destination, PlatformTarget target, CancellationToken ct);
}

/// <summary>Where an asset's bytes for a target came from.</summary>
public enum VariantOrigin
{
    /// <summary>No processor applied, so the imported form ships unchanged.</summary>
    Universal,

    /// <summary>Served from the cache, which is the whole point of hashing.</summary>
    Cached,

    /// <summary>Produced by a processor just now, and stored for next time.</summary>
    Processed,
}

public sealed record ResolvedVariant(VariantOrigin Origin, string Format, string SourcePath, VariantKey? Key);

/// <summary>Picks a processor, consults the cache, and processes only on a miss.</summary>
public sealed class AssetVariantResolver
{
    private readonly IReadOnlyList<IAssetVariantProcessor> _processors;
    private readonly IVariantCache _cache;

    public AssetVariantResolver(IEnumerable<IAssetVariantProcessor> processors, IVariantCache cache)
    {
        _processors = processors?.ToList() ?? throw new ArgumentNullException(nameof(processors));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// The processor to use, or null when none applies and the imported form ships as it is.
    /// </summary>
    /// <remarks>
    /// Chosen by the target's preference order rather than the processor's, so a target that would
    /// rather have ASTC than raw gets ASTC without every processor having to know about every target.
    /// </remarks>
    public IAssetVariantProcessor? SelectProcessor(AssetEntry asset, PlatformTarget target)
    {
        var applicable = _processors.Where(p => p.AppliesTo(asset)).ToList();
        if (applicable.Count == 0) return null;

        foreach (string format in target.Capabilities.TextureFormats)
            if (applicable.FirstOrDefault(p => string.Equals(p.Format, format, StringComparison.OrdinalIgnoreCase)) is { } match)
                return match;

        return null;
    }

    /// <summary>
    /// Produces the bytes this target should ship for <paramref name="asset"/>, reusing cached output
    /// whenever the source content and the processor are both unchanged.
    /// </summary>
    public async Task<ResolvedVariant> ResolveAsync(
        AssetEntry asset, string importedPath, PlatformTarget target, CancellationToken ct = default)
    {
        var processor = SelectProcessor(asset, target);
        if (processor == null)
            return new ResolvedVariant(VariantOrigin.Universal, "universal", importedPath, null);

        var key = new VariantKey(await HashOfAsync(importedPath, ct).ConfigureAwait(false),
            target.Id, processor.Id, processor.Version);

        if (await _cache.ExistsAsync(key, ct).ConfigureAwait(false))
            return new ResolvedVariant(VariantOrigin.Cached, processor.Format, importedPath, key);

        using (var source = File.OpenRead(importedPath))
        using (var produced = new MemoryStream())
        {
            await processor.ProcessAsync(asset, source, produced, target, ct).ConfigureAwait(false);
            produced.Position = 0;
            await _cache.WriteAsync(key, produced, ct).ConfigureAwait(false);
        }

        return new ResolvedVariant(VariantOrigin.Processed, processor.Format, importedPath, key);
    }

    /// <summary>
    /// The content hash half of a <see cref="VariantKey"/>. Hashed rather than stamped with a modified
    /// time, so an entry stays valid across machines and a checkout that rewrites file times does not
    /// invalidate a whole project. Two assets with identical bytes share one entry for the same reason.
    /// </summary>
    private static async Task<string> HashOfAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    /// <summary>Opens the resolved bytes, from the cache when it was processed and from disk otherwise.</summary>
    public async Task<Stream> OpenAsync(ResolvedVariant variant, CancellationToken ct = default)
    {
        if (variant.Key is not { } key)
            return File.OpenRead(variant.SourcePath);

        return await _cache.OpenAsync(key, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The cache lost the entry it just reported for {key.ToStorageKey()}.");
    }
}
