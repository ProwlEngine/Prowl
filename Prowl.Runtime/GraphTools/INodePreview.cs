// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Implement on a Node subclass to display a custom preview area on the node's body.
/// The default node renderer reserves a square region below the port list (or above,
/// per the node's preference) and asks the implementer to fill it via the editor's
/// preview-render pipeline. Concrete drawing is editor-side — Runtime only exposes the
/// declarative metadata so the data model stays renderer-free.
/// </summary>
/// <remarks>
/// Why an interface (not a base class): node types extend specific bases (Node,
/// IShaderGraphNode-implementing classes, etc.) and need to add preview support
/// independently. The editor's renderer detects the interface and routes through to
/// the per-node-type preview drawer registered the same way as <see cref="GraphTools"/>'
/// other extension registries.
/// </remarks>
public interface INodePreview
{
    /// <summary>True if a preview should be drawn this frame. Lets nodes hide their
    /// preview when there's nothing to show (e.g. shader node with no compilation
    /// result yet).</summary>
    bool HasPreview { get; }

    /// <summary>Desired height of the preview region in graph-space units. Width is
    /// fixed by the node renderer (typically the node body width). Returning ≤ 0
    /// hides the preview without changing the node height.</summary>
    float PreviewHeight { get; }
}
