// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Audio;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for .audiomixer assets: the bus tree, each bus's level and effects, and the buttons for
/// restructuring it.
/// </summary>
/// <remarks>
/// The mixer is edited live, so a level change is audible while play mode runs. Writing to disk is
/// separate, and the base class works out whether there is anything to write by diffing against the
/// imported form.
/// </remarks>
[CustomAssetEditor(typeof(AudioMixer))]
public class AudioMixerAssetEditor : AssetImporterEditor
{
    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => asset is AudioMixer mixer && mixer.IsValid() ? SerializePersisted(mixer) : null;

    /// <summary>
    /// Baselines against what was imported rather than the live object, so a mixer changed from a
    /// script shows up as pending instead of being adopted as already saved.
    /// </summary>
    protected override EchoObject? CapturePersistedState(AssetEntry entry, EngineObject? asset)
        => EditorAssetBackend.Instance?.ReadCachedEcho(entry.Guid) ?? CaptureState(entry, asset);

    /// <summary>Serializes the mixer the way its file is written: AssetID cleared, so the whole object
    /// is emitted rather than an $assetId reference back to itself.</summary>
    private static EchoObject SerializePersisted(AudioMixer mixer)
    {
        Guid savedId = mixer.AssetID;
        mixer.AssetID = Guid.Empty;
        try { return Serializer.Serialize(typeof(object), mixer); }
        finally { mixer.AssetID = savedId; }
    }

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (asset is not AudioMixer mixer || mixer.IsNotValid()) return false;
        if (Project.Current == null) return false;

        try
        {
            string absolute = Path.Combine(Project.Current.AssetsPath, entry.Path);
            File.WriteAllText(absolute, SerializePersisted(mixer).WriteToString());
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save audio mixer '{entry.Path}': {ex.Message}");
            return false;
        }

        EditorAssetBackend.Instance?.Reimport(entry.Guid);
        return true;
    }

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (asset is not AudioMixer mixer || mixer.IsNotValid()) return;

        // Restored onto the live instance, so every source already pointing at one of its groups keeps
        // pointing at the same object and hears the revert immediately.
        mixer.ReleaseNative();
        Serializer.DeserializeInto(baseline, mixer);
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        Origami.Header(paper, $"{id}_h_info", $"{EditorIcons.VolumeHigh}  Audio Mixer").Show();
        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();

        if (asset is not AudioMixer mixer || mixer.IsNotValid()) return;

        Origami.Separator(paper, $"{id}_sep_groups").Show();
        Origami.Header(paper, $"{id}_h_groups", "Groups").Underline().Show();

        AudioMixerGroup master = mixer.Master;

        if (master.IsValid())
            DrawGroup(paper, id, mixer, master, 0);

        DrawApplyRevertBar(paper, id, entry, asset);
    }

    private void DrawGroup(Paper paper, string id, AudioMixer mixer, AudioMixerGroup group, int depth)
    {
        string groupId = $"{id}_g_{group.Identity}";
        bool isMaster = ReferenceEquals(group, mixer.Master);

        // Depth is carried by the property grid's own indent rather than a manual one, so a nested bus
        // lines up with the rest of the inspector.
        Origami.Foldout(paper, $"{groupId}_fold", group.GroupName).DefaultExpanded(true).Body(() =>
        {
            PropertyGridUtils.Draw(paper, $"{groupId}_props", group, _ => group.OnValidate(), depth);

            using (paper.Row($"{groupId}_buttons").Height(24).RowBetween(4).Enter())
            {
                Origami.Button(paper, $"{groupId}_add", "Add Child", () =>
                {
                    mixer.AddGroup(UniqueGroupName(mixer, "New Group"), group);
                }).Show();

                // The root has nowhere to re-point its children, so the mixer refuses to remove it.
                if (!isMaster)
                {
                    Origami.Button(paper, $"{groupId}_remove", "Remove", () =>
                    {
                        mixer.RemoveGroup(group);
                    }).Show();
                }
            }
        });

        // Snapshotted because removing a group during the walk would mutate what we are iterating.
        List<AudioMixerGroup> children = [];

        foreach (AudioMixerGroup candidate in mixer.Groups)
        {
            if (candidate.IsValid() && ReferenceEquals(candidate.Parent, group))
                children.Add(candidate);
        }

        foreach (AudioMixerGroup child in children)
            DrawGroup(paper, id, mixer, child, depth + 1);
    }

    private static string UniqueGroupName(AudioMixer mixer, string baseName)
    {
        if (mixer.FindGroup(baseName).IsNotValid()) return baseName;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{baseName} {i}";
            if (mixer.FindGroup(candidate).IsNotValid()) return candidate;
        }

        return baseName;
    }
}
