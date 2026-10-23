// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Editor.Core.Diagnostics;
using Prowl.Editor.Projects;
using Prowl.Graphite;
using Prowl.Graphite.Debugger;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Editor;

/// <summary> Takes, saves, loads, dumps and replays Graphite frame snapshots, the same ones the render profiler panels show. </summary>
public static class CliSnapshotCommands
{
    public enum SnapshotAction { Capture, Info, Save, Load }

    public enum DumpKind { Groups, Pass, Resources, Programs }

    private const int DefaultCommandCount = 200;
    private const int DefaultResolution = 2048;

    private static RenderProfilerSource Profiler
        => EditorApplication.Instance?.RenderProfiler ?? throw new CliException("The render profiler is not available in this editor.");

    private static DeepRecording Current
        => Profiler.Snapshot ?? throw new CliException("There is no snapshot. Run 'snapshot --action capture' or 'snapshot --action load --path <file>'.");

    [CliCommand("snapshot", "Captures a frame snapshot (a deep Graphite recording of the next frame), saves or loads one from a file, or summarizes the current one")]
    public static async Task<object> Snapshot(
        [CliArg("action")] SnapshotAction action = SnapshotAction.Info,
        [CliArg("path", "Snapshot file for save and load, relative to the project or absolute. Save defaults to Library/Cli/Snapshots")] string path = "",
        [CliArg("mode", "Full copies every pass output so passes restore without re-executing. ReplayOnly is smaller and re-executes")] DeepMode mode = DeepMode.Full)
    {
        RenderProfilerSource profiler = Profiler;
        switch (action)
        {
            case SnapshotAction.Capture:
            {
                DeepRecording? before = profiler.Snapshot;
                profiler.Capture(0, mode);
                bool taken = await CliServer.WaitUntil(() => profiler.Snapshot != null && !ReferenceEquals(profiler.Snapshot, before), TimeSpan.FromSeconds(30));
                if (!taken)
                    throw new CliException("The snapshot did not finish in time. The editor window must be drawing frames.");
                break;
            }

            case SnapshotAction.Save:
            {
                _ = Current;
                string target = path.Length > 0 ? Resolve(path) : Path.Combine(Project.Current!.RootPath, "Library", "Cli", "Snapshots", $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.gsn");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                profiler.SaveSnapshot(target);
                return new { saved = target, bytes = new FileInfo(target).Length };
            }

            case SnapshotAction.Load:
            {
                if (path.Length == 0)
                    throw new CliException("load needs --path.");
                string source = Resolve(path);
                if (!File.Exists(source))
                    throw new CliException($"File not found: {source}");
                try { profiler.LoadSnapshot(source); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
                {
                    throw new CliException($"Could not load {source}: {ex.Message}");
                }
                break;
            }
        }

        return Summarize(Current);
    }

    [CliCommand("snapshot_dump", "Dumps the current snapshot: groups with per pass stats and GPU time, one pass with its commands, resources, or programs")]
    public static object SnapshotDump(
        [CliArg("kind")] DumpKind kind = DumpKind.Groups,
        [CliArg("group", "Group index, from 'groups'. -1 holds passes added outside any group")] int? group = null,
        [CliArg("pass", "Pass index within the group, from 'groups'")] int? pass = null,
        [CliArg("from", "First command to list for 'pass'")] int from = 0,
        [CliArg("count", "How many commands to list for 'pass'")] int count = DefaultCommandCount)
    {
        DeepRecording recording = Current;
        Names names = new(recording);
        switch (kind)
        {
            case DumpKind.Groups: return Groups(recording);
            case DumpKind.Resources: return recording.Resources.Select(r => Describe(r, names)).ToList();
            case DumpKind.Programs: return recording.Programs.Select(p => Describe(p)).ToList();
            default:
            {
                if (group == null || pass == null)
                    throw new CliException("'pass' needs --group and --pass.");
                return DescribePass(recording, names, FindPass(recording, group.Value, pass.Value), from, count);
            }
        }
    }

    [CliCommand("snapshot_replay", "Replays a pass of the current snapshot on the editor's GPU and writes each output texture as a PNG. --event stops after that draw, dispatch, clear or copy")]
    public static object SnapshotReplay(
        [CliArg("group", "Group index, from 'snapshot_dump'")] int group,
        [CliArg("pass", "Pass index within the group")] int pass,
        [CliArg("event", "Last event of the pass to run, counted from 0. Omit to run the whole pass")] int? @event = null,
        [CliArg("resource", "Only write outputs of the resource with this name")] string resource = "",
        [CliArg("exposure", "Scale applied to color before clamping, useful for HDR targets")] float exposure = 1f,
        [CliArg("resolution", "Longest side of each image in pixels. Larger textures are shrunk by skipping texels")] int resolution = DefaultResolution,
        [CliArg("full", "Write images at the texture's full size, ignoring --resolution")] bool full = false,
        [CliArg("out", "Folder for the images, relative to the project or absolute. Defaults to Library/Cli/Replays")] string @out = "")
    {
        if (resolution <= 0 && !full)
            throw new CliException("--resolution must be above 0. Pass --full for the whole texture.");
        DeepRecording recording = Current;
        DeepPass target = FindPass(recording, group, pass);
        Names names = new(recording);

        ReplayResult result = Profiler.Replay(new ReplayRequest { GroupIndex = group, PassIndex = pass, EventIndex = @event });
        if (result.Status == ReplayStatus.NotReplayable)
            throw new CliException($"Pass {target.Name} is not replayable: {result.Reason}");

        string folder = @out.Length > 0
            ? Resolve(@out)
            : Path.Combine(Project.Current!.RootPath, "Library", "Cli", "Replays", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}");
        Directory.CreateDirectory(folder);

        Dictionary<BlobRef, EquatableArray<byte>> blobs = recording.Blobs.ToDictionary(b => b.Ref, b => b.Data);
        List<object> outputs = [];
        foreach (ReplayOutput output in result.Outputs)
        {
            string name = names.Of(output.Resource);
            if (resource.Length > 0 && name != resource)
                continue;

            RecordedCopy? recorded = target.Copies.FirstOrDefault(c => c.Placement == CopyPlacement.AfterPass && c.Version.Resource == output.Resource);
            bool? matches = recorded != null && blobs.TryGetValue(recorded.Blob, out EquatableArray<byte> data) ? data.Equals(output.Data) : null;
            outputs.Add(new
            {
                resource = name,
                matchesRecorded = matches,
                files = WriteOutput(folder, group, pass, name, output, names.Resource(output.Resource), exposure, full ? 0 : resolution),
            });
        }

        if (outputs.Count == 0 && resource.Length > 0)
            throw new CliException($"Pass {target.Name} has no output named '{resource}'. Outputs: {string.Join(", ", result.Outputs.Select(o => names.Of(o.Resource)))}");

        return new { status = result.Status.ToString(), result.Reason, pass = target.Name, folder, outputs };
    }

    private static List<object> WriteOutput(string folder, int group, int pass, string name, ReplayOutput output, RecordedResource? resource, float exposure, int resolution)
    {
        List<object> files = [];
        string stem = $"g{group}-p{pass}-{Sanitize(name)}";
        bool texture = resource?.Texture != null;
        ReadOnlySpan<byte> data = output.Data.AsSpan();

        if (!texture || output.Regions.Length == 0)
        {
            string raw = Path.Combine(folder, stem + ".bin");
            File.WriteAllBytes(raw, data.ToArray());
            files.Add(new { path = raw, bytes = data.Length });
            return files;
        }

        foreach (CopyRegion region in output.Regions)
        {
            string suffix = output.Regions.Length > 1 ? $"-m{region.MipLevel}-l{region.ArrayLayer}" : "";
            if (!SnapshotImages.CanDecode(region.Format))
            {
                string raw = Path.Combine(folder, stem + suffix + ".bin");
                File.WriteAllBytes(raw, data.ToArray());
                files.Add(new { path = raw, format = region.Format.ToString(), note = "Format cannot be turned into a PNG, raw bytes written" });
                continue;
            }

            int step = SnapshotImages.Step(region, resolution);
            (int width, int height) = SnapshotImages.Size(region, step);
            byte[] pixels = SnapshotImages.ToRgba8(data, region, exposure, step);
            string png = Path.Combine(folder, stem + suffix + ".png");
            using (var image = Aperture.Image.FromPixels(pixels, width, height, Aperture.PixelFormat.Rgba8))
                image.Save(png, new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png });
            files.Add(new { path = png, width, height, textureWidth = region.Width, textureHeight = region.Height, format = region.Format.ToString(), mip = region.MipLevel, layer = region.ArrayLayer });
        }
        return files;
    }

    private static object Summarize(DeepRecording recording)
    {
        Recording light = recording.Recording;
        IEnumerable<DeepPass> passes = recording.Groups.SelectMany(g => g.Passes);
        return new
        {
            executionId = recording.ExecutionId,
            graph = light.GraphName,
            device = light.DeviceName,
            backend = recording.Backend.ToString(),
            mode = recording.Mode.ToString(),
            groups = recording.Groups.Length,
            passes = passes.Count(),
            notReplayable = passes.Count(p => p.NotReplayable != null),
            draws = light.Groups.SelectMany(g => g.Passes).Sum(p => (long)p.Stats.Draws),
            dispatches = light.Groups.SelectMany(g => g.Passes).Sum(p => (long)p.Stats.Dispatches),
            gpuMilliseconds = Round(light.CommandBuffers.Where(b => !b.Name.StartsWith(CaptureCopy.BufferPrefix, StringComparison.Ordinal)).Sum(b => b.Milliseconds ?? 0)),
            resources = recording.Resources.Length,
            programs = recording.Programs.Length,
            blobBytes = recording.Blobs.Sum(b => (long)b.Data.Length),
        };
    }

    private static List<object> Groups(DeepRecording recording)
    {
        Dictionary<(int Group, int Pass), double> gpu = [];
        foreach (RecordedCommandBuffer buffer in recording.Recording.CommandBuffers)
            gpu[(buffer.GroupIndex, buffer.PassIndex)] = gpu.GetValueOrDefault((buffer.GroupIndex, buffer.PassIndex)) + (buffer.Milliseconds ?? 0);

        Dictionary<(int Group, int Pass), PassStats> stats = recording.Recording.Groups
            .SelectMany(g => g.Passes.Select(p => (Key: (g.Index, p.Index), p.Stats)))
            .ToDictionary(x => x.Key, x => x.Stats);

        return [.. recording.Groups.Select(g => (object)new
        {
            group = g.Index,
            name = g.Name,
            parent = g.ParentIndex,
            passes = g.Passes.Select(p =>
            {
                PassStats s = stats.GetValueOrDefault((g.Index, p.Index));
                return new
                {
                    pass = p.Index,
                    name = p.Name,
                    draws = s.Draws,
                    dispatches = s.Dispatches,
                    barriers = s.Barriers,
                    cpuMilliseconds = Round(s.CpuMilliseconds),
                    gpuMilliseconds = Round(gpu.GetValueOrDefault((g.Index, p.Index))),
                    events = p.Commands.Count(ReplayEvents.IsEvent),
                    writes = p.Accesses.Where(a => a.IsOutput).Select(a => a.Resource).ToList(),
                    reads = p.Accesses.Where(a => !a.IsOutput).Select(a => a.Resource).ToList(),
                    notReplayable = p.NotReplayable,
                };
            }).ToList(),
        })];
    }

    private static object DescribePass(DeepRecording recording, Names names, DeepPass pass, int from, int count)
    {
        List<object> commands = [];
        int eventIndex = 0;
        for (int i = 0; i < pass.Commands.Length; i++)
        {
            RecordedCommand command = pass.Commands[i];
            bool isEvent = ReplayEvents.IsEvent(command);
            if (i >= from && commands.Count < count)
                commands.Add(new { index = i, @event = isEvent ? eventIndex : (int?)null, what = Summarize(command, names, recording) });
            if (isEvent)
                eventIndex++;
        }

        return new
        {
            pass = pass.Name,
            index = pass.Index,
            notReplayable = pass.NotReplayable,
            accesses = pass.Accesses.Select(a => $"{(a.IsOutput ? "write" : "read")} {a.Resource} ({a.Kind}, {a.TextureUsage})").ToList(),
            touched = pass.References.Select(r => names.Of(r.Resource)).ToList(),
            copies = pass.Copies.Select(c => $"{c.Placement} {names.Of(c.Version.Resource)} v{c.Version.Version}").ToList(),
            commandCount = pass.Commands.Length,
            events = eventIndex,
            commands,
        };
    }

    private static string Summarize(RecordedCommand command, Names names, DeepRecording recording)
    {
        switch (command)
        {
            case SetFramebufferCommand f:
                string depth = f.Depth != null ? $" depth={names.Of(f.Depth.Texture.Resource)}" : "";
                return $"SetFramebuffer {f.Width}x{f.Height} colors=[{string.Join(", ", f.Colors.Select(c => names.Of(c.Texture.Resource)))}]{depth}";
            case SetPipelineCommand p:
                return $"SetPipeline {recording.Programs.FirstOrDefault(r => r.Key == p.Program)?.Name ?? "?"}{(p.Topology != null ? " " + p.Topology : "")}";
            case DrawCommand d: return $"Draw vertices={d.VertexCount} instances={d.InstanceCount}";
            case DrawIndexedCommand d: return $"DrawIndexed indices={d.IndexCount} instances={d.InstanceCount}";
            case DrawIndirectCommand d: return $"DrawIndirect {names.Of(d.Buffer.Resource)} draws={d.DrawCount}";
            case DrawIndexedIndirectCommand d: return $"DrawIndexedIndirect {names.Of(d.Buffer.Resource)} draws={d.DrawCount}";
            case DispatchCommand d: return $"Dispatch {d.GroupCountX}x{d.GroupCountY}x{d.GroupCountZ}";
            case ClearColorTargetCommand c: return $"ClearColorTarget {c.Index} {c.Color}";
            case ClearDepthStencilCommand c: return $"ClearDepthStencil depth={c.Depth} stencil={c.Stencil}";
            case SetViewportCommand v: return $"SetViewport {v.Viewport}";
            case SetScissorCommand s: return $"SetScissor {s.X},{s.Y} {s.Width}x{s.Height}";
            case SetPropertiesCommand s: return $"SetProperties changed=[{string.Join(", ", s.Changed.Select(c => c.Name))}] removed=[{string.Join(", ", s.Removed)}]";
            case BindVertexBuffersCommand b: return $"BindVertexBuffers [{string.Join(", ", b.Changed.Select(c => names.Of(c.Buffer.Resource)))}]";
            case BindIndexBufferCommand b: return $"BindIndexBuffer {names.Of(b.Buffer.Resource)} {b.Format}";
            case PushMarkerCommand m: return $"PushMarker {m.Name}";
            default: return command.GetType().Name.Replace("Command", "");
        }
    }

    private static object Describe(RecordedResource resource, Names names)
    {
        string name = names.Of(resource.Id);
        if (resource.Texture is { } t)
            return new { id = resource.Id.Value, name, kind = "Texture", origin = resource.Origin.ToString(), t.Width, t.Height, t.Depth, mips = t.MipLevels, layers = t.ArrayLayers, format = t.Format.ToString(), usage = t.Usage.ToString() };
        return new { id = resource.Id.Value, name, kind = resource.Kind.ToString(), origin = resource.Origin.ToString(), bytes = resource.Buffer?.SizeInBytes, usage = resource.Buffer?.Usage.ToString() };
    }

    private static object Describe(RecordedProgram program) => new
    {
        name = program.Name,
        compute = program.IsCompute,
        stages = program.Stages.Select(s => $"{s.Stage} {s.EntryPoint}").ToList(),
        resourceSets = program.Layouts.Length,
        vertexLayouts = program.VertexLayouts.Length,
        pushConstants = program.PushConstants.ToString(),
    };

    private static DeepPass FindPass(DeepRecording recording, int group, int pass)
    {
        DeepGroup instance = recording.Groups.FirstOrDefault(g => g.Index == group)
            ?? throw new CliException($"There is no group {group}. Groups: {string.Join(", ", recording.Groups.Select(g => $"{g.Index} {g.Name}"))}");
        return instance.Passes.FirstOrDefault(p => p.Index == pass)
            ?? throw new CliException($"Group {instance.Name} has no pass {pass}. Passes: {string.Join(", ", instance.Passes.Select(p => $"{p.Index} {p.Name}"))}");
    }

    private static string Resolve(string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Project.Current!.RootPath, path));

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = string.Concat(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c));
        return clean.Length == 0 ? "output" : clean;
    }

    private static double Round(double value) => Math.Round(value, 3);

    private sealed class Names
    {
        private readonly Dictionary<uint, RecordedResource> _resources = [];
        private readonly Dictionary<uint, string> _names = [];

        public Names(DeepRecording recording)
        {
            Dictionary<TraceResourceId, string> graphNames = [];
            foreach (RecordedGraphResource graph in recording.Groups.SelectMany(g => g.Resources))
                foreach (RecordedBacking backing in graph.Backings)
                    graphNames.TryAdd(backing.Id, backing.Role == BackingRole.Depth ? $"{graph.Name} (depth)" : graph.Name);

            foreach (RecordedResource resource in recording.Resources)
            {
                _resources[resource.Id.Value] = resource;
                _names[resource.Id.Value] = resource.Name.Length > 0 ? resource.Name : graphNames.GetValueOrDefault(resource.Id, $"resource {resource.Id.Value}");
            }
        }

        public string Of(TraceResourceId id) => _names.GetValueOrDefault(id.Value, $"resource {id.Value}");

        public RecordedResource? Resource(TraceResourceId id) => _resources.GetValueOrDefault(id.Value);
    }
}
