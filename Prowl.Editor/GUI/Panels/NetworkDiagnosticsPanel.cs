// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Scribe;
using Prowl.Warp;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

/// <summary>Local process network status and opt-in replication capture, retained after disconnect.</summary>
public class NetworkDiagnosticsPanel : DockPanel
{
    [MenuItem("Window/Debug/Network Analyzer", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(NetworkDiagnosticsPanel));

    public override string Title => "Network Analyzer";
    public override string Icon => EditorIcons.Wifi;

    public NetworkDiagnosticsPanel() => NetworkDiagnostics.Enabled = true;

    private readonly List<NetworkDiagnostics.Entry> _history = new();
    private readonly List<NetworkDiagnostics.Entry> _filtered = new();
    private readonly List<NetworkDiagnostics.ObjectDiagnostic> _objects = new();
    private readonly List<NetPeer> _peers = new();
    private readonly Dictionary<NetPeer, ConnectionStats> _peerStatBaselines = new();
    private string _search = "";
    private int _view, _kind, _direction;
    private bool _paused, _showInternalEvents;
    private bool _detailsCollapsed, _expandCompactDetails;
    private NetworkDiagnostics.Entry? _selected;
    private NetSession? _sampledSession;
    private double _sampleAt;
    private long _sent, _received;
    private double _sendRate, _receiveRate;
    private bool _usingPeerBaseline;

    private static readonly string[] Views = ["Traffic", "Peers", "Objects"];
    private static readonly string[] Kinds = ["All events", "RPC", "SyncVar", "Transform", "Session", "SceneSnapshot", "Spawn", "Despawn", "Owner"];
    private static readonly string[] Directions = ["All directions", "In", "Out", "Rejected"];

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        NetSession? session = Networking.Session;
        _peers.Clear();
        if (session != null) _peers.AddRange(session.Peers);
        _peers.Sort((a, b) => a.Id.CompareTo(b.Id));
        SampleRates(session);

        if (!_paused)
        {
            _history.Clear();
            for (int i = NetworkDiagnostics.Count - 1; i >= 0; i--) _history.Add(NetworkDiagnostics.GetEntry(i));
        }

        using (paper.Column("net_root").Size(width, height).Clip().Enter())
        {
            using (paper.Row("net_views").Height(34).Padding(8, 4).Gap(4).Enter())
            {
                if (width < 480)
                    Origami.Dropdown(paper, "net_view", _view, v => _view = v, Views).Width(92).Show();
                else for (int i = 0; i < Views.Length; i++)
                {
                    int view = i;
                    paper.Box($"net_view_{i}").Width(width < 640 ? 60 : 76).Height(26).Rounded(EditorTheme.Roundness)
                        .BackgroundColor(_view == i ? EditorTheme.Ink200 : EditorTheme.Ink100)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(Views[i], font).FontSize(EditorTheme.FontSizeSmall)
                        .TextColor(_view == i ? EditorTheme.Ink500 : EditorTheme.Ink400)
                        .Alignment(TextAlignment.MiddleCenter).Cursor(PaperCursor.Pointer).OnClick(_ => _view = view);
                }
                string status = session == null ? "Offline" : Networking.IsActive ? session.IsHost ? "Hosting" : "Client" : session.State.ToString();
                string details = session == null ? "Start a network session in Play mode."
                    : $"Role: {(session.IsHost ? "Host" : "Client")}\nState: {session.State}\nTransport: {session.Transport.GetType().Name}\nLocal peer: #{session.LocalPeer?.Id}\n{_peers.Count} peers and {NetworkDiagnostics.NetworkObjectCount} objects\nTick rate: {Networking.TickRate:0.#} Hz\nInterpolation: {Networking.InterpolationDelay.TotalMilliseconds:0} ms\n{NetworkDiagnostics.PendingSyncCount} pending SyncVars\nSent: {Bytes(_sent)}\nReceived: {Bytes(_received)}\nUpload: {Bytes(_sendRate)}/s\nDownload: {Bytes(_receiveRate)}/s";
                paper.Box("net_status").Width(UnitValue.Stretch()).Height(26).PaddingLeft(4)
                    .Text(status, font).FontSize(EditorTheme.FontSizeSmall)
                    .TextColor(Networking.IsActive ? EditorTheme.Green400 : EditorTheme.Ink400)
                    .Alignment(TextAlignment.MiddleLeft).Clip().Tooltip(details);
                if (_view == 0) DrawCaptureControls(paper);
                else if (_view == 1)
                    EditorGUI.ToolbarIconBtn(paper, "net_peer_baseline", EditorIcons.Stopwatch,
                        _usingPeerBaseline ? "Showing traffic since baseline. Click to show session totals."
                            : "Showing session totals. Click to measure traffic from now.",
                        _usingPeerBaseline, TogglePeerBaseline);
            }

            bool inlineFilters = width >= 520;
            using (paper.Row("net_search_row").Height(32).Padding(8, 4).Gap(8).Enter())
            {
                Origami.SearchField(paper, "net_search", _search, v => _search = v, "Search")
                    .Width(UnitValue.Stretch()).Show();
                if (_view == 0 && inlineFilters) DrawFilters(paper, false);
            }
            if (_view == 0 && !inlineFilters)
            {
                using (paper.Row("net_filters").Height(32).Padding(8, 4).Gap(8).Enter())
                    DrawFilters(paper, true);
            }
            EditorGUI.Divider(paper, "net_divider");

            float contentHeight = Math.Max(0, height - 67 - (_view == 0 && !inlineFilters ? 32 : 0));
            if (_view == 1) DrawPeers(paper, font, width, contentHeight);
            else if (_view == 2) DrawObjects(paper, font, width, contentHeight);
            else DrawTraffic(paper, font, width, contentHeight);
        }
    }

