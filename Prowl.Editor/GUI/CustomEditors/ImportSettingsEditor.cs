using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Base for editors whose authored state is an asset's <c>.meta</c> import settings.
/// </summary>
/// <remarks>
/// Draw fields straight against <see cref="Settings"/> and there is nothing else to do. That compound is
/// the state <see cref="AssetImporterEditor"/> diffs, so edits register as pending on their own, Apply
/// writes them and reimports, and Revert restores the last written values. No dirty flag per field to
/// remember, and no reload-from-disk on every selection change.
/// </remarks>
public abstract class ImportSettingsEditor : AssetImporterEditor
{
    /// <summary>
    /// Live settings per asset. Static because the inspector reuses one editor instance per asset type,
    /// so per-instance state would be a single slot the next selection overwrites.
    /// </summary>
    private static readonly Dictionary<Guid, EchoObject> s_settings = [];

    /// <summary>
    /// This asset's import settings, read from the <c>.meta</c> on first use and topped up with the
    /// importer's defaults. Mutate it in place; the base class notices.
    /// </summary>
    protected EchoObject Settings(AssetEntry entry)
    {
        if (!s_settings.TryGetValue(entry.Guid, out EchoObject settings))
        {
            settings = ReadFromDisk(entry);
            s_settings[entry.Guid] = settings;
            Rebaseline(entry, null); // what is on disk is, by definition, the clean state
        }

        // Drawn every frame, so an edit made from here is a pending settings record, kept only if something changed.
        if (!OrigamiUI.Origami.IsReadOnly) Core.Undo.WatchImporter(entry.Guid, "Change Import Settings");
        return settings;
    }

    /// <summary> Drops the cached settings for an asset whose <c>.meta</c> was written elsewhere, so the inspector reads it again. </summary>
    public static void Forget(Guid guid) => s_settings.Remove(guid);

    /// <summary>An asset's pending import settings, read from disk if nothing has edited them yet. Null when the asset is unknown.</summary>
    internal static EchoObject? Pending(Guid guid)
    {
        if (s_settings.TryGetValue(guid, out EchoObject cached)) return cached;
        if (EditorAssetBackend.Instance?.GetEntry(guid) is not AssetEntry entry) return null;
        return s_settings[guid] = ReadFromDisk(entry);
    }

    /// <summary>Replaces an asset's pending import settings, as undo does. Nothing is written or reimported.</summary>
    internal static void SetPending(Guid guid, EchoObject settings) => s_settings[guid] = settings;

    /// <summary>The asset's <c>.meta</c> path, or null when there is no open project.</summary>
    protected static string? MetaPathOf(AssetEntry entry)
    {
        if (Project.Current == null) return null;
        return MetaFile.GetMetaPath(Path.Combine(Project.Current.AssetsPath, entry.Path));
    }

    private static EchoObject ReadFromDisk(AssetEntry entry)
    {
        EchoObject settings = EchoObject.NewCompound();

        string? metaPath = MetaPathOf(entry);
        if (metaPath != null && File.Exists(metaPath))
            settings = MetaFile.Read(metaPath).Settings ?? EchoObject.NewCompound();

        // Top up with any key the importer has gained since this meta was written.
        EchoObject? defaults = EditorRegistries.CreateImporterByName(entry.ImporterType)?.DefaultSettings();
        if (defaults != null)
            foreach (KeyValuePair<string, EchoObject> kvp in defaults.Tags)
                if (!settings.TryGet(kvp.Key, out _))
                    settings[kvp.Key] = kvp.Value.Clone();

        return settings;
    }

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => s_settings.TryGetValue(entry.Guid, out EchoObject settings) ? settings : null;

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (!s_settings.TryGetValue(entry.Guid, out EchoObject settings)) return false;

        string? metaPath = MetaPathOf(entry);
        if (metaPath == null || !File.Exists(metaPath)) return false;

        OnBeforeApply(entry, settings);

        // Re-read and merge rather than replacing the compound wholesale. Something else may have
        // touched the meta since these settings were loaded, and only the keys this editor authored
        // should win over it.
        MetaFileData meta = MetaFile.Read(metaPath);
        EchoObject merged = meta.Settings ?? EchoObject.NewCompound();
        foreach (KeyValuePair<string, EchoObject> kvp in settings.Tags)
            merged[kvp.Key] = kvp.Value.Clone();

        meta.Settings = merged;
        MetaFile.Write(metaPath, meta);

        s_settings[entry.Guid] = merged;
        EditorAssetBackend.Instance?.Reimport(entry.Guid);
        return true;
    }

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        s_settings[entry.Guid] = baseline;
        OnAfterRevert(entry, baseline);
    }

    /// <summary>Last chance to fold extra state into <paramref name="settings"/> before it is written -
    /// the texture editor stitches its sprite configuration in here.</summary>
    protected virtual void OnBeforeApply(AssetEntry entry, EchoObject settings) { }

    /// <summary>Called once a revert has restored <paramref name="settings"/>, for editors holding
    /// derived state that has to be rebuilt from it.</summary>
    protected virtual void OnAfterRevert(AssetEntry entry, EchoObject settings) { }
}
