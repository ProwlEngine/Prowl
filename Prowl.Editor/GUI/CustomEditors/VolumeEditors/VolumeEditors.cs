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

/// <summary>The .volume asset inspector: the profile's components, edited live. Edits are undo records that put the profile in the dirty asset set.</summary>
[CustomAssetEditor(typeof(VolumeProfile))]
public class VolumeProfileAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (asset is not VolumeProfile profile || profile.IsNotValid()) return;
        EditorGUI.SectionHeader(paper, $"{id}_hdr", $"{EditorIcons.LayerGroup}  Volume Profile", first: true);
        VolumeProfileGUI.DrawComponents(paper, $"{id}_list", profile);
        DrawSaveRevertBar(paper, id, profile);
    }

    /// <summary>The Save and Revert bar for a database profile edited outside its own inspector.</summary>
    internal static void DrawPendingBar(Paper paper, string id, VolumeProfile profile)
    {
        if (profile.IsFromDatabase) DrawSaveRevertBar(paper, id, profile);
    }
}

/// <summary>The Volume inspector: where and how strongly it applies, then its profile's components.</summary>
[CustomEditor(typeof(Volume))]
public class VolumeEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var volume = (Volume)target;

        PropertyGridUtils.DrawField(paper, $"{id}_global", "Is Global", typeof(bool), volume.IsGlobal, v =>
        {
            Undo.RecordObject(volume, "Change Is Global");
            volume.IsGlobal = (bool)v!;
        });
        if (!volume.IsGlobal)
        {
            PropertyGridUtils.DrawField(paper, $"{id}_size", "Size", typeof(Vector.Float3), volume.Size, v =>
            {
                Undo.RecordObject(volume, "Change Size");
                volume.Size = (Vector.Float3)v!;
            });
            PropertyGridUtils.DrawField(paper, $"{id}_blend", "Blend Distance", typeof(float), volume.BlendDistance, v =>
            {
                Undo.RecordObject(volume, "Change Blend Distance");
                volume.BlendDistance = System.MathF.Max(0f, (float)v!);
            });
        }
        EditorGUI.SliderRow(paper, $"{id}_weight", "Weight", volume.Weight, 0f, 1f, v =>
        {
            Undo.RecordObject(volume, "Change Weight");
            volume.Weight = v;
        });
        PropertyGridUtils.DrawField(paper, $"{id}_priority", "Priority", typeof(float), volume.Priority, v =>
        {
            Undo.RecordObject(volume, "Change Priority");
            volume.Priority = (float)v!;
        });

        VolumeProfileGUI.DrawProfileField(paper, $"{id}_profile", "Profile", volume.SharedProfile, volume.GameObject.Name, p =>
        {
            Undo.RecordObject(volume, "Change Profile");
            volume.SharedProfile = p;
        });

        VolumeProfile? profile = volume.SharedProfile;
        if (profile.IsNotValid()) return;
        VolumeProfileGUI.DrawComponents(paper, $"{id}_list", profile, volume);
        VolumeProfileAssetEditor.DrawPendingBar(paper, $"{id}_pending", profile);
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
                Undo.RecordObject(camera, "New Profile");
                camera.Profile = new VolumeProfile { Name = $"{camera.GameObject.Name} Profile" };
            }).Small().Show();
            if (profile.IsValid())
            {
                Origami.Button(paper, $"{id}_base_clone", $"{EditorIcons.Clone}  Clone", () =>
                {
                    Undo.RecordObject(camera, "Clone Profile");
                    VolumeProfile copy = profile.Clone();
                    copy.Name = $"{profile.Name} Copy";
                    camera.Profile = copy;
                }).Small().Show();
                if (!profile.IsFromDatabase)
                    Origami.Button(paper, $"{id}_base_save", $"{EditorIcons.FloppyDisk}  Save As Asset", () => VolumeProfileGUI.SaveAsAsset(profile)).Small().Show();
            }
        }

        if (profile.IsNotValid()) return;
        VolumeProfileGUI.DrawComponents(paper, $"{id}_base", profile, camera);
        VolumeProfileAssetEditor.DrawPendingBar(paper, $"{id}_base_pending", profile);
    }
}