    public override void OnClosed()
    {
        _sampledSession = null;
        _peers.Clear();
        _peerStatBaselines.Clear();
        _usingPeerBaseline = false;
    }

    private void SampleRates(NetSession? session)
    {
        if (_sampledSession != session)
        {
            _peerStatBaselines.Clear();
            _usingPeerBaseline = false;
        }
        long sent = 0, received = 0;
        foreach (NetPeer peer in _peers)
        {
            ConnectionStats stats = PeerStats(peer);
            sent += stats.BytesSent;
            received += stats.BytesReceived;
        }
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (_sampledSession != session)
        {
            _sampledSession = session;
            _sampleAt = now;
            _sent = sent;
            _received = received;
            _sendRate = _receiveRate = 0;
        }
        else if (now - _sampleAt >= 0.5)
        {
            _sendRate = Math.Max(0, sent - _sent) / (now - _sampleAt);
            _receiveRate = Math.Max(0, received - _received) / (now - _sampleAt);
            _sent = sent;
            _received = received;
            _sampleAt = now;
        }
    }

    private void TogglePeerBaseline()
    {
        if (Networking.Session is not { } session) return;
        _usingPeerBaseline = !_usingPeerBaseline;
        _peerStatBaselines.Clear();
        _sent = _received = 0;
        foreach (NetPeer peer in session.Peers)
        {
            if (_usingPeerBaseline)
                _peerStatBaselines[peer] = peer.Connection?.Stats ?? default;
            ConnectionStats stats = PeerStats(peer);
            _sent += stats.BytesSent;
            _received += stats.BytesReceived;
        }
        _sampleAt = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        _sendRate = _receiveRate = 0;
    }

    private ConnectionStats PeerStats(NetPeer peer)
    {
        ConnectionStats stats = peer.Connection?.Stats ?? default;
        if (!_peerStatBaselines.TryGetValue(peer, out ConnectionStats baseline)) return stats;
        return new ConnectionStats(
            Math.Max(0, stats.PacketsSent - baseline.PacketsSent),
            Math.Max(0, stats.PacketsReceived - baseline.PacketsReceived),
            Math.Max(0, stats.BytesSent - baseline.BytesSent),
            Math.Max(0, stats.BytesReceived - baseline.BytesReceived));
    }

