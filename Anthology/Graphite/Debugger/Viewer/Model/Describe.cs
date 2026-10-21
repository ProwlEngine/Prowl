using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Graphite.Debugger.Trace;

namespace Prowl.Graphite.Debugger.Viewer;

internal static class Describe
{
    private static readonly Regex ChannelToken = new(@"^([RGBA])(\d+)$", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<PixelFormat, string> FormatNames = new();

    private static readonly Dictionary<PixelFormat, string> DepthNames = new()
    {
        [PixelFormat.D16_UNorm] = "D16",
        [PixelFormat.D32_Float] = "D32F",
        [PixelFormat.D24_UNorm_S8_UInt] = "D24S8",
        [PixelFormat.D32_Float_S8_UInt] = "D32FS8",
    };

    public static string Format(PixelFormat format) => FormatNames.GetOrAdd(format, ShortFormat);

    private static string ShortFormat(PixelFormat format)
    {
        if (DepthNames.TryGetValue(format, out string? depth))
            return depth;

        var prefix = new List<string>();
        var channels = new StringBuilder();
        string? lastBits = null;
        string type = "";
        bool srgb = false;
        foreach (string token in format.ToString().Split('_'))
        {
            Match m = ChannelToken.Match(token);
            if (m.Success)
            {
                if (lastBits != null && lastBits != m.Groups[2].Value)
                    channels.Append(lastBits);
                lastBits = m.Groups[2].Value;
                channels.Append(m.Groups[1].Value);
                continue;
            }
            switch (token)
            {
                case "UNorm": type = "(0, 1)"; break;
                case "SNorm": type = "(-1, 1)"; break;
                case "Float": type = "F"; break;
                case "UInt": type = "uint"; break;
                case "SInt": type = "sint"; break;
                case "SRgb": srgb = true; break;
                default: prefix.Add(token); break;
            }
        }
        if (lastBits != null)
            channels.Append(lastBits);

        var name = new StringBuilder(string.Join(" ", prefix));
        if (channels.Length > 0)
            name.Append(name.Length > 0 ? " " : "").Append(channels);
        if (type == "F")
            name.Append('F');
        else if (srgb)
            name.Append(" sRGB");
        else if (type.Length > 0)
            name.Append(' ').Append(type);
        return name.ToString();
    }

    public static string Key(GraphInstanceKey key) => key.Kind == GraphKind.Root ? key.Kind.Name : $"{key.Kind.Name} #{key.Id}";

    public static string Handle(int index, int generation) => generation > 0 ? $"{index}:{generation}" : "-";

    public static string Renderable(Prowl.Graphite.Rendering.DrawnRenderable renderable)
        => renderable.Name ?? (renderable.Handle.IsValid ? $"Renderable {renderable.Handle.Index}" : "Provider or batch item");

    public static string Position(Prowl.Vector.Double4x4 transform)
        => $"{transform.c3.X:0.##}, {transform.c3.Y:0.##}, {transform.c3.Z:0.##}";

    public static string Payload(object? payload) => payload == null ? "-" : payload.GetType().Name;

    public static string PayloadValue(object? payload) => payload?.ToString() ?? "None, or dropped when the snapshot was saved";

    public static string Name(VertexAttributeID id) => VertexAttributeID.ToString(id) ?? id.ToString();

    public static string Name(PropertyID id) => PropertyID.ToString(id) ?? id.ToString();

    public static IEnumerable<VertexElementDescription> Elements(EquatableArray<VertexLayoutDescription> layouts)
        => layouts.SelectMany(l => l.Elements ?? []);

    public static IEnumerable<(uint Set, ResourceLayoutElementDescription Element)> Elements(EquatableArray<ResourceLayoutDescription> layouts)
        => layouts.SelectMany(l => (l.Elements ?? []).Select(e => (l.Set, e)));

    public static int Components(VertexElementFormat format)
    {
        string name = format.ToString().Replace("_Norm", "");
        return name.EndsWith('4') ? 4 : name.EndsWith('3') ? 3 : name.EndsWith('2') ? 2 : 1;
    }

    public static string Color(BlendAttachmentDescription b) => $"{b.SourceColorFactor} / {b.ColorFunction} / {b.DestinationColorFactor}";

    public static string Alpha(BlendAttachmentDescription b) => $"{b.SourceAlphaFactor} / {b.AlphaFunction} / {b.DestinationAlphaFactor}";

    public static string Stencil(StencilBehaviorDescription s) => $"{s.Fail} / {s.Pass} / {s.DepthFail} / {s.Comparison}";

    public static string Outputs(OutputDescription o)
    {
        string colors = o.ColorFormats is { Length: > 0 } c ? string.Join(", ", c.Select(Format)) : "none";
        return o.DepthFormat is { } depth ? $"{colors} + {Format(depth)}" : colors;
    }

    public static string Ops(AttachmentOps ops) => $"{ops.Load} / {ops.Store}";

    public static int IndexSize(IndexFormat format) => format == IndexFormat.UInt32 ? 4 : 2;
}
