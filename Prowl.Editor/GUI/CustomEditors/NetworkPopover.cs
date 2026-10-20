// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using FontFile = Prowl.Scribe.FontFile;
using TextWrapMode = Prowl.Scribe.TextWrapMode;
using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// The GameObject header's network button: an icon that shows how the object networks, opening a popover
/// to choose its <see cref="NetworkMode"/> and the settings of a networked object.
/// </summary>
internal static class NetworkPopover
{
    private const float Width = 320f;

    private static bool s_open;
    private static GameObject? s_target;

    /// <summary>The icon and colour that stand for a mode, here and in the hierarchy.</summary>
    public static (IOrigamiIcon Icon, Color Color) Style(NetworkMode mode) => mode switch
    {
        NetworkMode.Object => (EditorIcons.Wifi_I, EditorTheme.Blue400),
        NetworkMode.Never => (EditorIcons.LinkSlash_I, EditorTheme.InkDim),
        _ => (EditorIcons.Signal_I, EditorTheme.Ink400),
    };

    /// <summary>
    /// The hierarchy's live ownership marker, or null outside play or when the object isn't networked.
    /// </summary>
    public static (IOrigamiIcon Icon, Color Color, string Tooltip)? HierarchyMarker(GameObject go)
    {
        if (!Application.IsPlaying || go.IsNetworkExcluded)
            return null;

        if (go.IsNetworked)
        {
            string owner = go.NetworkOwner is { } peer
                ? peer.Name
                : Loc.Get("network.owner.host");
            string tooltip = Loc.Get("network.controlled_by", new { name = owner });
            return (EditorIcons.Wifi_I, go.IsProxy ? EditorTheme.Amber400 : EditorTheme.Green400, tooltip);
        }

        return null;
    }

    public static void DrawButton(Paper paper, FontFile font, GameObject go)
    {
        (IOrigamiIcon icon, Color color) = go.NetworkMode != NetworkMode.Never && go.IsNetworkExcluded
            ? Style(NetworkMode.Never)
            : Style(go.NetworkMode);

        ElementBuilder trigger = paper.Box("gi_net_btn")
            .Width(26).Height(26).Rounded(EditorTheme.Roundness)
            .BackgroundColor(EditorTheme.Ink100)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .OnClick(go, (g, _) => Toggle(g));

        if (!s_open || s_target != go)
            trigger.Tooltip(Loc.Get("network.title"), ModeName(go.NetworkMode));

        using (trigger.Enter())
        {
            ElementHandle handle = paper.CurrentParent;
            paper.Box("gi_net_icon").Width(26).Height(26).IsNotInteractable().Icon(paper, icon, color, size: 14f);

            if (!s_open || s_target != go)
                return;

            if (paper.IsKeyPressed(PaperKey.Escape))
            {
                Close();
                return;
            }

            MenuTreePopup.Backdrop(paper, "gi_net", Close);
            DrawPopover(paper, font, go, handle);
        }
    }

    private static void Toggle(GameObject go)
    {
        bool opening = !s_open || s_target != go;
        s_open = opening;
        s_target = opening ? go : null;
    }

    private static void Close()
    {
        s_open = false;
        s_target = null;
    }

    private static void DrawPopover(Paper paper, FontFile font, GameObject go, ElementHandle trigger)
    {
        float triggerHeight = trigger.Data.LayoutRect.Size.Y > 0 ? (float)trigger.Data.LayoutRect.Size.Y : 26f;

        using (paper.Column("gi_net_pop")
            .PositionType(PositionType.SelfDirected)
            .Position(0, triggerHeight + 4f)
            .Width(Width).Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Popover)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .DropShadow(0, 14, 40, -6, EditorTheme.Shadow)
            .Rounded(Origami.Current.Metrics.ContainerRounding)
            .Padding(10).Gap(2)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            // Network Mode
            Heading(paper, font, "gi_net_mode", Loc.Get("network.title"), Loc.Get("network.title.desc"));
            foreach (NetworkMode mode in new[] { NetworkMode.Never, NetworkMode.Object, NetworkMode.Snapshot })
            {
                (IOrigamiIcon icon, Color color) = Style(mode);
                NetworkMode chosen = mode;
                Option(paper, font, $"gi_net_mode_{mode}", icon, color, ModeName(mode), go.NetworkMode == mode, () =>
                {
                    Undo.RecordGameObjectChange(go, "Change Network Mode", go.NetworkMode, chosen, (g, x) => g.NetworkMode = x);
                    go.NetworkMode = chosen;
                });
            }
            Note(paper, font, "gi_net_mode_note", ModeDescription(go.NetworkMode), EditorTheme.Ink400);

            // What a parent decides for it.
            GameObject? parent = go.Parent;
            if (go.NetworkMode != NetworkMode.Never && parent.IsValid() && parent!.IsNetworkExcluded)
                Note(paper, font, "gi_net_inherited", Loc.Get("network.inherited_never"), EditorTheme.Amber400);
            else if (parent.IsValid() && parent!.NetworkRoot is { } root)
                Note(paper, font, "gi_net_part_of", Loc.Get("network.part_of", new { name = root.Name }), EditorTheme.Blue400);

            if (go.NetworkMode == NetworkMode.Object)
            {
                Spacer(paper, "gi_net_gap1");

                Heading(paper, font, "gi_net_orphaned", Loc.Get("network.orphaned"), Loc.Get("network.orphaned.desc"));
                OrphanedOption(paper, font, go, NetworkOrphaned.Destroy, EditorIcons.Trash_I, Loc.Get("network.orphaned.destroy"));
                OrphanedOption(paper, font, go, NetworkOrphaned.ClearOwner, EditorIcons.Server_I, Loc.Get("network.orphaned.host"));

                Spacer(paper, "gi_net_gap2");

                Heading(paper, font, "gi_net_flags", Loc.Get("network.flags"), null);
                using (paper.Row("gi_net_sync_tf").Height(UnitValue.Auto).Gap(8).Margin(0, 0, 2, 0).Enter())
                {
                    Origami.Checkbox(paper, "gi_net_sync_tf_cb", go.NetworkSyncTransform, v =>
                    {
                        Undo.RecordGameObjectChange(go, "Toggle Sync Transform", go.NetworkSyncTransform, v, (g, x) => g.NetworkSyncTransform = x);
                        go.NetworkSyncTransform = v;
                    }).NoLabel().Show();

                    using (paper.Column("gi_net_sync_tf_text").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                    {
                        Text(paper, font, "gi_net_sync_tf_lbl", Loc.Get("network.sync_transform"), EditorTheme.Ink500, EditorTheme.FontSizeSmall);
                        Text(paper, font, "gi_net_sync_tf_desc", Loc.Get("network.sync_transform.desc"), EditorTheme.Ink400, EditorTheme.FontSizeSmall - 1);
                    }
                }
            }
        }
    }

