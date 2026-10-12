// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Inspector;

/// <summary>The .volume asset inspector: the profile's components, edited live, with Apply and Revert.</summary>
[CustomAssetEditor(typeof(VolumeProfile))]
public class VolumeProfileAssetEditor : AssetImporterEditor
{
    /// <summary>One editor for inline edits of profile assets from the Volume and Camera inspectors.</summary>
    internal static readonly VolumeProfileAssetEditor Shared = new();

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => asset is VolumeProfile profile && profile.IsValid() ? Serializer.Serialize(typeof(object), profile) : null;

    protected override EchoObject? CapturePersistedState(AssetEntry entry, EngineObject? asset)
        => EditorAssetBackend.Instance?.ReadCachedEcho(entry.Guid) ?? CaptureState(entry, asset);

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
        => asset is VolumeProfile profile && profile.IsValid() && (EditorAssetBackend.Instance?.SaveAsset(entry.Guid, Serializer.Serialize(typeof(object), profile)) ?? false);

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (asset is VolumeProfile profile && profile.IsValid()) EditorAssetBackend.Instance?.RevertToSaved(profile);
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (asset is not VolumeProfile profile || profile.IsNotValid()) return;
        EditorGUI.SectionHeader(paper, $"{id}_hdr", $"{EditorIcons.LayerGroup}  Volume Profile", first: true);
        VolumeProfileGUI.DrawComponents(paper, $"{id}_list", profile);
        DrawApplyRevertBar(paper, id, entry, asset);
    }

    /// <summary>The Apply and Revert bar for a database profile edited outside its own inspector.</summary>
    internal void DrawPendingBar(Paper paper, string id, VolumeProfile profile)
    {
        if (!profile.IsFromDatabase || EditorAssetBackend.Instance?.GetEntry(profile.AssetID) is not { } entry) return;
        DrawApplyRevertBar(paper, id, entry, profile);
    }
}

/// <summary>The Volume inspector: where and how strongly it applies, then its profile's components.</summary>
[CustomEditor(typeof(Volume))]
public class VolumeEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var volume = (Volume)target;
        Undo.Snapshot(volume);

        PropertyGridUtils.DrawField(paper, $"{id}_global", "Is Global", typeof(bool), volume.IsGlobal, v => volume.IsGlobal = (bool)v!);
        if (!volume.IsGlobal)
        {
            PropertyGridUtils.DrawField(paper, $"{id}_size", "Size", typeof(Vector.Float3), volume.Size, v => volume.Size = (Vector.Float3)v!);
            PropertyGridUtils.DrawField(paper, $"{id}_blend", "Blend Distance", typeof(float), volume.BlendDistance, v => volume.BlendDistance = System.MathF.Max(0f, (float)v!));
        }
        EditorGUI.SliderRow(paper, $"{id}_weight", "Weight", volume.Weight, 0f, 1f, v => volume.Weight = v);
        PropertyGridUtils.DrawField(paper, $"{id}_priority", "Priority", typeof(float), volume.Priority, v => volume.Priority = (float)v!);

        VolumeProfileGUI.DrawProfileField(paper, $"{id}_profile", "Profile", volume.SharedProfile, volume.GameObject.Name, p => volume.SharedProfile = p);

        VolumeProfile? profile = volume.SharedProfile;
        if (profile.IsNotValid()) return;
        VolumeProfileGUI.DrawComponents(paper, $"{id}_list", profile, VolumeProfileGUI.ChangedFor(profile));
        VolumeProfileAssetEditor.Shared.DrawPendingBar(paper, $"{id}_pending", profile);
    }
}

/// <summary>The Camera inspector: its fields, then the components of its base profile.</summary>
[CustomEditor(typeof(Camera))]
public class CameraEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var camera = (Camera)target;
        DrawDefaultInspector(paper, id, camera);

        VolumeProfile? profile = camera.Profile;
        EditorGUI.SectionHeader(paper, $"{id}_base_hdr", "Base Profile");
        using (paper.Row($"{id}_base_btns").Height(Prowl.PaperUI.LayoutEngine.UnitValue.Auto)
            .Padding(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 0).Gap(Origami.Current.Metrics.Padding)
            .Margin(0, 0, 0, Origami.Current.Metrics.SpacingLarge).Enter())
        {
            Origami.Button(paper, $"{id}_base_new", $"{EditorIcons.Plus}  New", () =>
            {
                Undo.Snapshot(camera);
                camera.Profile = new VolumeProfile { Name = $"{camera.GameObject.Name} Profile" };
            }).Small().Show();
            if (profile.IsValid())
            {
                Origami.Button(paper, $"{id}_base_clone", $"{EditorIcons.Clone}  Clone", () =>
                {
                    Undo.Snapshot(camera);
                    VolumeProfile copy = profile.Clone();
                    copy.Name = $"{profile.Name} Copy";
                    camera.Profile = copy;
                }).Small().Show();
                if (!profile.IsFromDatabase)
                    Origami.Button(paper, $"{id}_base_save", $"{EditorIcons.FloppyDisk}  Save As Asset", () => VolumeProfileGUI.SaveAsAsset(profile)).Small().Show();
            }
        }

        if (profile.IsNotValid()) return;
        VolumeProfileGUI.DrawComponents(paper, $"{id}_base", profile, VolumeProfileGUI.ChangedFor(profile));
        VolumeProfileAssetEditor.Shared.DrawPendingBar(paper, $"{id}_base_pending", profile);
    }
}
