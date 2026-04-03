using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;

namespace Prowl.Editor;

/// <summary>
/// Persists the full asset index to Library/metadata.db for fast startup.
/// Uses Echo string format for debuggability.
/// </summary>
public static class MetadataCache
{
    public static Dictionary<Guid, AssetEntry> Load(string metadataDbPath)
    {
        var result = new Dictionary<Guid, AssetEntry>();
        if (!File.Exists(metadataDbPath)) return result;

        try
        {
            string text = File.ReadAllText(metadataDbPath);
            var root = EchoObject.ReadFromString(text);

            if (root.TryGet("entries", out var entriesTag) && entriesTag.TagType == EchoType.List)
            {
                foreach (var entryTag in entriesTag.List)
                {
                    var entry = Serializer.Deserialize<AssetEntry>(entryTag, new SerializationContext());
                    if (entry != null && entry.Guid != Guid.Empty)
                        result[entry.Guid] = entry;
                }
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to load metadata cache: {ex.Message}");
        }

        return result;
    }

    public static void Save(string metadataDbPath, IEnumerable<AssetEntry> entries)
    {
        try
        {
            var root = EchoObject.NewCompound();
            root["version"] = new EchoObject(1);
            root["lastScanTime"] = new EchoObject(DateTime.UtcNow.ToString("o"));

            var list = EchoObject.NewList();
            foreach (var entry in entries)
            {
                var ctx = new SerializationContext();
                var serialized = Serializer.Serialize(entry, ctx);
                if (serialized != null)
                    list.ListAdd(serialized);
            }
            root["entries"] = list;

            string dir = Path.GetDirectoryName(metadataDbPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(metadataDbPath, root.WriteToString());
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Failed to save metadata cache: {ex.Message}");
        }
    }
}