    private static void OrphanedOption(Paper paper, FontFile font, GameObject go, NetworkOrphaned value, IOrigamiIcon icon, string label) =>
        Option(paper, font, $"gi_net_orph_{value}", icon, EditorTheme.Ink400, label, go.NetworkOrphaned == value, () =>
        {
            Undo.RecordGameObjectChange(go, "Change Orphaned Mode", go.NetworkOrphaned, value, (g, x) => g.NetworkOrphaned = x);
            go.NetworkOrphaned = value;
        });

    private static string ModeName(NetworkMode mode) => mode switch
    {
        NetworkMode.Object => Loc.Get("network.object"),
        NetworkMode.Never => Loc.Get("network.never"),
        _ => Loc.Get("network.snapshot"),
    };

    private static string ModeDescription(NetworkMode mode) => mode switch
    {
        NetworkMode.Object => Loc.Get("network.object.desc"),
        NetworkMode.Never => Loc.Get("network.never.desc"),
        _ => Loc.Get("network.snapshot.desc"),
    };

    // --- Building blocks ---------------------------------------------------------------------------

    private static void Heading(Paper paper, FontFile font, string id, string title, string? description)
    {
        Text(paper, font, id, title, EditorTheme.Ink500, EditorTheme.FontSize, EditorTheme.FontSemiBold ?? font);
        if (description != null)
            Text(paper, font, id + "_desc", description, EditorTheme.Ink400, EditorTheme.FontSizeSmall - 1);
    }

    /// <summary>A radio row: the circle, the option's icon and its name.</summary>
    private static void Option(Paper paper, FontFile font, string id, IOrigamiIcon icon, Color iconColor, string label, bool selected, Action choose)
    {
        using (paper.Row(id)
            .Height(EditorTheme.RowHeight).Gap(8).PaddingLeft(4)
            .Rounded(EditorTheme.Roundness)
            .Hovered.BackgroundColor(EditorTheme.Ink100).End()
            .OnClick(choose, (c, _) => c())
            .Enter())
        {
            using (paper.Box(id + "_radio")
                .Width(14).Height(14).Margin(0, UnitValue.StretchOne)
                .Rounded(7).BorderWidth(1.5f)
                .BorderColor(selected ? EditorTheme.Accent : EditorTheme.Ink300)
                .IsNotInteractable()
                .Enter())
            {
                if (selected)
                    paper.Box(id + "_dot").Width(6).Height(6).Rounded(3).Margin(UnitValue.StretchOne).BackgroundColor(EditorTheme.Accent).IsNotInteractable();
            }

            paper.Box(id + "_icon").Width(16).Height(EditorTheme.RowHeight).IsNotInteractable().Icon(paper, icon, iconColor, size: 13f);
            paper.Box(id + "_label").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).IsNotInteractable()
                .Text(label, font).TextColor(selected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
    }

    private static void Note(Paper paper, FontFile font, string id, string text, Color color) =>
        Text(paper, font, id, text, color, EditorTheme.FontSizeSmall - 1);

    private static void Spacer(Paper paper, string id) =>
        paper.Box(id).Height(8).IsNotInteractable();

    private static void Text(Paper paper, FontFile font, string id, string text, Color color, float size, FontFile? face = null) =>
        paper.Box(id)
            .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .IsNotInteractable()
            .Text(text, face ?? font).TextColor(color).FontSize(size)
            .Wrap(TextWrapMode.Wrap)
            .Alignment(TextAlignment.MiddleLeft);
}