    private bool Matches(string text) => string.IsNullOrWhiteSpace(_search) || text.Contains(_search, StringComparison.OrdinalIgnoreCase);

    private void DrawCaptureControls(Paper paper)
    {
        bool capturing = NetworkDiagnostics.Enabled;
        using (paper.Box("net_capture").Width(26).Height(26).Rounded(EditorTheme.Roundness)
            .BackgroundColor(capturing ? EditorTheme.Selected : System.Drawing.Color.Transparent)
            .Hovered.BackgroundColor(capturing ? EditorTheme.Selected : EditorTheme.Hover).End()
            .Cursor(PaperCursor.Pointer)
            .Tooltip(capturing ? "Stop capturing events" : "Capture network events")
            .OnClick(_ => NetworkDiagnostics.Enabled = !NetworkDiagnostics.Enabled).Enter())
        {
            paper.Box("net_capture_circle").Width(12).Height(12).Rounded(6).Margin(UnitValue.StretchOne)
                .BorderWidth(1.5f).BorderColor(capturing ? EditorTheme.Red400 : EditorTheme.Ink300)
                .BackgroundColor(capturing ? EditorTheme.Red400 : System.Drawing.Color.Transparent)
                .IsNotInteractable();
        }
        EditorGUI.ToolbarIconBtn(paper, "net_pause", _paused ? EditorIcons.Play : EditorIcons.Pause,
            _paused ? "Resume live view" : "Pause view (capture continues)", _paused, () => _paused = !_paused);
        EditorGUI.ToolbarIconBtn(paper, "net_clear", EditorIcons.Trash, "Clear captured events", false, () =>
        {
            NetworkDiagnostics.Clear();
            _history.Clear();
            _selected = null;
        });
        EditorGUI.ToolbarIconBtn(paper, "net_internal_events", EditorIcons.Microscope,
            "Show internal events (serialization, sync and RPC processing)", _showInternalEvents,
            () => _showInternalEvents = !_showInternalEvents);
    }

    private void DrawFilters(Paper paper, bool stretch)
    {
        Origami.Dropdown(paper, "net_kind", _kind, v => _kind = v, Kinds)
            .Width(stretch ? UnitValue.Stretch() : 124).Show();
        Origami.Dropdown(paper, "net_direction", _direction, v => _direction = v, Directions)
            .Width(stretch ? UnitValue.Stretch() : 124).Show();
    }

