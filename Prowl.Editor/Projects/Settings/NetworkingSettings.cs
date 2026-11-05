using System;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Networking;

namespace Prowl.Editor.Projects.Settings;

[ProjectSettings("Networking", EditorIcons.Wifi, order: 24)]
public sealed class NetworkingSettings : ProjectSettingsBase
{
    public NetworkConfiguration Configuration = new();

    public override void ResetToDefaults() => Configuration = new();
    public override void Apply()
    {
        Configuration = (Configuration ?? new()).ValidatedCopy();
        Networking.Configure(Configuration);
    }

    private void Changed()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Label(paper, "netsettings_info", "Transport and session changes apply when the next session starts. Replication timing applies immediately.").Show();
        Header(paper, "transport", "Transport");
        EditorGUI.SettingsRow(paper, "netsettings_transport", "Transport", () =>
            Origami.Dropdown(paper, "netsettings_transport_v", (int)Configuration.Transport,
                v => { Configuration.Transport = (NetworkTransport)v; Changed(); }, ["KCP", "Local"]).Show());
        if (Configuration.Transport == NetworkTransport.Local)
        {
            EditorGUI.SettingsTextField(paper, "netsettings_local", "Local Host Address", Configuration.LocalAddress,
                v => { Configuration.LocalAddress = v; Changed(); });
            Origami.Label(paper, "netsettings_local_note", "Local runs within one process. Separate editor client instances require KCP.").Show();
        }
        else if (Configuration.Transport == NetworkTransport.Kcp)
        {
            Toggle(paper, "batchmessages", "Batch Messages", Configuration.BatchMessages, v => Configuration.BatchMessages = v);
            EditorGUI.SettingsRow(paper, "netsettings_execution", "Execution", () =>
                Origami.Dropdown(paper, "netsettings_execution_v",
                    (int)Configuration.Execution,
                    v => { Configuration.Execution = (NetworkExecution)v; Changed(); },
                    ["Automatic", "Inline", "Dedicated Thread"]).Show());
            Integer(paper, "port", "Port (0 = automatic)", Configuration.Port, v => Configuration.Port = v, 0, 65535);
            Unsigned(paper, "protocol", "Protocol ID", Configuration.ProtocolId, v => Configuration.ProtocolId = v);
            Integer(paper, "mtu", "MTU (bytes)", Configuration.Mtu, v => Configuration.Mtu = v, 1200, 65507);
            Number(paper, "connect", "Connect Timeout (s)", Configuration.ConnectTimeoutSeconds, v => Configuration.ConnectTimeoutSeconds = v, 0.1f, 300);
            Number(paper, "timeout", "Connection Timeout (s)", Configuration.TimeoutSeconds, v => Configuration.TimeoutSeconds = v, 0.1f, 300);
            Number(paper, "ping", "Ping Interval (s)", Configuration.PingIntervalSeconds, v => Configuration.PingIntervalSeconds = v, 0.05f, 60);
        }

        Header(paper, "session", "Session");
        Unsigned(paper, "version", "Application Version", Configuration.AppVersion, v => Configuration.AppVersion = v);
        Integer(paper, "peers", "Maximum Clients", Configuration.MaxPeers, v => Configuration.MaxPeers = v, 1, 4096);
        Integer(paper, "message", "Maximum Message Size (bytes)", Configuration.MaxMessageSize, v => Configuration.MaxMessageSize = v, 1024, 1073741824);
        Number(paper, "join", "Join Timeout (s)", Configuration.JoinTimeoutSeconds, v => Configuration.JoinTimeoutSeconds = v, 0.1f, 300);

        Header(paper, "replication", "Replication");
        Number(paper, "tick", "Tick Rate (Hz)", Configuration.TickRate, v => Configuration.TickRate = v, 1, 240);
        Number(paper, "interpolation", "Interpolation Delay (ms)", Configuration.InterpolationMilliseconds, v => Configuration.InterpolationMilliseconds = v, 0, 5000);
        Number(paper, "positionthreshold", "Position Settle Threshold (units)", Configuration.TransformPositionThreshold, v => Configuration.TransformPositionThreshold = v, 0, float.MaxValue);
        Number(paper, "rotationthreshold", "Rotation Settle Threshold (degrees)", Configuration.TransformRotationThresholdDegrees, v => Configuration.TransformRotationThresholdDegrees = v, 0, 180);
        Number(paper, "scalethreshold", "Scale Settle Threshold", Configuration.TransformScaleThreshold, v => Configuration.TransformScaleThreshold = v, 0, float.MaxValue);

