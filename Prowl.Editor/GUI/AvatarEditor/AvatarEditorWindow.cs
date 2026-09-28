// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Motion;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Canvas = Prowl.Quill.Canvas;
using MotionAvatar = Prowl.Motion.Avatar;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Maps a model's skeleton onto the human body by hand: pick a humanoid slot, then click its bone in the
/// preview. The mapping is saved in the model's import settings and laid over the auto mapper's.
/// </summary>
public class AvatarEditorWindow : DockPanel
{
    private const float DotRadius = 4.5f;
    private const float HitRadius = 9f;

    /// <summary>The humanoid bones, grouped the way a person looks for them.</summary>
    private static readonly (string Name, HumanBodyBone[] Bones)[] s_groups =
    {
        ("Body", new[] { HumanBodyBone.Hips, HumanBodyBone.Spine, HumanBodyBone.Chest, HumanBodyBone.UpperChest }),
        ("Head", new[] { HumanBodyBone.Neck, HumanBodyBone.Head, HumanBodyBone.LeftEye, HumanBodyBone.RightEye, HumanBodyBone.Jaw }),
        ("Left Arm", new[] { HumanBodyBone.LeftShoulder, HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftHand }),
        ("Right Arm", new[] { HumanBodyBone.RightShoulder, HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm, HumanBodyBone.RightHand }),
        ("Left Leg", new[] { HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftFoot, HumanBodyBone.LeftToes }),
        ("Right Leg", new[] { HumanBodyBone.RightUpperLeg, HumanBodyBone.RightLowerLeg, HumanBodyBone.RightFoot, HumanBodyBone.RightToes }),
    };

    private Guid _modelGuid;
    private string _modelName = "";
    private PreviewRenderer? _preview;
    private MotionSkeleton? _skeleton;

    /// <summary>Humanoid bone to skeleton bone name. This is what gets written back.</summary>
    private readonly Dictionary<HumanBodyBone, string> _map = new();

    /// <summary>Which skeleton bones are worth looking at, for a rig with more of them than fit.</summary>
    private enum BoneView { All, Unmapped, Mapped }

    private HumanBodyBone? _slot;
    private int _hoveredBone = -1;
    private string _boneFilter = "";
    private BoneView _boneView;
    private bool _dirty;

    // Where the preview image sits on screen this frame, so a click on a bone can be matched to it.
    private float _viewX, _viewY, _viewW, _viewH;

    public override string Title => _modelName.Length > 0 ? $"Avatar: {_modelName}" : "Avatar Editor";

    public override string Icon => EditorIcons.Bone;