    private void DrawTraffic(Paper paper, FontFile font, float width, float height)
    {
        _filtered.Clear();
        foreach (NetworkDiagnostics.Entry entry in _history)
        {
            if (!_showInternalEvents && IsInternalEvent(entry)) continue;
            if (_kind > 0 && !(Kinds[_kind] switch
            {
                "RPC" => entry.Kind.StartsWith("Rpc", StringComparison.OrdinalIgnoreCase),
                "SyncVar" => entry.Kind is "Sync" or "SyncVar",
                _ => entry.Kind == Kinds[_kind],
            })) continue;
            if (_direction > 0 && TrafficDirection(entry) != Directions[_direction]) continue;
            if (!Matches($"{entry.Kind} {entry.Name} {entry.Direction} {entry.Peer} {entry.Detail} {entry.Target} {entry.Context}")) continue;
            _filtered.Add(entry);
        }
        float footerHeight = Math.Min(24, height);
        float availableHeight = Math.Max(0, height - footerHeight);
        bool compactDetails = width < 520 || availableHeight < 240;
        bool expanded = !_detailsCollapsed && (!compactDetails || _expandCompactDetails) && availableHeight >= 160;
        float detailsHeight = _selected.HasValue ? Math.Min(expanded ? 172 : 28, availableHeight * 0.5f) : 0;
        float tableHeight = availableHeight - detailsHeight;
        if (_filtered.Count == 0)
        {
            using (paper.Column("net_empty_wrap").Height(tableHeight).Enter())
                EditorGUI.EmptyState(paper, "net_empty", _history.Count > 0 ? "No events match your filters."
                    : !NetworkDiagnostics.Enabled ? "Enable Capture to record network events."
                    : _paused ? "View paused. Resume to see new events." : "Waiting for network events…", font);
        }
        else
            Origami.Table(paper, "net_events", -1, i => _selected = _filtered[i])
                .Bordered(false).Scroll(width, tableHeight).RowHeight(EditorTheme.RowHeight)
                .Column("Time (UTC)", 1).Column("Event", 1.4f).Column("Name", 2)
                .Column("Peer", 1.2f).Column("Size", 0.7f)
                .IsSelected(i => _selected?.Sequence == _filtered[i].Sequence)
                .RowCount(_filtered.Count)
                .CellContent((i, col) =>
                {
                    NetworkDiagnostics.Entry e = _filtered[i];
                    string eventName = e.Kind == "Sync" ? "SyncVar" : e.Kind;
                    bool warning = e.Direction is "Rejected" or "Failed";
                    if (col == 1)
                    {
                        IOrigamiIcon? icon = e.Direction switch
                        {
                            "Out" => EditorIcons.ArrowUp_I,
                            "In" => EditorIcons.ArrowDown_I,
                            "Rejected" or "Failed" => EditorIcons.TriangleExclamation_I,
                            "Serialize" => EditorIcons.FileExport_I,
                            "Apply" => EditorIcons.Sliders_I,
                            "Send" => EditorIcons.PaperPlane_I,
                            "Receive" => EditorIcons.Inbox_I,
                            "Invoke" => EditorIcons.Bolt_I,
                            "Local" => EditorIcons.Plug_I,
                            _ => EditorIcons.CircleInfo_I,
                        };
                        using (paper.Row($"net_event_{i}").Height(EditorTheme.RowHeight).Gap(4).Clip().Enter())
                        {
                            ElementBuilder marker = paper.Box($"net_event_icon_{i}").Width(20).Height(EditorTheme.RowHeight);
                            if (icon != null)
                                marker.Tooltip(e.Direction switch
                                {
                                    "Out" => "Outgoing",
                                    "In" => "Incoming",
                                    "Rejected" => "Rejected",
                                    "Failed" => "Failed to process incoming message",
                                    "Serialize" => "Serialize a SyncVar value",
                                    "Apply" => "Apply a received SyncVar value",
                                    "Send" => "Send an RPC",
                                    "Receive" => "Receive an RPC",
                                    "Invoke" => "Invoke an RPC method",
                                    "Local" => "Local session event",
                                    _ => e.Direction,
                                }).Icon(paper, icon, warning ? EditorTheme.Amber400
                                    : e.Direction == "Out" ? EditorTheme.Blue400
                                    : e.Direction == "In" ? EditorTheme.Green400 : EditorTheme.Ink400, size: 16f);
                            using (paper.Box($"net_event_name_wrap_{i}").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
                                Label(paper, font, $"net_event_name_{i}", eventName, EditorTheme.RowHeight, warning);
                        }
                        return;
                    }
                    string text = col switch { 0 => e.Time.ToString("HH:mm:ss.fff"),
                        2 => e.Name ?? (e.Kind == "Session" ? e.Detail : ""),
                        3 => e.Peer, _ => Bytes(e.PayloadBytes) };
                    Label(paper, font, $"net_e_{i}_{col}", text, EditorTheme.RowHeight, warning);
                }).Show();

        if (_selected is { } selected)
        {
            using (paper.Column("net_details").Height(detailsHeight).Padding(8, 0).Clip()
                .BackgroundColor(EditorTheme.Ink100).Enter())
            {
                using (paper.Row("net_detail_heading").Height(28).Gap(4).Enter())
                {
                    paper.Box("net_detail_toggle").Width(UnitValue.Stretch()).Height(28).Clip()
                        .Text($"{(expanded ? EditorIcons.ChevronDown : EditorIcons.ChevronRight)}  {selected.Kind} details", font)
                        .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink500)
                        .Alignment(TextAlignment.MiddleLeft).Cursor(PaperCursor.Pointer)
                        .OnClick(_ =>
                        {
                            _detailsCollapsed = expanded;
                            _expandCompactDetails = !expanded;
                        });
                    EditorGUI.ToolbarIconBtn(paper, "net_detail_close", EditorIcons.Xmark, "Close event details", false, () => _selected = null);
                }
                if (expanded)
                {
                    Origami.ScrollView(paper, "net_detail_scroll", Math.Max(0, width - 16), Math.Max(0, detailsHeight - 28)).Body(() =>
                    {
                        string text = selected.Detail
                            + (string.IsNullOrEmpty(selected.Context) ? "" : $"\n\n{selected.Context}")
                            + $"\n\nAction: {selected.Direction}\nPeer: {selected.Peer}\nSize: {Bytes(selected.PayloadBytes)}";
                        if (selected.Target != Guid.Empty) text += $"\nTarget: {selected.Target}";
                        paper.Box("net_detail_context").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                            .Text(text, font).FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink400)
                            .Wrap(Prowl.Scribe.TextWrapMode.Wrap).Alignment(TextAlignment.MiddleLeft);
                    });
                }
            }
        }