        Header(paper, "snapshots", "Snapshot Limits");
        Integer(paper, "snapshotbytes", "Maximum Decompressed Bytes", Configuration.MaxSnapshotDecompressedBytes, v => Configuration.MaxSnapshotDecompressedBytes = v, 1, 1073741824);
        Integer(paper, "snapshotdepth", "Maximum Nesting Depth", Configuration.MaxSnapshotDepth, v => Configuration.MaxSnapshotDepth = v, 1, 256);
        Integer(paper, "snapshotnodes", "Maximum Values", Configuration.MaxSnapshotNodes, v => Configuration.MaxSnapshotNodes = v, 1, 1000000);
        Origami.Label(paper, "netsettings_snapshot_note", "Limits apply to incoming scenes and spawned objects before they are created.").Show();

        if (Configuration.Transport != NetworkTransport.Kcp) return;
        Header(paper, "reliability", "KCP Reliability");
        Origami.Label(paper, "netsettings_agreement", "Peers must use matching batching settings, protocol IDs, MTUs, and send/receive windows.").Show();
        Toggle(paper, "nodelay", "No Delay", Configuration.NoDelay, v => Configuration.NoDelay = v);
        Toggle(paper, "congestion", "Congestion Control", Configuration.CongestionControl, v => Configuration.CongestionControl = v);
        Integer(paper, "interval", "Update Interval (ms)", Configuration.UpdateIntervalMilliseconds, v => Configuration.UpdateIntervalMilliseconds = v, 10, 5000);
        Integer(paper, "resend", "Fast Resend (0 = off)", Configuration.FastResend, v => Configuration.FastResend = v, 0, 255);
        Integer(paper, "sendwindow", "Send Window (segments)", Configuration.SendWindow, v => Configuration.SendWindow = v, 1, 65535);
        Integer(paper, "receivewindow", "Receive Window (segments)", Configuration.ReceiveWindow, v => Configuration.ReceiveWindow = v, 128, 65535);
        Integer(paper, "retransmits", "Maximum Retransmits", Configuration.MaxRetransmits, v => Configuration.MaxRetransmits = v, 1, 1000);

        Header(paper, "simulation", "Network Simulation");
        Toggle(paper, "simulate", "Simulate Lag", Configuration.SimulateNetwork, v => Configuration.SimulateNetwork = v);
        Number(paper, "loss", "Packet Loss (%)", Configuration.PacketLossPercent, v => Configuration.PacketLossPercent = v, 0, 100);
        Number(paper, "latency", "Added Latency (ms)", Configuration.LatencyMilliseconds, v => Configuration.LatencyMilliseconds = v, 0, 10000);
        Number(paper, "jitter", "Jitter (ms)", Configuration.JitterMilliseconds, v => Configuration.JitterMilliseconds = v, 0, 10000);
        Origami.Label(paper, "netsettings_sim_note", "Simulation affects outgoing packets and is also exported to builds when enabled.").Show();
    }

    private static void Header(Paper paper, string id, string title) => Origami.Header(paper, "netsettings_" + id, title).Underline().Show();
    private void Integer(Paper paper, string id, string label, int value, Action<int> setter, int min, int max) =>
        EditorGUI.Row(paper, "netsettings_" + id, label, () => Origami.NumericField<int>(paper, "netsettings_" + id + "_v", value,
            v => { setter(Math.Clamp(v, min, max)); Changed(); }).Min(min).Max(max).Show());
    private void Unsigned(Paper paper, string id, string label, uint value, Action<uint> setter) =>
        EditorGUI.Row(paper, "netsettings_" + id, label, () => Origami.NumericField<uint>(paper, "netsettings_" + id + "_v", value,
            v => { setter(v); Changed(); }).Show());
    private void Number(Paper paper, string id, string label, float value, Action<float> setter, float min, float max) =>
        EditorGUI.Row(paper, "netsettings_" + id, label, () => Origami.NumericField<float>(paper, "netsettings_" + id + "_v", value,
            v => { setter(v); Changed(); }).Min(min).Max(max).Show());
    private void Toggle(Paper paper, string id, string label, bool value, Action<bool> setter) =>
        EditorGUI.SettingsToggle(paper, "netsettings_" + id, label, value, v => { setter(v); Changed(); });
}
