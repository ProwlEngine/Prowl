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
using Prowl.Runtime.Networking;

using FontFile = Prowl.Scribe.FontFile;
using TextWrapMode = Prowl.Scribe.TextWrapMode;
using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// The GameObject header's network button: an icon that shows how the object networks, opening a popover
/// to make it networked or server only, with the settings of a networked object under an advanced fold.
/// </summary>
internal static class NetworkPopover
{
    private const float Width = 320f;

    private static bool s_open;
    private static GameObject? s_target;

    private enum Sharing { Off, Networked, ServerOnly }

    private static Sharing SharingOf(GameObject go) =>
        NetworkAccessor.ServerOnlyOf(go) ? Sharing.ServerOnly
        : NetworkAccessor.NetworkedOf(go) ? Sharing.Networked
        : Sharing.Off;

    private static (IOrigamiIcon Icon, Color Color) Style(Sharing sharing) => sharing switch
    {
        Sharing.Networked => (EditorIcons.Wifi_I, EditorTheme.Blue400),
        Sharing.ServerOnly => (EditorIcons.LinkSlash_I, EditorTheme.InkDim),
        _ => (EditorIcons.Signal_I, EditorTheme.Ink400),
    };

    /// <summary>True when the object has networked components that nothing sends, since it is not part of a networked object.</summary>
    private static bool NeedsNetworking(GameObject go) =>
        go.Network.Root.IsNotValid() && !NetworkAccessor.IsExcludedFromNetwork(go) && NetworkComponents.Gather(go).Length > 0;

    /// <summary>
    /// The hierarchy's live ownership marker, or null outside play or when the object isn't networked.
    /// </summary>
    public static (IOrigamiIcon Icon, Color Color, string Tooltip)? HierarchyMarker(GameObject go)
    {
        if (!Application.IsPlaying || NetworkAccessor.IsExcludedFromNetwork(go))
            return null;

        if (go.Network.Active)
        {
            string owner = go.Network.Owner is { } peer
                ? peer.Name
                : Loc.Get("network.owner.host");
            string tooltip = Loc.Get("network.controlled_by", new { name = owner });
            return (EditorIcons.Wifi_I, go.IsProxy ? EditorTheme.Amber400 : EditorTheme.Green400, tooltip);
        }

        return null;
    }