        using (paper.Row("net_capture_status_row").Height(footerHeight).Padding(8, 0).Gap(8).Clip().Enter())
        {
            using (paper.Box("net_capture_status_wrap").Width(UnitValue.Stretch()).Height(footerHeight).Enter())
                Label(paper, font, "net_capture_status",
                    $"{(_paused ? "Paused with" : NetworkDiagnostics.Enabled ? "Capturing" : "Capture off with")} {_filtered.Count} events{(_showInternalEvents ? " (including internal events)" : "")}",
                    footerHeight, tooltip: $"Showing {_filtered.Count} of {_history.Count} retained events\n{NetworkDiagnostics.Overwritten} overwritten\nCapacity: {NetworkDiagnostics.Capacity}");
            if (width >= 420)
                paper.Box("net_rates").Width(Math.Min(240, width * 0.5f)).Height(footerHeight).Clip()
                    .Text($"↑ {Bytes(_sendRate)}/s  ↓ {Bytes(_receiveRate)}/s", font)
                    .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink400)
                    .Alignment(TextAlignment.MiddleRight);
        }
    }

    // Member traces describe processing, while In/Out entries record actual transport messages.
    // Preserve session lifecycle and failures in the default view.
    private static bool IsInternalEvent(NetworkDiagnostics.Entry entry) =>
        entry.Direction is "Serialize" or "Apply" or "Send" or "Receive" or "Invoke";

    private static string TrafficDirection(NetworkDiagnostics.Entry entry) => entry.Direction switch
    {
        "In" or "Out" or "Rejected" => entry.Direction,
        "Failed" => "In", // Failed entries are recorded by the incoming message handler.
        _ => "—",
    };

    private void DrawPeers(Paper paper, FontFile font, float width, float height)
    {
        _peers.RemoveAll(p => !Matches($"{p.Name} {p.Id} {p.Connection?.Address}"));
        if (_peers.Count == 0)
        {
            using (paper.Column("net_peers_empty_wrap").Height(height).Enter())
                EditorGUI.EmptyState(paper, "net_peers_empty", Networking.Session == null ? "No active session." : "No peers match your filter.", font);
            return;
        }
        Origami.Table(paper, "net_peers", -1, _ => { })
            .Bordered(false).Scroll(width, height).RowHeight(EditorTheme.RowHeight)
            .Column("Peer", 1.4f).Column("Connection", 1.5f).Column("RTT", 0.7f)
            .Column("Sent", 1).Column("Received", 1).Column("Packets ↑ / ↓", 1)
            .RowCount(_peers.Count).CellContent((i, col) =>
            {
                NetPeer peer = _peers[i];
                ConnectionStats stats = PeerStats(peer);
                string text = col switch
                {
                    0 => peer.Name,
                    1 => peer.IsLocal ? "Local" : peer.Connection == null ? "Via host" : $"{peer.Connection.Address} ({peer.Connection.State})",
                    2 => peer.Connection == null ? "—" : $"{peer.RoundTripTime.TotalMilliseconds:0.0} ms",
                    3 => peer.Connection == null ? "—" : Bytes(stats.BytesSent),
                    4 => peer.Connection == null ? "—" : Bytes(stats.BytesReceived),
                    _ => peer.Connection == null ? "—" : $"{stats.PacketsSent} / {stats.PacketsReceived}"
                };
                Label(paper, font, $"net_p_{i}_{col}", text, EditorTheme.RowHeight,
                    tooltip: col == 0 ? $"{peer.Name}\nPeer ID: {peer.Id}" : null);
            }).Show();
    }

    private void DrawObjects(Paper paper, FontFile font, float width, float height)
    {
        NetworkDiagnostics.GetObjectDiagnostics(_objects);
        _objects.RemoveAll(o => !Matches($"{o.Name} {o.Identifier} {o.OwnerId} {OwnerName(o.OwnerId)}"));
        _objects.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        if (_objects.Count == 0)
        {
            using (paper.Column("net_objects_empty_wrap").Height(height).Enter())
                EditorGUI.EmptyState(paper, "net_objects_empty", Networking.Session == null ? "No active session."
                    : string.IsNullOrWhiteSpace(_search) ? "No networked objects." : "No objects match your filter.", font);
            return;
        }
        Origami.Table(paper, "net_objects", -1, _ => { })
            .Bordered(false).Scroll(width, height).RowHeight(EditorTheme.RowHeight)
            .Column("Object", 2).Column("Owner", 1).Column("Control", 1).Column("Transform sync", 1)
            .RowCount(_objects.Count).CellContent((i, col) =>
            {
                NetworkDiagnostics.ObjectDiagnostic o = _objects[i];
                string text = col switch { 0 => o.Name, 1 => OwnerName(o.OwnerId),
                    2 => o.Controlled ? "Local" : "Remote", _ => o.SyncTransform ? "On" : "Off" };
                string? tooltip = col == 0 ? $"{o.Name}\n{o.Identifier}\nBuffered samples: {o.BufferedSamples}\nLast received tick: {o.LastReceivedTick}"
                    : col == 1 ? $"{OwnerName(o.OwnerId)}" + (o.OwnerId < 0 ? "" : $"\nPeer ID: {o.OwnerId}") : null;
                Label(paper, font, $"net_o_{i}_{col}", text, EditorTheme.RowHeight, tooltip: tooltip);
            }).Show();
    }

    private string OwnerName(int id) => id < 0 ? "Host" : _peers.Find(p => p.Id == id)?.Name ?? $"#{id}";

    private static void Label(Paper paper, FontFile font, string id, string text, float height, bool warning = false, string? tooltip = null)
    {
        ElementBuilder label = paper.Box(id).Height(height).Clip()
            .Text(text, font).FontSize(EditorTheme.FontSizeSmall)
            .TextColor(warning ? EditorTheme.Amber400 : EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft);
        if (tooltip != null) label.Tooltip(tooltip);
    }

    private static string Bytes(double value) => value >= 1048576 ? $"{value / 1048576:0.0} MiB"
        : value >= 1024 ? $"{value / 1024:0.0} KiB" : $"{value:0} B";
}
