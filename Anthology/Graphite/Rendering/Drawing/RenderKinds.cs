namespace Prowl.Graphite.Rendering;

internal static class RenderKinds
{
    public static readonly GraphKind World = GraphKind.Intern("Graphite.World");
    public static readonly GraphKind Camera = GraphKind.Intern("Graphite.Camera");
    public static readonly GraphKind SkyCapture = GraphKind.Intern("Graphite.SkyCapture");
    public static readonly GraphKind ProbeCapture = GraphKind.Intern("Graphite.ProbeCapture");

    public static readonly GraphScope[] Scopes =
    [
        new(World, GraphKind.Root),
        new(Camera, World),
        new(SkyCapture, World),
        new(ProbeCapture, World),
    ];
}
