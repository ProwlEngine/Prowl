using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.Importer;

/// <summary>
/// Per-format importer: produces an <see cref="IntermediateScene"/> from a byte source, which the
/// post-process pipeline then transforms into the public <see cref="Model"/>.
/// </summary>
internal interface IModelFormat
{
    /// <summary>Format token this importer handles (e.g. <c>"gltf"</c>).</summary>
    string Token { get; }

    /// <summary>True when this importer can handle the supplied format token.</summary>
    bool CanRead(string formatToken);

    /// <summary>
    /// Reads the source into an <see cref="IntermediateScene"/>. The source may be either
    /// <see cref="ImportContext.SourcePath"/> (when not null) or <paramref name="stream"/>.
    /// </summary>
    IntermediateScene Read(Stream stream, ImportContext context);
}
