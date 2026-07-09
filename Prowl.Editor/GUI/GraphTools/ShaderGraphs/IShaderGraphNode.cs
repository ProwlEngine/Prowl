// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>
/// Marker interface any <see cref="Node"/> subclass that implements this is eligible
/// to appear in <see cref="ShaderGraph"/>'s node-creation menu. A node can implement
/// multiple graph-type markers (e.g. a generic Math node usable in both shader and
/// visual-script graphs) by listing each interface; the registry will surface it in
/// every matching menu.
/// </summary>
public interface IShaderGraphNode { }
