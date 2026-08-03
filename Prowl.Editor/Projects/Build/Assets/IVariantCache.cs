// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Editor.Utils;

namespace Prowl.Editor.Build;

/// <summary>
/// Identifies one processed form of one asset.
/// </summary>
/// <remarks>
/// Every part matters. The content hash means an edited asset misses. The target means two platforms do
/// not evict each other. The processor id and version mean fixing a compressor invalidates only what that
/// compressor produced, rather than forcing a full rebuild or, worse, quietly shipping stale output.
/// </remarks>
public readonly record struct VariantKey(string ContentHash, string TargetId, string ProcessorId, int ProcessorVersion)
{
    /// <summary>A filename safe form. Stable, because it is what a shared cache is addressed by.</summary>
    public string ToStorageKey() => $"{ProcessorId}.v{ProcessorVersion}.{ContentHash}";
}

/// <summary>
/// Stores processed assets by content.
/// </summary>
/// <remarks>
/// An interface rather than a folder because a studio shares this across a team and a build farm, and
/// retrofitting that later would mean touching every call site. Only the local implementation ships now.
/// </remarks>
public interface IVariantCache
{
    ValueTask<bool> ExistsAsync(VariantKey key, CancellationToken ct = default);

    /// <summary>The stored bytes, or null on a miss. The caller owns the stream.</summary>
    ValueTask<Stream?> OpenAsync(VariantKey key, CancellationToken ct = default);

    ValueTask WriteAsync(VariantKey key, Stream data, CancellationToken ct = default);

    /// <summary>
    /// Discards whatever the cache no longer wants to keep, by its own policy. Returns how many entries
    /// went.
    /// </summary>
    /// <remarks>
    /// A cache keyed by content grows without bound otherwise. Every edit of an asset produces a new key
    /// and leaves the previous variant behind, and nothing will ever ask for it again. On the interface
    /// rather than only on the local implementation, because a shared cache has the same problem and the
    /// caller should not have to know which kind it is holding.
    /// </remarks>
    ValueTask<int> PruneAsync(CancellationToken ct = default);
}

/// <summary>A cache on disk, laid out so it can be copied or shared wholesale.</summary>
public sealed class LocalVariantCache : IVariantCache
{
    private readonly string _root;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;

    /// <param name="maxBytes">Generous on purpose: the cost of a miss is reprocessing the asset.</param>
    /// <param name="maxAge">Entries untouched for this long go regardless of how much room is left.</param>
    public LocalVariantCache(string root, long maxBytes = 8L * 1024 * 1024 * 1024, TimeSpan? maxAge = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _maxBytes = maxBytes;
        _maxAge = maxAge ?? TimeSpan.FromDays(30);
    }

    /// <summary>
    /// Split by target, then by the first two hash characters. The fan out keeps any one directory from
    /// growing to the size where enumerating it becomes the slow part.
    /// </summary>
    /// <remarks>
    /// Target and processor ids reach here from out of tree code, so anything that could be read as a
    /// path is replaced. Otherwise an id carrying a separator writes outside the cache root.
    /// </remarks>
    private string PathFor(VariantKey key)
    {
        string prefix = key.ContentHash.Length >= 2 ? EditorUtils.SafeFileName(key.ContentHash[..2], "_") : "00";
        return Path.Combine(_root, EditorUtils.SafeFileName(key.TargetId, "_"), prefix,
            EditorUtils.SafeFileName(key.ToStorageKey(), "_"));
    }

    public ValueTask<bool> ExistsAsync(VariantKey key, CancellationToken ct = default)
    {
        string path = PathFor(key);
        if (!File.Exists(path)) return ValueTask.FromResult(false);

        // Stamped on a hit so pruning can go by least recently used. Without it the oldest file is the
        // one produced longest ago, and an asset that never changes would be the first thing discarded
        // despite being needed by every build.
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }

        return ValueTask.FromResult(true);
    }

    public ValueTask<Stream?> OpenAsync(VariantKey key, CancellationToken ct = default)
    {
        string path = PathFor(key);
        return ValueTask.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    /// <summary>
    /// Keeps the most recently used entries that fit in the byte budget and are young enough, and
    /// deletes the rest.
    /// </summary>
    public ValueTask<int> PruneAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root)) return ValueTask.FromResult(0);

        var cutoff = DateTime.UtcNow - _maxAge;
        long kept = 0;
        int removed = 0;

        // Newest first, so the budget is spent on what was used most recently.
        var entries = new DirectoryInfo(_root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.EndsWith(".partial", StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc);

        foreach (var file in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (file.LastWriteTimeUtc >= cutoff && kept + file.Length <= _maxBytes)
            {
                kept += file.Length;
                continue;
            }

            try { file.Delete(); removed++; } catch { }
        }

        return ValueTask.FromResult(removed);
    }

    public async ValueTask WriteAsync(VariantKey key, Stream data, CancellationToken ct = default)
    {
        string path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written aside and moved into place, so a build killed mid-write cannot leave a truncated entry
        // that a later build would happily serve as a cache hit. The name is unique per writer because
        // two operations can legitimately produce the same key at the same time.
        string temp = $"{path}.{Guid.NewGuid():N}.partial";

        try
        {
            await using (var destination = File.Create(temp))
                await data.CopyToAsync(destination, ct).ConfigureAwait(false);

            try
            {
                File.Move(temp, path, overwrite: true);
            }
            // Windows reports a contended replace as either of these, depending on whether the loser is
            // another writer or a reader holding the file open.
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && File.Exists(path))
            {
                // Two writers can only collide on a key when they produced it from the same content, so
                // what landed there is byte for byte what this one was about to write, and losing the
                // race is a success rather than a failure to report.
            }
        }
        finally
        {
            if (File.Exists(temp))
                try { File.Delete(temp); } catch { }
        }
    }
}