    public static void DrawButton(Paper paper, FontFile font, GameObject go)
    {
        Sharing sharing = SharingOf(go);
        bool needsNetworking = NeedsNetworking(go);
        (IOrigamiIcon icon, Color color) = NetworkAccessor.IsExcludedFromNetwork(go) ? Style(Sharing.ServerOnly) : Style(sharing);
        if (needsNetworking)
            color = EditorTheme.Amber400;

        ElementBuilder trigger = paper.Box("gi_net_btn")
            .Width(26).Height(26).Rounded(EditorTheme.Roundness)
            .Cursor(PaperCursor.Pointer)
            .BackgroundColor(EditorTheme.Ink100)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
            .OnClick(go, (g, _) => Toggle(g));

        if (!s_open || s_target != go)
            trigger.Tooltip(Loc.Get("network.title"), needsNetworking ? Loc.Get("network.needs_networking") : SharingName(sharing));

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
            Heading(paper, font, "gi_net_mode", Loc.Get("network.title"), Loc.Get("network.title.desc"));

            // A networked object can't hold another, so inside one there is nothing to choose.
            GameObject? parent = go.Parent;
            GameObject? outer = parent.IsValid() ? parent!.Network.Root : null;
            Sharing current = SharingOf(go);
            if (outer.IsValid())
            {
                Note(paper, font, "gi_net_part_of", Loc.Get("network.part_of", new { name = outer!.Name }), EditorTheme.Blue400);
                Live(paper, font, go);
                ComponentIntervals(paper, font, go);
                return;
            }

            foreach (Sharing sharing in new[] { Sharing.Off, Sharing.Networked, Sharing.ServerOnly })
            {
                (IOrigamiIcon icon, Color color) = Style(sharing);
                Sharing chosen = sharing;
                Option(paper, font, $"gi_net_mode_{sharing}", icon, color, SharingName(sharing), current == sharing, () =>
                {
                    Undo.RecordObject(go, "Change Networking");
                    go.Network.Networked = chosen == Sharing.Networked;
                    go.Network.ServerOnly = chosen == Sharing.ServerOnly;
                });
            }
            Note(paper, font, "gi_net_mode_note", SharingDescription(current), EditorTheme.Ink400);

            if (current != Sharing.ServerOnly && parent.IsValid() && parent!.Network.IsExcluded)
                Note(paper, font, "gi_net_inherited", Loc.Get("network.inherited_never"), EditorTheme.Amber400);
            else if (NeedsNetworking(go))
                Note(paper, font, "gi_net_needs", Loc.Get("network.needs_networking"), EditorTheme.Amber400);

            if (current != Sharing.Networked)
                return;

            int components = NetworkComponents.Gather(go).Length;
            Note(paper, font, "gi_net_components",
                Loc.Get("network.components", new { count = components, max = NetworkComponents.MaxPerObject }),
                components > NetworkComponents.MaxPerObject ? EditorTheme.Red400 : EditorTheme.Ink400);

            Spacer(paper, "gi_net_gap0");
            SyncTransform(paper, font, go);
            Live(paper, font, go);

            Spacer(paper, "gi_net_gap1");
            Origami.Foldout(paper, "gi_net_advanced", Loc.Get("network.advanced")).Body(() =>
            {
                Heading(paper, font, "gi_net_orphaned", Loc.Get("network.orphaned"), Loc.Get("network.orphaned.desc"));
                OrphanedOption(paper, font, go, NetworkOrphaned.Destroy, EditorIcons.Trash_I, Loc.Get("network.orphaned.destroy"));
                OrphanedOption(paper, font, go, NetworkOrphaned.ClearOwner, EditorIcons.Server_I, Loc.Get("network.orphaned.host"));

                Spacer(paper, "gi_net_gap_vis");
                Receivers(paper, font, go);

                ComponentIntervals(paper, font, go);
            });
        }
    }

    /// <summary>Who receives the object: its visibility rule, the groups it is in, and how far away it is still sent.</summary>
    private static void Receivers(Paper paper, FontFile font, GameObject go)
    {
        Heading(paper, font, "gi_net_vis", Loc.Get("network.visibility"), Loc.Get("network.visibility.desc"));
        if (go.Network.Active && !Networking.IsHost)
        {
            Note(paper, font, "gi_net_vis_ro", Loc.Get("network.visibility.host_only"), EditorTheme.Ink400);
            return;
        }

        string[] rules =
        [
            Loc.Get("network.visibility.default"), Loc.Get("network.visibility.always"),
            Loc.Get("network.visibility.owner_only"), Loc.Get("network.visibility.manual"),
        ];
        Origami.Dropdown(paper, "gi_net_vis_v", (int)go.Network.Visibility, v =>
        {
            Undo.RecordObject(go, "Change Network Visibility");
            go.Network.Visibility = (NetworkVisibility)v;
        }, rules).Show();

        LabeledRow(paper, font, "gi_net_cull", Loc.Get("network.cull_distance"), () =>
            Origami.NumericField<float>(paper, "gi_net_cull_v", go.Network.CullDistance, v =>
            {
                Undo.RecordObject(go, "Change Cull Distance");
                go.Network.CullDistance = float.IsFinite(v) ? MathF.Max(0f, v) : 0f;
            }).Min(0).Show());

        LabeledRow(paper, font, "gi_net_groups", Loc.Get("network.groups"), () =>
            Origami.TextField(paper, "gi_net_groups_v", GroupList(go.Network.Groups), text =>
            {
                Undo.RecordObject(go, "Change Network Groups");
                go.Network.Groups = ParseGroups(text);
            }).CommitOnEnter().Show());
        Note(paper, font, "gi_net_groups_desc", Loc.Get("network.groups.desc"), EditorTheme.Ink400);
    }

    private static string GroupList(NetworkGroups groups)
    {
        System.Collections.Generic.List<string> numbers = [];
        for (int group = 0; group < NetworkGroups.Count; group++)
        {
            if (groups.Contains(group))
                numbers.Add(group.ToString());
        }

        return string.Join(", ", numbers);
    }

    /// <summary>The groups a list of numbers names. What is not a number from 0 to 63 is left out.</summary>
    private static NetworkGroups ParseGroups(string text)
    {
        NetworkGroups groups = NetworkGroups.None;
        foreach (string part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out int group) && group >= 0 && group < NetworkGroups.Count)
                groups = groups.With(group);
        }

        return groups;
    }

    private static void LabeledRow(Paper paper, FontFile font, string id, string label, Action control)
    {
        using (paper.Row(id).Height(EditorTheme.RowHeight).Gap(8).Enter())
        {
            paper.Box(id + "_name").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            control();
        }
    }

    /// <summary>A send interval for each networked component on the object itself, as Mirror's syncInterval.</summary>
    private static void ComponentIntervals(Paper paper, FontFile font, GameObject go)
    {
        bool any = false;
        int row = 0;
        foreach (Component component in go.GetComponents<Component>())
        {
            if (!NetworkMembers.Of(component).Any)
            {
                continue;
            }

            if (!any)
            {
                any = true;
                Spacer(paper, "gi_net_gap_ci");
                Heading(paper, font, "gi_net_ci", Loc.Get("network.component_interval"), Loc.Get("network.component_interval.desc"));
            }

            Component target = component;
            string id = $"gi_net_ci_{row++}";
            using (paper.Row(id).Height(EditorTheme.RowHeight).Gap(8).Enter())
            {
                paper.Box(id + "_name").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).IsNotInteractable()
                    .Text(component.GetType().Name, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                Origami.NumericField<int>(paper, id + "_v", go.Network.GetSendInterval(component), v =>
                {
                    Undo.RecordObject(go, "Change Component Send Interval");
                    go.Network.SetSendInterval(target, Math.Clamp(v, 1, 255));
                }).Min(1).Max(255).Show();
            }
        }
    }

    /// <summary>While playing: the object's id, owner, who controls it and, on the host, how many clients have it.</summary>
    private static void Live(Paper paper, FontFile font, GameObject go)
    {
        if (!Application.IsPlaying || Networking.Session == null)
            return;

        Spacer(paper, "gi_net_gap_live");
        Heading(paper, font, "gi_net_live", Loc.Get("network.live"), null);
        if (!NetworkDiagnostics.TryGetObjectDiagnostic(go, out NetworkDiagnostics.ObjectDiagnostic live))
        {
            Note(paper, font, "gi_net_live_none", Loc.Get("network.not_spawned"), EditorTheme.Ink400);
            return;
        }

        string owner = go.Network.Owner is { } peer ? peer.Name : Loc.Get("network.owner.host");
        LiveRow(paper, font, "gi_net_live_id", Loc.Get("network.net_id"), live.NetId.ToString());
        LiveRow(paper, font, "gi_net_live_owner", Loc.Get("network.owner"), owner);
        LiveRow(paper, font, "gi_net_live_control", Loc.Get("network.control"),
            Loc.Get(live.Controlled ? "network.controlled" : "network.proxy"));
        if (Networking.IsHost)
            LiveRow(paper, font, "gi_net_live_observers", Loc.Get("network.observers"), live.Observers.ToString());
    }

    private static void LiveRow(Paper paper, FontFile font, string id, string label, string value)
    {
        using (paper.Row(id).Height(EditorTheme.RowHeight).Gap(8).Enter())
        {
            paper.Box(id + "_label").Width(96).Height(EditorTheme.RowHeight).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            paper.Box(id + "_value").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
    }

    /// <summary>Adds or removes the object's NetworkTransform, whose own settings show with its other components.</summary>
    private static void SyncTransform(Paper paper, FontFile font, GameObject go)
    {
        const string id = "gi_net_sync_tf";
        NetworkTransform? transform = go.GetComponent<NetworkTransform>();
        bool enabled = transform.IsValid();
        string label = Loc.Get("network.sync_transform");

        // A spawned object's transforms are fixed, as the list every peer built at spawn.
        if (go.Network.Active)
        {
            Text(paper, font, id, $"{(enabled ? EditorIcons.Check : EditorIcons.Xmark)}  {label}", EditorTheme.Ink500, EditorTheme.FontSizeSmall);
        }
        else
        {
            Origami.Checkbox(paper, id, enabled, on =>
            {
                if (on)
                    Undo.AddComponent<NetworkTransform>(go);
                else if (transform.IsValid())
                    Undo.DestroyObjectImmediate(transform!);
            }).LabelRight(label).Show();
        }

        Note(paper, font, id + "_desc", Loc.Get("network.sync_transform.desc"), EditorTheme.Ink400);
    }

    private static void OrphanedOption(Paper paper, FontFile font, GameObject go, NetworkOrphaned value, IOrigamiIcon icon, string label) =>
        Option(paper, font, $"gi_net_orph_{value}", icon, EditorTheme.Ink400, label, go.Network.Orphaned == value, () =>
        {
            Undo.RecordObject(go, "Change Orphaned Mode");
            go.Network.Orphaned = value;
        });

    private static string SharingName(Sharing sharing) => sharing switch
    {
        Sharing.Networked => Loc.Get("network.object"),
        Sharing.ServerOnly => Loc.Get("network.never"),
        _ => Loc.Get("network.snapshot"),
    };

    private static string SharingDescription(Sharing sharing) => sharing switch
    {
        Sharing.Networked => Loc.Get("network.object.desc"),
        Sharing.ServerOnly => Loc.Get("network.never.desc"),
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