    public static void OpenFor(Guid modelGuid)
    {
        var panel = new AvatarEditorWindow();
        panel.Load(modelGuid);
        EditorApplication.Instance?.OpenPanelInstance(panel, 1080, 700);
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_modelGuid == Guid.Empty) return false;
        state["model"] = _modelGuid.ToString();
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        if (state["model"]?.GetValue<string>() is { } text && Guid.TryParse(text, out Guid guid)) Load(guid);
    }

    public override void OnClosed()
    {
        _preview = null;
        _skeleton = null;
    }

    private void Load(Guid modelGuid)
    {
        _modelGuid = modelGuid;
        _map.Clear();
        _slot = null;
        _dirty = false;

        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(modelGuid);
        _modelName = entry != null ? Path.GetFileNameWithoutExtension(entry.Path) : "";
        if (entry == null) return;

        // The preview owns the instantiated hierarchy the gizmos are drawn from.
        PrefabAsset? model = new AssetRef<PrefabAsset>(modelGuid).Res;
        if (model.IsValid())
        {
            _preview = new PreviewRenderer(512, 512) { ShowGrid = true };
            _preview.SetupForPrefab(model!);
            _preview.PoseAt(null, 0f);
            _skeleton = _preview.Skeleton;
        }

        ReadMapping(entry);
    }

    /// <summary>Starts from the rig's current mapping.</summary>
    private void ReadMapping(AssetEntry entry)
    {
        if (_skeleton == null) return;

        Runtime.Avatar? avatar = FindAvatar(entry);
        HumanDescription? description = avatar.IsValid() ? avatar!.Description : null;
        if (description != null)
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                var bone = (HumanBodyBone)i;
                int index = description.GetSkeletonBoneIndex(bone);
                if (index >= 0 && index < _skeleton.BoneCount)
                    _map[bone] = _skeleton.GetBoneID(index).DebugName ?? "";
            }

        // Anything already written by hand wins over it.
        EchoObject settings = ReadSettings(entry);
        if (ModelImportOverrides.ReadHumanoidMap(settings) is { } stored)
            foreach (KeyValuePair<string, string> pair in stored)
                if (Enum.TryParse(pair.Key, ignoreCase: true, out HumanBodyBone bone))
                {
                    if (pair.Value.Length == 0) _map.Remove(bone);
                    else _map[bone] = pair.Value;
                }
    }

    private static Runtime.Avatar? FindAvatar(AssetEntry entry)
    {
        foreach (var sub in entry.SubAssets)
        {
            if (sub.Type == null || !typeof(Runtime.Avatar).IsAssignableFrom(sub.Type)) continue;
            Runtime.Avatar? avatar = new AssetRef<Runtime.Avatar>(sub.Guid).Res;
            if (avatar.IsValid()) return avatar;
        }
        return null;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;

        if (_preview == null || _skeleton == null)
        {
            paper.Box("avatar_empty").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Text("This model has no rig to map. Set its Rig to Humanoid and reimport.", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        using (paper.Column("avatar_root").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
        {
            DrawToolbar(paper, font, m, width);

            using (paper.Row("avatar_body").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float panelW = Math.Clamp(width * 0.42f, 300f, 460f);
                DrawViewport(paper, font, width - panelW, height - 44f);
                DrawSlots(paper, font, m, panelW, height - 44f);
            }
        }
    }

    // ---- toolbar ------------------------------------------------------------------------------

    private void DrawToolbar(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width)
    {
        int mapped = 0, missingRequired = 0;
        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            var bone = (HumanBodyBone)i;
            if (IsMapped(bone)) mapped++;
            else if (HumanTrait.IsRequired(bone)) missingRequired++;
        }

        using (paper.Row("avatar_toolbar").Width(UnitValue.Stretch()).Height(40)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.SpacingMedium)
            .BackgroundColor(EditorTheme.Neutral200)
            .AlignItems(LayoutAlignment.Center).Enter())
        {
            string status = missingRequired == 0
                ? $"{mapped} bones mapped, every required one present"
                : $"{mapped} bones mapped, {missingRequired} required still missing";

            paper.Box("avatar_status").Width(UnitValue.Stretch()).Height(26).IsNotInteractable()
                .Text(status, font)
                .TextColor(missingRequired == 0 ? EditorTheme.Green400 : EditorTheme.Amber400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            EditorGUI.PillButton(paper, "avatar_auto", "Auto Map", false, AutoMap);
            EditorGUI.PillButton(paper, "avatar_clear", "Clear", false, () => { _map.Clear(); _dirty = true; });
            EditorGUI.PillButton(paper, "avatar_save", _dirty ? "Save and Reimport" : "Saved", _dirty, Save);
        }
    }

    /// <summary>Throws the hand made map away and takes whatever the auto mapper finds.</summary>
    private void AutoMap()
    {
        if (_skeleton == null) return;

        _map.Clear();
        Runtime.Avatar avatar = Runtime.Avatar.CreateAutomatic(_skeleton, out HumanoidMapResult report);
        HumanDescription? description = avatar.Description;
        if (description != null)
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                var bone = (HumanBodyBone)i;
                int index = description.GetSkeletonBoneIndex(bone);
                if (index >= 0) _map[bone] = _skeleton.GetBoneID(index).DebugName ?? "";
            }

        if (!report.IsHumanoid)
            Debug.LogWarning($"[Avatar] The auto mapper still could not recognise a humanoid. Missing: {string.Join(", ", report.UnmappedBones)}.");
        _dirty = true;
    }

    private void Save()
    {
        if (!_dirty) return;

        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(_modelGuid);
        if (entry == null) return;

        // Every humanoid bone is written, including the ones left empty: an empty entry is what clears a
        // bone the auto mapper claimed and should not have.
        var map = new Dictionary<string, string>();
        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            var bone = (HumanBodyBone)i;
            map[bone.ToString()] = _map.TryGetValue(bone, out string? name) ? name : "";
        }

        EchoObject settings = ReadSettings(entry);
        ModelImportOverrides.WriteHumanoidMap(settings, map);
        if (!SaveSettings(entry, settings)) return;

        _dirty = false;
        EditorAssetBackend.Instance?.Reimport(_modelGuid);
    }

    // ---- viewport -----------------------------------------------------------------------------

    private void DrawViewport(Paper paper, Scribe.FontFile font, float width, float height)
    {
        using (paper.Box("avatar_viewport").Width(width).Height(height)
            .BackgroundColor(EditorTheme.Neutral300)
            .JustifyContent(LayoutJustification.Center).AlignItems(LayoutAlignment.Center).Enter())
        {
            float size = MathF.Max(64f, MathF.Min(width, height) - 24f);

            // The gizmos are painted by the preview element, not by one laid over it: an overlay on top
            // eats the drag and the wheel, and then the camera cannot be moved at all.
            _preview!.DrawPreview(paper, "avatar_preview", size, size,
                (canvas, rect) =>
                {
                    _viewX = (float)rect.Min.X; _viewY = (float)rect.Min.Y;
                    _viewW = (float)rect.Size.X; _viewH = (float)rect.Size.Y;
                    PaintGizmos(canvas);
                },
                point => ClickBone(point));

            HoverBone(paper);

            if (_slot.HasValue)
                paper.Box("avatar_hint")
                    .PositionType(PositionType.SelfDirected).Left(12).Top(12)
                    .Width(UnitValue.Auto).Height(24).Padding(10, 10, 0, 0)
                    .Rounded(Origami.Current.Metrics.SmallRounding)
                    .BackgroundColor(EditorTheme.Accent).IsNotInteractable()
                    .Text($"Click the bone for {Pretty(_slot.Value)}", font)
                    .TextColor(System.Drawing.Color.White).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter);
        }
    }

    private void PaintGizmos(Canvas canvas)
    {
        if (_skeleton == null || _preview == null) return;

        int assigned = _slot.HasValue ? BoneIndexOf(_slot.Value) : -1;

        for (int b = 0; b < _skeleton.BoneCount; b++)
        {
            if (!IsDrawn(b) || !TryScreen(b, out Float2 point)) continue;

            bool isAssigned = b == assigned;
            bool isHovered = b == _hoveredBone;

            Color32 fill = isAssigned ? EditorTheme.ToColor32(EditorTheme.Accent, 1f)
                : isHovered ? EditorTheme.ToColor32(EditorTheme.Amber400, 1f)
                : IsBoneUsed(b) ? EditorTheme.ToColor32(EditorTheme.Green400, 0.95f)
                : EditorTheme.ToColor32(EditorTheme.Ink300, 0.7f);

            canvas.CircleFilled(point.X, point.Y, isAssigned || isHovered ? DotRadius + 2f : DotRadius, fill);
        }
    }

    /// <summary>Whether a bone is drawn, following the same filter as the list.</summary>
    private bool IsDrawn(int bone)
    {
        if (_skeleton == null) return false;

        if (_boneFilter.Length > 0)
        {
            string name = _skeleton.GetBoneID(bone).DebugName ?? "";
            if (!name.Contains(_boneFilter, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return _boneView switch
        {
            BoneView.Mapped => IsBoneUsed(bone),
            BoneView.Unmapped => !IsBoneUsed(bone),
            _ => true,
        };
    }

    private bool TryScreen(int bone, out Float2 point)
    {
        point = default;
        if (_preview == null || !_preview.TryProjectBone(bone, out Float2 normalized)) return false;
        if (normalized.X < -0.2f || normalized.X > 1.2f || normalized.Y < -0.2f || normalized.Y > 1.2f) return false;

        point = new Float2(_viewX + normalized.X * _viewW, _viewY + normalized.Y * _viewH);
        return true;
    }

    // Only what the pointer is over in the viewport counts as hovered. The bone list has its own row
    // highlight, and letting both write this would make them take turns each frame.
    private void HoverBone(Paper paper)
    {
        float x = (float)paper.PointerPos.X, y = (float)paper.PointerPos.Y;
        bool inside = x >= _viewX && x <= _viewX + _viewW && y >= _viewY && y <= _viewY + _viewH;
        _hoveredBone = inside ? NearestBone(x, y) : -1;
    }

    private void ClickBone(Float2 point)
    {
        if (!_slot.HasValue) return;

        int bone = NearestBone(point.X, point.Y);
        if (bone < 0) return;
        Assign(bone);
    }

    private void Assign(int bone)
    {
        if (_skeleton == null || !_slot.HasValue) return;

        _map[_slot.Value] = _skeleton.GetBoneID(bone).DebugName ?? "";
        _dirty = true;
        _slot = NextSlotAfter(_slot.Value);
    }

    private int NearestBone(float x, float y)
    {
        if (_skeleton == null) return -1;

        int best = -1;
        float bestDistance = HitRadius * HitRadius;
        for (int b = 0; b < _skeleton.BoneCount; b++)
        {
            if (!IsDrawn(b) || !TryScreen(b, out Float2 point)) continue;

            float dx = point.X - x, dy = point.Y - y;
            float distance = dx * dx + dy * dy;
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = b;
        }
        return best;
    }

    // ---- the body map -------------------------------------------------------------------------

    private void DrawSlots(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        // The slots take the larger share: the bone list is a way out of an overlap, not the main way
        // of working.
        float boneListHeight = MathF.Max(150f, (height - 76f) * 0.4f);
        float slotsHeight = MathF.Max(120f, height - boneListHeight - 76f);

        using (paper.Column("avatar_panel").Width(width).Height(height)
            .BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            using (paper.Row("avatar_slotRow").Width(UnitValue.Stretch()).Height(36)
                .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.SpacingMedium)
                .AlignItems(LayoutAlignment.Center).Enter())
            {
                paper.Box("avatar_slotTitle").Width(UnitValue.Stretch()).Height(26).IsNotInteractable()
                    .Text("Humanoid Bones", EditorTheme.FontSemiBold ?? font)
                    .TextColor(EditorTheme.Ink700).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            Origami.ScrollView(paper, "avatar_slots", width - 4f, slotsHeight).Body(() =>
            {
                foreach ((string name, HumanBodyBone[] bones) in s_groups)
                    DrawGroup(paper, font, m, name, bones);

                DrawGroup(paper, font, m, "Fingers", FingerBones());
            });

            DrawBoneList(paper, font, m, width, boneListHeight);
        }
    }

    /// <summary>Every bone of the rig as a list, for bones the preview dots cannot separate.</summary>
    private void DrawBoneList(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        using (paper.Row("avatar_boneRow").Width(UnitValue.Stretch()).Height(40)
            .Padding(m.PaddingLarge, m.PaddingLarge, m.Spacing, 0).Gap(m.SpacingMedium)
            .AlignItems(LayoutAlignment.Center).Enter())
        {
            Origami.SearchField(paper, "avatar_filter", _boneFilter, v => _boneFilter = v, "Filter bones...").Show();
            EditorGUI.PillButton(paper, "avatar_view", $"Show: {_boneView}", _boneView != BoneView.All,
                () => _boneView = _boneView switch
                {
                    BoneView.All => BoneView.Unmapped,
                    BoneView.Unmapped => BoneView.Mapped,
                    _ => BoneView.All,
                });
        }

        Origami.ScrollView(paper, "avatar_bones", width - 4f, height).Body(() =>
        {
            if (_skeleton == null) return;

            int shown = 0;
            for (int b = 0; b < _skeleton.BoneCount; b++)
            {
                if (!IsDrawn(b)) continue;
                DrawBoneRow(paper, font, m, b);
                shown++;
            }

            if (shown == 0)
                paper.Box("avatar_noBones").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                    .Text("No bones match.", font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
        });
    }

    private void DrawBoneRow(Paper paper, Scribe.FontFile font, OrigamiMetrics m, int bone)
    {
        string name = _skeleton!.GetBoneID(bone).DebugName ?? $"Bone {bone}";
        HumanBodyBone? usedBy = SlotUsing(name);

        string id = $"avatar_bone_{bone}";
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(m.RowHeight)
            .Margin(m.PaddingLarge, m.PaddingLarge, 1, 1).Padding(m.Padding, m.Padding, 0, 0)
            .Rounded(m.SmallRounding)
            .BackgroundColor(bone == _hoveredBone ? EditorTheme.Neutral400 : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
            .AlignItems(LayoutAlignment.Center)
            .OnClick(0, (_, _) => Assign(bone))
            .Enter())
        {
            paper.Box($"{id}_name").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                .Text(name, font)
                .TextColor(usedBy.HasValue ? EditorTheme.Green400 : EditorTheme.Ink700)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            if (usedBy.HasValue)
                paper.Box($"{id}_used").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                    .Text(Pretty(usedBy.Value), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
        }
    }

    private HumanBodyBone? SlotUsing(string boneName)
    {
        foreach (KeyValuePair<HumanBodyBone, string> pair in _map)
            if (pair.Value == boneName) return pair.Key;
        return null;
    }

    private void DrawGroup(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string title, HumanBodyBone[] bones)
    {
        EditorGUI.SectionHeader(paper, $"avatar_g_{title}", title, first: title == "Body");

        foreach (HumanBodyBone bone in bones)
            DrawSlot(paper, font, m, bone);
    }

    private void DrawSlot(Paper paper, Scribe.FontFile font, OrigamiMetrics m, HumanBodyBone bone)
    {
        bool selected = _slot == bone;
        bool mapped = IsMapped(bone);
        bool required = HumanTrait.IsRequired(bone);
        string assigned = _map.TryGetValue(bone, out string? name) && name.Length > 0 ? name : "none";

        string id = $"avatar_slot_{bone}";
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(m.RowHeight + 4)
            .Margin(m.PaddingLarge, m.PaddingLarge, 1, 1).Padding(m.Padding, m.Padding, 0, 0)
            .Rounded(m.SmallRounding)
            .BackgroundColor(selected ? EditorTheme.Accent : EditorTheme.Neutral300)
            .Hovered.BackgroundColor(selected ? EditorTheme.Accent : EditorTheme.Neutral400).End()
            .AlignItems(LayoutAlignment.Center)
            .OnClick(0, (_, _) => _slot = selected ? null : bone)
            .Enter())
        {
            paper.Box($"{id}_name").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                .Text(required ? Pretty(bone) + " *" : Pretty(bone), font)
                .TextColor(selected ? System.Drawing.Color.White : EditorTheme.Ink700)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_value").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                .Text(assigned, font)
                .TextColor(selected ? System.Drawing.Color.White
                    : mapped ? EditorTheme.Green400
                    : required ? EditorTheme.Amber400 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

            if (mapped)
                paper.Box($"{id}_clear").Width(22).Height(m.RowHeight).Margin(m.SpacingMedium, 0, 0, 0)
                    .Text(EditorIcons.Xmark, font)
                    .TextColor(selected ? System.Drawing.Color.White : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => { _map.Remove(bone); _dirty = true; });
        }
    }

    // ---- helpers ------------------------------------------------------------------------------

    private bool IsMapped(HumanBodyBone bone) => BoneIndexOf(bone) >= 0;

    private int BoneIndexOf(HumanBodyBone bone)
    {
        if (_skeleton == null || !_map.TryGetValue(bone, out string? name) || name.Length == 0) return -1;
        return _skeleton.GetBoneIndex(new StringID(name));
    }

    private bool IsBoneUsed(int boneIndex)
    {
        if (_skeleton == null) return false;
        string name = _skeleton.GetBoneID(boneIndex).DebugName ?? "";
        if (name.Length == 0) return false;

        foreach (string mapped in _map.Values)
            if (mapped == name) return true;
        return false;
    }

    /// <summary>The next slot to fill after one is assigned.</summary>
    private HumanBodyBone? NextSlotAfter(HumanBodyBone bone)
    {
        foreach ((_, HumanBodyBone[] bones) in s_groups)
        {
            int at = Array.IndexOf(bones, bone);
            if (at < 0) continue;

            for (int i = at + 1; i < bones.Length; i++)
                if (!IsMapped(bones[i])) return bones[i];
            return null;
        }
        return null;
    }

    private static HumanBodyBone[] FingerBones()
    {
        var bones = new List<HumanBodyBone>();
        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            string name = ((HumanBodyBone)i).ToString();
            if (name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle")
                || name.Contains("Ring") || name.Contains("Little"))
                bones.Add((HumanBodyBone)i);
        }
        return bones.ToArray();
    }

    /// <summary>"LeftUpperArm" reads better as "Left Upper Arm".</summary>
    private static string Pretty(HumanBodyBone bone)
    {
        string name = bone.ToString();
        var text = new System.Text.StringBuilder(name.Length + 6);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) text.Append(' ');
            text.Append(name[i]);
        }
        return text.ToString();
    }



    private static EchoObject ReadSettings(AssetEntry entry)
    {
        string? metaPath = MetaPath(entry);
        if (metaPath == null || !File.Exists(metaPath)) return EchoObject.NewCompound();
        return MetaFile.Read(metaPath).Settings ?? EchoObject.NewCompound();
    }

    private static bool SaveSettings(AssetEntry entry, EchoObject settings)
    {
        string? metaPath = MetaPath(entry);
        if (metaPath == null || !File.Exists(metaPath)) return false;

        // Merge rather than replace: the inspector may have written keys this window never read.
        MetaFileData meta = MetaFile.Read(metaPath);
        EchoObject merged = meta.Settings ?? EchoObject.NewCompound();
        foreach (KeyValuePair<string, EchoObject> kvp in settings.Tags)
            merged[kvp.Key] = kvp.Value.Clone();

        meta.Settings = merged;
        MetaFile.Write(metaPath, meta);
        return true;
    }

    private static string? MetaPath(AssetEntry entry)
    {
        if (Project.Current == null) return null;
        return MetaFile.GetMetaPath(Path.Combine(Project.Current.AssetsPath, entry.Path));
    }
}
