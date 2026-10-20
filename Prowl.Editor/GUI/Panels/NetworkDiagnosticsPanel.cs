// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Editor.Core;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Warp;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

/// <summary>Local process network status and opt-in replication capture, retained after disconnect.</summary>
public class NetworkDiagnosticsPanel : DockPanel
{
    [MenuItem("Window/Debug/Network", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(NetworkDiagnosticsPanel));

    public override string Title => "Network";
    public override string Icon => EditorIcons.Wifi;

    private readonly List<NetworkDiagnostics.Entry> _history = new();
    private readonly List<NetworkDiagnostics.Entry> _filtered = new();
    private readonly List<Networking.ObjectDiagnostic> _objects = new();
    private readonly List<NetPeer> _peers = new();
    private string _search = "";
    private int _view, _kind, _direction;
    private bool _paused;
    private NetworkDiagnostics.Entry? _selected;
    private NetSession? _sampledSession;
    private double _sampleAt;
    private long _sent, _received;
    private double _sendRate, _receiveRate;

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
            using (paper.Row("net_controls").Height(34).Padding(8, 5).Gap(8).Enter())
            {
                Origami.Checkbox(paper, "net_capture", NetworkDiagnostics.Enabled, v => NetworkDiagnostics.Enabled = v)
                    .Label("Capture").Show();
                EditorGUI.ToolbarIconBtn(paper, "net_pause", _paused ? EditorIcons.Play : EditorIcons.Pause,
                    "Freeze/resume the traffic view; capture continues", _paused, () => _paused = !_paused);
                EditorGUI.ToolbarIconBtn(paper, "net_clear", EditorIcons.Trash, "Clear captured events", false, () =>
                {
                    NetworkDiagnostics.Clear();
                    _history.Clear();
                    _selected = null;
                });
                EditorGUI.ToolbarIconBtn(paper, "net_settings", EditorIcons.Gear, "Open Networking project settings", false,
                    ProjectSettingsPanel.OpenCategory<NetworkingSettings>);
                Origami.Dropdown(paper, "net_view", _view, v => _view = v, Views).Width(100).Show();
                Origami.SearchField(paper, "net_search", _search, v => _search = v, "Filter name, peer, member or ID")
                    .Width(UnitValue.Stretch()).Show();
            }

            string status = session == null ? "Offline — start a network session in Play mode"
                : $"{(session.IsHost ? "Host" : "Client")} / {session.State} / {session.Transport.GetType().Name} / Local #{session.LocalPeer?.Id}";
            Label(paper, font, "net_status", status, 24);
            Label(paper, font, "net_summary",
                $"{_peers.Count} peers   ·   {Networking.NetworkObjectCount} objects   ·   {Networking.PendingSyncCount} dirty SyncVars   ·   {Networking.TickRate:0.#} Hz   ·   Interpolation {Networking.InterpolationDelay.TotalMilliseconds:0} ms", 24);
            Label(paper, font, "net_rates", $"Connected links: ↑ {Bytes(_sendRate)}/s   ↓ {Bytes(_receiveRate)}/s   ·   Totals ↑ {Bytes(_sent)}   ↓ {Bytes(_received)}", 24);
            EditorGUI.Divider(paper, "net_divider");

            float contentHeight = Math.Max(0, height - 107);
            if (_view == 1) DrawPeers(paper, font, width, contentHeight);
            else if (_view == 2) DrawObjects(paper, font, width, contentHeight);
            else DrawTraffic(paper, font, width, contentHeight);
        }
    }

    public override void OnClosed()
    {
        _sampledSession = null;
        _peers.Clear();
    }

    private void SampleRates(NetSession? session)
    {
        long sent = 0, received = 0;
        foreach (NetPeer peer in _peers)
        {
            ConnectionStats stats = peer.Connection?.Stats ?? default;
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

    private bool Matches(string text) => string.IsNullOrWhiteSpace(_search) || text.Contains(_search, StringComparison.OrdinalIgnoreCase);

    private void DrawTraffic(Paper paper, FontFile font, float width, float height)
    {
        using (paper.Row("net_filters").Height(32).Padding(8, 4).Gap(8).Enter())
        {
            Origami.Dropdown(paper, "net_kind", _kind, v => _kind = v, Kinds).Width(130).Show();
            Origami.Dropdown(paper, "net_dir", _direction, v => _direction = v, Directions).Width(130).Show();
        }
        _filtered.Clear();
        foreach (NetworkDiagnostics.Entry entry in _history)
        {
            if (_kind > 0 && !(Kinds[_kind] == "RPC" ? entry.Kind.StartsWith("Rpc", StringComparison.OrdinalIgnoreCase) : entry.Kind == Kinds[_kind])) continue;
            if (_direction > 0 && entry.Direction != Directions[_direction]) continue;
            if (!Matches($"{entry.Kind} {entry.Direction} {entry.Peer} {entry.Detail} {entry.Target}")) continue;
            _filtered.Add(entry);
        }
        Label(paper, font, "net_capture_status",
            $"{(_paused ? "View paused" : NetworkDiagnostics.Enabled ? "Capturing" : "Capture disabled")}   ·   {_filtered.Count} shown / {_history.Count} retained   ·   {NetworkDiagnostics.Overwritten} overwritten (limit {NetworkDiagnostics.Capacity})", 24);
        float detailsHeight = _selected.HasValue ? 100 : 0;
        float tableHeight = Math.Max(0, height - 56 - detailsHeight);
        if (_filtered.Count == 0)
        {
            using (paper.Column("net_empty_wrap").Height(tableHeight).Enter())
                EditorGUI.EmptyState(paper, "net_empty", "Enable Capture to record local engine traffic, RPCs and SyncVars. Adjust filters if events are hidden.", font);
        }
        else
            Origami.Table(paper, "net_events", -1, i => _selected = _filtered[i])
                .Bordered(false).Scroll(width, tableHeight).RowHeight(EditorTheme.RowHeight)
                .Column("Time (UTC)", 1).Column("Event", 1).Column("Action", 0.8f).Column("Peer", 1.2f)
                .Column("Detail", 3).Column("Payload B", 0.7f)
                .IsSelected(i => _selected?.Sequence == _filtered[i].Sequence)
                .RowCount(_filtered.Count)
                .CellContent((i, col) =>
                {
                    NetworkDiagnostics.Entry e = _filtered[i];
                    string text = col switch { 0 => e.Time.ToString("HH:mm:ss.fff"), 1 => e.Kind, 2 => e.Direction,
                        3 => e.Peer, 4 => e.Detail, _ => e.PayloadBytes.ToString() };
                    Label(paper, font, $"net_e_{i}_{col}", text, EditorTheme.RowHeight, e.Direction == "Rejected");
                }).Show();

        if (_selected is { } selected)
            using (paper.Column("net_details").Height(detailsHeight).Padding(8).Clip().Enter())
            {
                Label(paper, font, "net_detail_title", $"#{selected.Sequence}  {selected.Detail}", 24);
                Label(paper, font, "net_detail_target", $"Target: {selected.Target}   ·   {selected.PayloadBytes} payload bytes", 24);
                Label(paper, font, "net_detail_hex", $"Hex (first 64 bytes): {selected.Preview}", 24);
            }
    }

    private void DrawPeers(Paper paper, FontFile font, float width, float height)
    {
        _peers.RemoveAll(p => !Matches($"{p.Name} {p.Id} {p.Connection?.Address}"));
        Origami.Table(paper, "net_peers", -1, _ => { })
            .Bordered(false).Scroll(width, height).RowHeight(EditorTheme.RowHeight)
            .Column("Peer", 1.4f).Column("Connection", 1.5f).Column("RTT", 0.7f)
            .Column("Sent", 1).Column("Received", 1).Column("Packets ↑ / ↓", 1)
            .RowCount(_peers.Count).CellContent((i, col) =>
            {
                NetPeer peer = _peers[i];
                ConnectionStats stats = peer.Connection?.Stats ?? default;
                string text = col switch
                {
                    0 => peer.ToString(),
                    1 => peer.IsLocal ? "Local" : peer.Connection == null ? "Via host" : $"{peer.Connection.Address} / {peer.Connection.State}",
                    2 => peer.Connection == null ? "—" : $"{peer.RoundTripTime.TotalMilliseconds:0.0} ms",
                    3 => Bytes(stats.BytesSent), 4 => Bytes(stats.BytesReceived),
                    _ => $"{stats.PacketsSent} / {stats.PacketsReceived}"
                };
                Label(paper, font, $"net_p_{i}_{col}", text, EditorTheme.RowHeight);
            }).Show();
    }

    private void DrawObjects(Paper paper, FontFile font, float width, float height)
    {
        Networking.GetObjectDiagnostics(_objects);
        _objects.RemoveAll(o => !Matches($"{o.Name} {o.Identifier} {o.OwnerId}"));
        _objects.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Origami.Table(paper, "net_objects", -1, _ => { })
            .Bordered(false).Scroll(width, height).RowHeight(EditorTheme.RowHeight)
            .Column("Object", 1.5f).Column("Identifier", 2).Column("Owner", 0.7f)
            .Column("Authority", 0.9f).Column("Transform", 0.8f).Column("Samples / Tick", 1)
            .RowCount(_objects.Count).CellContent((i, col) =>
            {
                Networking.ObjectDiagnostic o = _objects[i];
                string text = col switch { 0 => o.Name, 1 => o.Identifier.ToString(),
                    2 => o.OwnerId < 0 ? "Host" : $"#{o.OwnerId}", 3 => o.Controlled ? "Controlled" : "Proxy",
                    4 => o.SyncTransform ? "Enabled" : "Disabled", _ => $"{o.BufferedSamples} / {o.LastReceivedTick}" };
                Label(paper, font, $"net_o_{i}_{col}", text, EditorTheme.RowHeight);
            }).Show();
    }

    private static void Label(Paper paper, FontFile font, string id, string text, float height, bool warning = false) =>
        paper.Box(id).Height(height).Clip().Tooltip(text).IsNotInteractable()
            .Text(text, font).FontSize(EditorTheme.FontSizeSmall)
            .TextColor(warning ? EditorTheme.Amber400 : EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft);

    private static string Bytes(double value) => value >= 1048576 ? $"{value / 1048576:0.0} MiB"
        : value >= 1024 ? $"{value / 1024:0.0} KiB" : $"{value:0} B";
}
