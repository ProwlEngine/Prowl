// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Vector;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Static entry point for the render frame capture path. A capture is a one-shot: call
/// <see cref="RequestCapture"/> to arm it, and the next camera that runs its graph while armed stashes
/// a pending capture (retaining its pooled resource render textures instead of releasing them). The
/// heavy read-back cannot run while the main Graphite frame is open, so <see cref="FlushPendingCapture"/>
/// performs it once the frame has closed, builds the <see cref="RenderSnapshot"/>, and only then releases
/// the retained textures.
/// </summary>
public static class RenderProfiler
{
    private sealed class PendingCapture
    {
        public RenderFrameReport Report = new();
        public List<(string ResourceId, RenderTexture Texture)> Resources = new();
        public RenderPipeline.CameraSnapshot Camera;
    }

    private static bool s_armed;
    private static bool s_reportArmed;
    private static PendingCapture? s_pending;

    /// <summary>True while a capture has been requested but no camera has stashed it yet.</summary>
    public static bool IsCaptureArmed => s_armed;

    /// <summary>The most recently completed snapshot, or null if none has been captured this session.</summary>
    public static RenderSnapshot? LastSnapshot { get; private set; }

    /// <summary>Raised on <see cref="FlushPendingCapture"/> once a new snapshot is ready.</summary>
    public static event Action<RenderSnapshot>? SnapshotCaptured;

    /// <summary>Arms a one-shot capture of the next camera frame that renders.</summary>
    public static void RequestCapture() => s_armed = true;

    /// <summary>
    /// Arms a one-shot request for the next camera frame to build and publish a lightweight
    /// <see cref="RenderFrameReport"/> (timings, draw calls, counters) to <see cref="Camera.LastRenderReport"/>,
    /// without the heavy texture/geometry read-back a full <see cref="RequestCapture"/> does. Callers that
    /// want a continuous stream of reports (e.g. a live stats display) call this once per frame they need
    /// fresh data; the pipeline otherwise skips building a report entirely. This is the only mechanism
    /// the editor's render profiler UI uses to turn recording on - the runtime pipeline has no notion of
    /// "the editor is open".
    /// </summary>
    public static void RequestReport() => s_reportArmed = true;

    /// <summary>Consumes the one-shot report request, if any. Called once per camera execution.</summary>
    internal static bool ConsumeReportRequest()
    {
        if (!s_reportArmed)
            return false;

        s_reportArmed = false;
        return true;
    }

    /// <summary>
    /// Consumes the armed one-shot and stashes a pending capture for the flush to process. Snapshots the
    /// resource id -> render texture mapping so the pipeline can retain (not release) those textures.
    /// Returns true when the caller must skip releasing the graph's pooled render textures; the flush
    /// releases them instead.
    /// </summary>
    internal static bool TryBeginCapture(RenderFrameReport report,
        IReadOnlyDictionary<RenderResourceID, RenderTexture> resources, RenderPipeline.CameraSnapshot camera)
    {
        if (!s_armed || s_pending != null)
            return false;

        s_armed = false;

        var pending = new PendingCapture
        {
            Report = report,
            Camera = camera,
        };

        foreach (KeyValuePair<RenderResourceID, RenderTexture> entry in resources)
        {
            string id = RenderResourceID.NameOf(entry.Key) ?? entry.Key.ToString();
            pending.Resources.Add((id, entry.Value));
        }

        s_pending = pending;
        return true;
    }

    /// <summary>
    /// Performs the deferred read-back for a pending capture and assembles the <see cref="RenderSnapshot"/>.
    /// A no-op when nothing is pending. Must only run outside an open main frame: the texture read-back
    /// blocks on a GPU copy and Graphite frames cannot nest, so when <see cref="Graphics.CurrentFrame"/>
    /// is non-null this defers to a later call.
    /// </summary>
    public static void FlushPendingCapture()
    {
        PendingCapture? pending = s_pending;
        if (pending == null)
            return;

        if (Graphics.CurrentFrame != null)
            return;

        s_pending = null;

        var snapshot = new RenderSnapshot
        {
            Report = pending.Report,
            Camera = new CapturedCamera
            {
                View = pending.Camera.View,
                Projection = pending.Camera.Projection,
                Position = pending.Camera.CameraPosition,
                PixelWidth = (int)pending.Camera.PixelWidth,
                PixelHeight = (int)pending.Camera.PixelHeight,
            },
        };

        foreach ((string resourceId, RenderTexture texture) in pending.Resources)
        {
            SnapshotTexture? captured = CaptureTexture(resourceId, texture);
            if (captured != null)
                snapshot.Textures.Add(captured);
        }

        CaptureGeometry(pending.Report, snapshot.Geometry);

        foreach ((_, RenderTexture texture) in pending.Resources)
            RenderTexture.ReleaseTemporaryRT(texture);

        LastSnapshot = snapshot;
        SnapshotCaptured?.Invoke(snapshot);
    }

    private static SnapshotTexture? CaptureTexture(string resourceId, RenderTexture rt)
    {
        Texture2D? source = null;
        bool isDepth = false;

        if (rt.InternalTextures.Length > 0)
        {
            source = rt.MainTexture;
        }
        else if (rt.InternalDepth != null)
        {
            source = rt.InternalDepth;
            isDepth = true;
        }

        if (source == null)
            return null;

        var pixels = new byte[source.GetSize()];
        source.GetData<byte>(pixels);

        return new SnapshotTexture
        {
            ResourceId = resourceId,
            Width = (int)source.Width,
            Height = (int)source.Height,
            Depth = 1,
            Format = source.ImageFormat,
            IsDepth = isDepth,
            Pixels = pixels,
        };
    }

    private static void CaptureGeometry(RenderFrameReport report, List<SnapshotGeometry> output)
    {
        var seen = new HashSet<(Guid, int)>();

        foreach (PassReport pass in report.Passes)
        {
            foreach (DrawCallReport call in pass.DrawCalls)
            {
                if (call.MeshGuid == Guid.Empty)
                    continue;

                if (!seen.Add((call.MeshGuid, call.SubMeshIndex)))
                    continue;

                SnapshotGeometry? geometry = BuildGeometry(call);
                if (geometry != null)
                    output.Add(geometry);
            }
        }
    }

    private static SnapshotGeometry? BuildGeometry(DrawCallReport call)
    {
        if (AssetDatabase.GetCached(call.MeshGuid) is not Mesh mesh)
            return null;

        if (!mesh.isReadable)
            return null;

        Float3[] verts = mesh.Vertices;
        if (verts.Length == 0)
            return null;

        uint[] allIndices = mesh.Indices;

        int start = 0;
        int count = allIndices.Length;
        if (call.SubMeshIndex >= 0 && call.SubMeshIndex < mesh.SubMeshCount)
        {
            SubMeshDescriptor sub = mesh.GetSubMesh(call.SubMeshIndex);
            start = sub.IndexStart;
            count = sub.IndexCount;
        }

        if (start < 0 || count < 0 || start + count > allIndices.Length)
        {
            start = 0;
            count = allIndices.Length;
        }

        var positions = new float[verts.Length * 3];
        for (int i = 0; i < verts.Length; i++)
        {
            positions[i * 3 + 0] = verts[i].X;
            positions[i * 3 + 1] = verts[i].Y;
            positions[i * 3 + 2] = verts[i].Z;
        }

        var indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = (int)allIndices[start + i];

        return new SnapshotGeometry
        {
            MeshGuid = call.MeshGuid,
            SubMeshIndex = call.SubMeshIndex,
            Name = call.MeshName,
            Positions = positions,
            Indices = indices,
        };
    }

    /// <summary>Serializes a snapshot to a single Echo binary blob (.rendersnapshot).</summary>
    public static void Save(RenderSnapshot snapshot, string path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EchoObject root = Serializer.Serialize(snapshot);
        using var writer = new BinaryWriter(File.Create(path));
        root.WriteToBinary(writer);
    }

    /// <summary>Loads a snapshot previously written by <see cref="Save"/>.</summary>
    public static RenderSnapshot Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        EchoObject root = EchoObject.ReadFromBinary(reader);
        return Serializer.Deserialize<RenderSnapshot>(root);
    }
}
